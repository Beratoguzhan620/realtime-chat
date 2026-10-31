using RealtimeChat.Core.Messages;

namespace RealtimeChat.Api.Features.Messages;

public sealed record MessageResponse(
    Guid Id,
    Guid ConversationId,
    long Seq,
    Guid SenderId,
    Guid ClientMessageId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt)
{
    public static MessageResponse From(Message message) => new(
        message.Id, message.ConversationId, message.Seq, message.SenderId, message.ClientMessageId,
        message.Body, message.CreatedAt, message.EditedAt, message.DeletedAt);
}

/// <summary>A page of messages in ascending order. <c>HasMore</c> says whether there are more in the direction asked.</summary>
public sealed record MessagePage(IReadOnlyList<MessageResponse> Messages, bool HasMore);

/// <summary>Someone has read a conversation up to <c>Seq</c>.</summary>
public sealed record ReadReceipt(Guid ConversationId, Guid UserId, long Seq);

public sealed record SendResult(MessageResponse Message, bool Replayed);
