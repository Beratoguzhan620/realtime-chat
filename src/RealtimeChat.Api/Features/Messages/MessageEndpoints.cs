using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RealtimeChat.Api.Common;

namespace RealtimeChat.Api.Features.Messages;

public sealed record SendMessageRequest(Guid ClientMessageId, string Body);

public sealed record EditMessageRequest(string Body);

public sealed record MarkReadRequest(long Seq);

/// <summary>
/// The REST side of messaging: history, catching up, and sending for clients that cannot keep a connection
/// open. Live clients send through the hub; both end in <see cref="MessageService"/>.
/// </summary>
internal static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Messages").RequireAuthorization();

        group.MapGet("/conversations/{id:guid}/messages", Page)
            .WithSummary("History, oldest first within the page")
            .WithDescription("`after=n` returns what came after message n (catching up after a reconnect); `before=n` pages back; neither returns the newest messages.");
        group.MapPost("/conversations/{id:guid}/messages", Send)
            .WithSummary("Send a message")
            .WithDescription("`clientMessageId` is chosen by the client. Sending the same one again returns the stored message with 200 instead of a copy.");
        group.MapPatch("/messages/{id:guid}", Edit).WithSummary("Change your message");
        group.MapDelete("/messages/{id:guid}", Delete).WithSummary("Delete your message; its place in the conversation stays");
        group.MapPost("/conversations/{id:guid}/read", MarkRead).WithSummary("Mark the conversation read up to a message");
        return app;
    }

    private static async Task<Ok<MessagePage>> Page(
        Guid id, ClaimsPrincipal user, MessageService messages, CancellationToken ct, long? before = null, long? after = null, int limit = MessageService.DefaultPageSize) =>
        TypedResults.Ok(await messages.PageAsync(user.UserId(), id, before, after, limit, ct));

    private static async Task<Results<Created<MessageResponse>, Ok<MessageResponse>>> Send(
        Guid id, SendMessageRequest request, ClaimsPrincipal user, MessageService messages, CancellationToken ct)
    {
        var result = await messages.SendAsync(user.UserId(), id, request.ClientMessageId, request.Body, ct);
        return result.Replayed
            ? TypedResults.Ok(result.Message)
            : TypedResults.Created($"/api/conversations/{id}/messages?after={result.Message.Seq - 1}&limit=1", result.Message);
    }

    private static async Task<Ok<MessageResponse>> Edit(Guid id, EditMessageRequest request, ClaimsPrincipal user, MessageService messages, CancellationToken ct) =>
        TypedResults.Ok(await messages.EditAsync(user.UserId(), id, request.Body, ct));

    private static async Task<Ok<MessageResponse>> Delete(Guid id, ClaimsPrincipal user, MessageService messages, CancellationToken ct) =>
        TypedResults.Ok(await messages.DeleteAsync(user.UserId(), id, ct));

    private static async Task<NoContent> MarkRead(Guid id, MarkReadRequest request, ClaimsPrincipal user, MessageService messages, CancellationToken ct)
    {
        await messages.MarkReadAsync(user.UserId(), id, request.Seq, ct);
        return TypedResults.NoContent();
    }
}
