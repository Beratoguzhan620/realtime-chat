using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using RealtimeChat.Api.Common;
using RealtimeChat.Infrastructure.Presence;

namespace RealtimeChat.Api.Features.Hub;

internal static class PresenceEndpoints
{
    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/presence", Online)
            .WithTags("Presence")
            .RequireAuthorization()
            .WithSummary("Who of the people you share a conversation with is online now")
            .WithDescription("Changes after this arrive as `PresenceChanged` events on the hub.");
        return app;
    }

    private static async Task<Ok<List<Guid>>> Online(ClaimsPrincipal user, PresenceNotifier notifier, PresenceTracker presence, CancellationToken ct)
    {
        var contacts = await notifier.ContactsAsync(user.UserId(), ct);
        var online = await presence.OnlineAsync(contacts);
        return TypedResults.Ok(online.ToList());
    }
}
