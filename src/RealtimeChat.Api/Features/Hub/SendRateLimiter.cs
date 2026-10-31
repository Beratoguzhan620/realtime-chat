using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using RealtimeChat.Core;

namespace RealtimeChat.Api.Features.Hub;

public sealed class ChatLimits
{
    public const string Section = "Chat";

    /// <summary>Messages a person may send per second, on average.</summary>
    public int MessagesPerSecond { get; set; } = 5;

    /// <summary>How many messages may be sent at once before the rate applies.</summary>
    public int MessageBurst { get; set; } = 20;
}

/// <summary>
/// A token bucket per person for sending, shared by the hub and the REST endpoint. It lives in each instance,
/// so with N instances someone could send at most N times the rate; that bounds abuse without a Redis round
/// trip on every message.
/// </summary>
internal sealed class SendRateLimiter(IOptions<ChatLimits> options)
{
    private readonly ConcurrentDictionary<Guid, TokenBucketRateLimiter> _buckets = new();

    public void Take(Guid userId)
    {
        var bucket = _buckets.GetOrAdd(userId, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.Value.MessageBurst,
            TokensPerPeriod = options.Value.MessagesPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

        using var lease = bucket.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            throw new DomainException(ErrorCodes.RateLimited, "You are sending too fast; wait a moment.");
        }
    }
}
