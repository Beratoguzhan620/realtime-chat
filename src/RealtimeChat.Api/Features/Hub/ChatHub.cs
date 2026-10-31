using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RealtimeChat.Api.Common;
using RealtimeChat.Api.Features.Messages;
using RealtimeChat.Infrastructure.Presence;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>
/// The live side. Clients connect to <c>/hubs/chat</c> with their access token and call these methods;
/// the server pushes <see cref="IChatClient"/> events to every connection of every member, on any instance.
/// </summary>
[Authorize]
internal sealed class ChatHub(MessageService messages, PresenceTracker presence, PresenceNotifier notifier, ChatEvents events, TimeProvider clock)
    : Hub<IChatClient>
{
    private const string LastTypingKey = "last-typing";
    private static readonly TimeSpan TypingThrottle = TimeSpan.FromSeconds(1);

    public override async Task OnConnectedAsync()
    {
        if (await presence.ConnectAsync(UserId, Context.ConnectionId))
        {
            await notifier.ChangedAsync(UserId, online: true, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (await presence.DisconnectAsync(UserId, Context.ConnectionId))
        {
            await notifier.ChangedAsync(UserId, online: false, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<MessageResponse> Send(Guid conversationId, Guid clientMessageId, string body) =>
        (await messages.SendAsync(UserId, conversationId, clientMessageId, body, Context.ConnectionAborted)).Message;

    public Task<MessageResponse> Edit(Guid messageId, string body) =>
        messages.EditAsync(UserId, messageId, body, Context.ConnectionAborted);

    public Task<MessageResponse> Delete(Guid messageId) =>
        messages.DeleteAsync(UserId, messageId, Context.ConnectionAborted);

    public Task<long?> MarkRead(Guid conversationId, long seq) =>
        messages.MarkReadAsync(UserId, conversationId, seq, Context.ConnectionAborted);

    /// <summary>Keeps this connection counted as online. Clients call it every 20 seconds; false means "reconnect".</summary>
    public Task<bool> Heartbeat() => presence.HeartbeatAsync(UserId, Context.ConnectionId);

    /// <summary>
    /// Tells the other members this person is typing. Nothing is stored. Repeats within a second from the same
    /// connection are dropped, so a client that sends one per keystroke cannot flood anyone.
    /// </summary>
    public async Task Typing(Guid conversationId)
    {
        var now = clock.GetUtcNow();
        if (Context.Items.TryGetValue(LastTypingKey, out var last) && last is DateTimeOffset previous && now - previous < TypingThrottle)
        {
            return;
        }

        Context.Items[LastTypingKey] = now;
        var members = await messages.MemberIdsAsync(conversationId, UserId, Context.ConnectionAborted);
        await events.Typing(members.Where(m => m != UserId), new TypingEvent(conversationId, UserId));
    }

    private Guid UserId => Context.User!.UserId();
}
