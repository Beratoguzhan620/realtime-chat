using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RealtimeChat.Infrastructure.Notifications;

/// <summary>
/// Sends through the notification-service: it renders the template, retries delivery and keeps a dead-letter
/// queue, so this service only decides who should hear about what.
/// </summary>
internal sealed class NotificationServiceClient(HttpClient http, IOptions<UnreadEmailOptions> options) : INotificationClient
{
    public async Task SendAsync(UnreadEmail email, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl!), "/api/notifications"))
        {
            Content = JsonContent.Create(new
            {
                recipient = new { id = email.UserId.ToString(), email = email.Email },
                channel = "Email",
                template = settings.Template,
                language = settings.Language,
                variables = new Dictionary<string, string>
                {
                    ["name"] = email.DisplayName,
                    ["conversation"] = email.ConversationTitle,
                    ["count"] = email.UnreadCount.ToString(CultureInfo.InvariantCulture),
                },
            }),
        };
        request.Headers.Add("X-Api-Key", settings.ApiKey);
        request.Headers.Add("Idempotency-Key", email.IdempotencyKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
