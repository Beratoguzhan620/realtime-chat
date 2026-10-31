using RealtimeChat.Api.Features.Messages;

namespace RealtimeChat.Api.Features.Hub;

public sealed record TypingEvent(Guid ConversationId, Guid UserId);

public sealed record PresenceEvent(Guid UserId, bool Online);

/// <summary>The events the server sends to connected clients.</summary>
public interface IChatClient
{
    Task MessageReceived(MessageResponse message);

    Task MessageUpdated(MessageResponse message);

    Task ReadUpdated(ReadReceipt receipt);

    /// <summary>A conversation was created, renamed or its people changed; fetch it again.</summary>
    Task ConversationChanged(Guid conversationId);

    /// <summary>Someone is typing in a conversation; show it for a few seconds unless it is repeated.</summary>
    Task Typing(TypingEvent typing);

    /// <summary>Someone you share a conversation with came online or went offline.</summary>
    Task PresenceChanged(PresenceEvent presence);
}
