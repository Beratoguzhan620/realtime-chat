using RealtimeChat.Api.Features.Messages;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>The events the server sends to connected clients.</summary>
public interface IChatClient
{
    Task MessageReceived(MessageResponse message);

    Task MessageUpdated(MessageResponse message);

    Task ReadUpdated(ReadReceipt receipt);

    /// <summary>A conversation was created, renamed or its people changed; fetch it again.</summary>
    Task ConversationChanged(Guid conversationId);
}
