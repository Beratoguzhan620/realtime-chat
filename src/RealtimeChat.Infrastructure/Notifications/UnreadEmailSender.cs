using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealtimeChat.Infrastructure.Persistence;
using RealtimeChat.Infrastructure.Presence;

namespace RealtimeChat.Infrastructure.Notifications;

/// <summary>
/// Every minute: finds members with unread messages in a conversation that has been quiet for a while, skips
/// anyone who is online, and sends each of the rest one email. The member's <c>notified_seq</c> remembers
/// it, so the next email comes only after new messages and another quiet period. Several instances may run
/// this at once; the idempotency key makes the notification-service send each email once anyway.
/// </summary>
public sealed partial class UnreadEmailSender(
    IServiceScopeFactory scopes,
    PresenceTracker presence,
    INotificationClient client,
    IOptions<UnreadEmailOptions> options,
    TimeProvider clock,
    ILogger<UnreadEmailSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var cutoff = clock.GetUtcNow() - options.Value.QuietPeriod;

        var candidates = await CandidatesAsync(db, cutoff, ct);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var online = await presence.OnlineAsync(candidates.Select(c => c.UserId));
        var sent = 0;
        foreach (var candidate in candidates.Where(c => !online.Contains(c.UserId)))
        {
            await client.SendAsync(new UnreadEmail(
                candidate.UserId, candidate.Email, candidate.DisplayName, candidate.ConversationId,
                candidate.Title, candidate.UnreadCount, candidate.LastSeq), ct);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE members SET notified_seq = {candidate.LastSeq}
                WHERE conversation_id = {candidate.ConversationId} AND user_id = {candidate.UserId} AND notified_seq < {candidate.LastSeq}
                """, ct);
            sent++;
        }

        if (sent > 0)
        {
            LogSent(logger, sent);
        }

        return sent;
    }

    /// <summary>Members with unread messages in a conversation that has been quiet since before <paramref name="cutoff"/>.</summary>
    private static async Task<List<UnreadCandidate>> CandidatesAsync(ChatDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT m.conversation_id, m.user_id, u.email, u.display_name, c.last_seq, c.last_seq - m.last_read_seq,
                   COALESCE(c.title, sender.display_name)
            FROM members m
            JOIN conversations c ON c.id = m.conversation_id
            JOIN users u ON u.id = m.user_id
            JOIN messages last ON last.conversation_id = c.id AND last.seq = c.last_seq
            JOIN users sender ON sender.id = last.sender_id
            WHERE c.last_seq > m.last_read_seq
              AND c.last_seq > m.notified_seq
              AND c.last_activity_at < @cutoff
            ORDER BY c.last_activity_at
            LIMIT 200
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "cutoff";
        parameter.Value = cutoff;
        command.Parameters.Add(parameter);

        var candidates = new List<UnreadCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            candidates.Add(new UnreadCandidate(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetString(6)));
        }

        return candidates;
    }

    private sealed record UnreadCandidate(Guid ConversationId, Guid UserId, string Email, string DisplayName, long LastSeq, long UnreadCount, string Title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {Count} unread-message emails")]
    private static partial void LogSent(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending unread-message emails failed; it runs again at the next check")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Used when no notification-service is configured: nothing leaves the service.</summary>
internal sealed partial class DisabledNotificationClient(ILogger<DisabledNotificationClient> logger) : INotificationClient
{
    public Task SendAsync(UnreadEmail email, CancellationToken cancellationToken)
    {
        LogSkipped(logger, email.ConversationId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Unread email for conversation {ConversationId} skipped: no notification-service configured")]
    private static partial void LogSkipped(ILogger logger, Guid conversationId);
}
