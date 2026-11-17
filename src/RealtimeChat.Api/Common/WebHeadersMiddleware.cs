namespace RealtimeChat.Api.Common;

/// <summary>
/// The headers nginx adds in Compose, for when this app serves the web pages itself (the one-container demo). Message
/// text is rendered as text; the policy is a second line of defence.
/// </summary>
public sealed class WebHeadersMiddleware(RequestDelegate next)
{
    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        if (!IsApi(context.Request.Path))
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = Policy;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
        }

        return next(context);
    }

    public static bool IsApi(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi")
        || path.StartsWithSegments("/health");
}
