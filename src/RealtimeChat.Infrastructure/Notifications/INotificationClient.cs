namespace RealtimeChat.Infrastructure.Notifications;

/// <summary>One "you have unread messages" email.</summary>
public sealed record UnreadEmail(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid ConversationId,
    string ConversationTitle,
    long UnreadCount,
    long LastSeq)
{
    /// <summary>The same email for the same unread message is the same request, whoever sends it and however often.</summary>
    public string IdempotencyKey => $"chat-unread-{ConversationId:N}-{UserId:N}-{LastSeq}";
}

public interface INotificationClient
{
    Task SendAsync(UnreadEmail email, CancellationToken cancellationToken);
}
