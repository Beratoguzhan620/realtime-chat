using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RealtimeChat.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Realtime Chat",
            Version = "1.0",
            Description = """
                Direct and group conversations with live delivery over SignalR. The REST API covers accounts,
                conversations and history; the hub at `/hubs/chat` carries messages, typing, read receipts and presence.

                **Getting started**

                1. Register at `POST /api/auth/register` and sign in at `POST /api/auth/login`.
                2. Paste the access token into *Authentication*.
                3. Find someone at `GET /api/users?query=…` and start a conversation at `POST /api/conversations/direct`.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `not_found`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/realtime-chat/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
