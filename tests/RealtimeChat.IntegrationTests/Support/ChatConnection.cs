using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace RealtimeChat.IntegrationTests.Support;

public sealed record MessageDto(
    Guid Id, Guid ConversationId, long Seq, Guid SenderId, Guid ClientMessageId, string Body,
    DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, DateTimeOffset? DeletedAt);

public sealed record MessagePageDto(List<MessageDto> Messages, bool HasMore);

public sealed record ReadReceiptDto(Guid ConversationId, Guid UserId, long Seq);

public sealed record TypingDto(Guid ConversationId, Guid UserId);

public sealed record PresenceDto(Guid UserId, bool Online);

/// <summary>
/// A real SignalR client over WebSockets against the in-memory test server, the way the web client connects:
/// no negotiate request, the token on the connection. Every event it receives is kept for assertions.
/// </summary>
public sealed class ChatConnection : IAsyncDisposable
{
    private ChatConnection(HubConnection connection) => Hub = connection;

    public HubConnection Hub { get; }

    public ConcurrentQueue<MessageDto> Received { get; } = new();

    public ConcurrentQueue<MessageDto> Updated { get; } = new();

    public ConcurrentQueue<ReadReceiptDto> Reads { get; } = new();

    public ConcurrentQueue<Guid> ConversationChanges { get; } = new();

    public ConcurrentQueue<TypingDto> Typing { get; } = new();

    public ConcurrentQueue<PresenceDto> Presence { get; } = new();

    public static async Task<ChatConnection> OpenAsync<T>(WebApplicationFactory<T> factory, string token)
        where T : class
    {
        var server = factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                {
                    var client = server.CreateWebSocketClient();
                    client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
                    return await client.ConnectAsync(context.Uri, ct);
                };
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var connection = new ChatConnection(hub);
        hub.On<MessageDto>("MessageReceived", connection.Received.Enqueue);
        hub.On<MessageDto>("MessageUpdated", connection.Updated.Enqueue);
        hub.On<ReadReceiptDto>("ReadUpdated", connection.Reads.Enqueue);
        hub.On<Guid>("ConversationChanged", connection.ConversationChanges.Enqueue);
        hub.On<TypingDto>("Typing", connection.Typing.Enqueue);
        hub.On<PresenceDto>("PresenceChanged", connection.Presence.Enqueue);
        await hub.StartAsync();
        return connection;
    }

    public Task<MessageDto> SendAsync(Guid conversationId, string body, Guid? clientMessageId = null) =>
        Hub.InvokeAsync<MessageDto>("Send", conversationId, clientMessageId ?? Guid.NewGuid(), body);

    public async ValueTask DisposeAsync() => await Hub.DisposeAsync();
}

public static class Waiting
{
    /// <summary>Polls until the condition holds; events arrive asynchronously.</summary>
    public static async Task UntilAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(25);
        }
    }

    /// <summary>Gives events that should not arrive a chance to arrive, then lets the caller check they did not.</summary>
    public static Task QuietPeriodAsync() => Task.Delay(500);
}
