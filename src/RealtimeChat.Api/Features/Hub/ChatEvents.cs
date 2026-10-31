using Microsoft.AspNetCore.SignalR;
using RealtimeChat.Api.Features.Messages;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>
/// Sends events to the members of a conversation, by user id. A user's every connection (tabs, phones) gets
/// them, on whichever instance it is connected to, so there are no SignalR groups to keep in sync with
/// membership.
/// </summary>
internal sealed class ChatEvents(IHubContext<ChatHub, IChatClient> hub)
{
    public Task MessageReceived(IEnumerable<Guid> members, MessageResponse message) =>
        Users(members).MessageReceived(message);

    public Task MessageUpdated(IEnumerable<Guid> members, MessageResponse message) =>
        Users(members).MessageUpdated(message);

    public Task ReadUpdated(IEnumerable<Guid> members, ReadReceipt receipt) =>
        Users(members).ReadUpdated(receipt);

    public Task ConversationChanged(IEnumerable<Guid> members, Guid conversationId) =>
        Users(members).ConversationChanged(conversationId);

    public Task Typing(IEnumerable<Guid> members, TypingEvent typing) =>
        Users(members).Typing(typing);

    public Task PresenceChanged(IEnumerable<Guid> contacts, PresenceEvent presence) =>
        Users(contacts).PresenceChanged(presence);

    private IChatClient Users(IEnumerable<Guid> members) =>
        hub.Clients.Users(members.Select(m => m.ToString()).ToList());
}
