using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RealtimeChat.Api.Common;
using RealtimeChat.Api.Features.Messages;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>
/// The live side. Clients connect to <c>/hubs/chat</c> with their access token and call these methods;
/// the server pushes <see cref="IChatClient"/> events to every connection of every member.
/// </summary>
[Authorize]
internal sealed class ChatHub(MessageService messages) : Hub<IChatClient>
{
    public async Task<MessageResponse> Send(Guid conversationId, Guid clientMessageId, string body) =>
        (await messages.SendAsync(UserId, conversationId, clientMessageId, body, Context.ConnectionAborted)).Message;

    public Task<MessageResponse> Edit(Guid messageId, string body) =>
        messages.EditAsync(UserId, messageId, body, Context.ConnectionAborted);

    public Task<MessageResponse> Delete(Guid messageId) =>
        messages.DeleteAsync(UserId, messageId, Context.ConnectionAborted);

    public Task<long?> MarkRead(Guid conversationId, long seq) =>
        messages.MarkReadAsync(UserId, conversationId, seq, Context.ConnectionAborted);

    private Guid UserId => Context.User!.UserId();
}
