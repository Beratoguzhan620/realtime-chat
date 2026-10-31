using System.Collections.Concurrent;
using RealtimeChat.Infrastructure.Notifications;

namespace RealtimeChat.IntegrationTests.Support;

/// <summary>Stands in for the notification-service and keeps every email request it gets.</summary>
public sealed class RecordingNotificationClient : INotificationClient
{
    public ConcurrentQueue<UnreadEmail> Sent { get; } = new();

    public Task SendAsync(UnreadEmail email, CancellationToken cancellationToken)
    {
        Sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public List<UnreadEmail> For(Guid userId) => Sent.Where(e => e.UserId == userId).ToList();
}
