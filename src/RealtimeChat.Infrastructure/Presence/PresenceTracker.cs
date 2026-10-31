using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace RealtimeChat.Infrastructure.Presence;

/// <summary>
/// Who is online, shared by every instance through Redis. Each user has a sorted set of their connection ids,
/// scored by when each connection expires; a heartbeat pushes the score forward. A user is online while the
/// set has a live connection, so two tabs and a phone count as one person, closing one tab changes nothing,
/// and a server that crashes takes its connections offline when they expire instead of leaving them online
/// forever. Every change is one Lua script, so concurrent connects and disconnects cannot interleave.
/// </summary>
public sealed class PresenceTracker(IConnectionMultiplexer redis, IOptions<PresenceOptions> options, TimeProvider clock)
{
    private const string UsersKey = "presence:users";

    // Returns how many live connections the user had before this one.
    private static readonly LuaScript Connect = LuaScript.Prepare("""
        redis.call('ZREMRANGEBYSCORE', @user, '-inf', @now)
        local before = redis.call('ZCARD', @user)
        redis.call('ZADD', @user, @expires, @connection)
        redis.call('PEXPIREAT', @user, @expires, 'GT')
        if redis.call('PTTL', @user) < 0 then redis.call('PEXPIREAT', @user, @expires) end
        redis.call('ZADD', @users, 'GT', @expires, @userId)
        return before
        """);

    // Returns how many live connections are left.
    private static readonly LuaScript Disconnect = LuaScript.Prepare("""
        redis.call('ZREM', @user, @connection)
        redis.call('ZREMRANGEBYSCORE', @user, '-inf', @now)
        local left = redis.call('ZCARD', @user)
        if left == 0 then redis.call('ZREM', @users, @userId) end
        return left
        """);

    // Returns 1 when the connection was still known.
    private static readonly LuaScript Heartbeat = LuaScript.Prepare("""
        if redis.call('ZSCORE', @user, @connection) == false then return 0 end
        redis.call('ZADD', @user, @expires, @connection)
        redis.call('PEXPIREAT', @user, @expires, 'GT')
        redis.call('ZADD', @users, 'GT', @expires, @userId)
        return 1
        """);

    /// <summary>Records a connection. Returns true when the user just came online.</summary>
    public async Task<bool> ConnectAsync(Guid userId, string connectionId)
    {
        var before = (long)await Database.ScriptEvaluateAsync(Connect, Arguments(userId, connectionId));
        return before == 0;
    }

    /// <summary>Forgets a connection. Returns true when the user just went offline.</summary>
    public async Task<bool> DisconnectAsync(Guid userId, string connectionId)
    {
        var left = (long)await Database.ScriptEvaluateAsync(Disconnect, Arguments(userId, connectionId));
        return left == 0;
    }

    /// <summary>Keeps a connection alive. Returns false when it had already expired; the client should reconnect.</summary>
    public async Task<bool> HeartbeatAsync(Guid userId, string connectionId) =>
        (long)await Database.ScriptEvaluateAsync(Heartbeat, Arguments(userId, connectionId)) == 1;

    public async Task<HashSet<Guid>> OnlineAsync(IEnumerable<Guid> userIds)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var ids = userIds.Distinct().ToList();
        var batch = Database.CreateBatch();
        var counts = ids.Select(id => batch.SortedSetLengthAsync(KeyFor(id), now, double.PositiveInfinity, Exclude.Start)).ToList();
        batch.Execute();
        await Task.WhenAll(counts);
        return ids.Where((_, i) => counts[i].Result > 0).ToHashSet();
    }

    /// <summary>
    /// Finds users whose last connection expired without a disconnect (a crashed server, a lost network) and
    /// takes them offline. Returns those users.
    /// </summary>
    public async Task<List<Guid>> SweepAsync()
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var stale = await Database.SortedSetRangeByScoreAsync(UsersKey, double.NegativeInfinity, now, take: 500);
        var offline = new List<Guid>();
        foreach (var value in stale)
        {
            if (!Guid.TryParse(value.ToString(), out var userId))
            {
                await Database.SortedSetRemoveAsync(UsersKey, value);
                continue;
            }

            var left = (long)await Database.ScriptEvaluateAsync(Disconnect, Arguments(userId, string.Empty));
            if (left == 0)
            {
                offline.Add(userId);
            }
        }

        return offline;
    }

    private IDatabase Database => redis.GetDatabase();

    private static RedisKey KeyFor(Guid userId) => $"presence:user:{userId:N}";

    private object Arguments(Guid userId, string connectionId)
    {
        var now = clock.GetUtcNow();
        return new
        {
            user = KeyFor(userId),
            users = (RedisKey)UsersKey,
            userId = userId.ToString(),
            connection = connectionId,
            now = now.ToUnixTimeMilliseconds(),
            expires = now.Add(options.Value.ConnectionTimeout).ToUnixTimeMilliseconds(),
        };
    }
}
