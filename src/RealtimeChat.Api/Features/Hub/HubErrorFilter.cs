using Microsoft.AspNetCore.SignalR;
using RealtimeChat.Core;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>
/// Hub methods throw the same <see cref="DomainException"/>s as the REST API. They reach the client as
/// <c>"code: message"</c>; anything else stays a generic error so no internals leak.
/// </summary>
internal sealed class HubErrorFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (DomainException ex)
        {
            throw new HubException($"{ex.Code}: {ex.Message}");
        }
    }
}
