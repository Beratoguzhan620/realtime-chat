using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RealtimeChat.Api.Features.Demo;

/// <summary>
/// Runs the public demo through the API's own endpoints, so everything in it passes the same rules as what a visitor
/// does. On start it makes the people once: the two published sign-ins, two colleagues, a group with a little history
/// and a few direct conversations. Then, while the service is awake, the colleagues answer what is written to them, in
/// their direct conversations and taking turns in the group, so a visitor alone still sees messages arrive live.
/// </summary>
internal sealed partial class DemoWorker(
    IOptions<DemoOptions> options, IHttpClientFactory clients, IHostApplicationLifetime lifetime, TimeProvider clock, ILogger<DemoWorker> logger)
    : BackgroundService
{
    public const string HttpClientName = "RealtimeChat.Demo";

    public const string FirstName = "Ayşe Kaya";

    public const string SecondName = "Mehmet Demir";

    private const string GroupTitle = "Samsun .NET meetup";

    private static readonly (string Key, string Name)[] Colleagues = [("elif", "Elif Arslan"), ("can", "Can Yıldız")];

    private static readonly string[] Answers =
    [
        "Sounds good to me 👍",
        "Good point. Let me check and get back to you.",
        "Ha, same here.",
        "I can take that one.",
        "Thanks! Seen it.",
        "Let's talk about it on Thursday.",
        "Agreed. Shall I put it in the notes?",
        "On my way, five minutes.",
    ];

    private static readonly string[] QuestionAnswers =
    [
        "Good question. I think yes, but let's ask the group.",
        "Not sure yet, I'll look this evening.",
        "Yes! Thursday at 19:00 works for me.",
        "I'd say the second option.",
    ];

    private readonly Dictionary<string, Person> _colleagues = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastAnswer = [];
    private int _next;
    private DateTimeOffset _signedInAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
        {
            return;
        }

        using var http = clients.CreateClient(HttpClientName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_colleagues.Count == 0 || clock.GetUtcNow() - _signedInAt > TimeSpan.FromMinutes(30))
                {
                    await SetUpAsync(http, stoppingToken);
                }

                await TickAsync(http, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The demo keeps going; the next tick tries again.
                LogTickFailed(logger, ex);
            }

            await Task.Delay(options.Value.Tick, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
        return started.Task.IsCompleted && !ct.IsCancellationRequested;
    }

    /// <summary>Signs everyone in, making the people and their conversations the first time.</summary>
    public async Task SetUpAsync(HttpClient http, CancellationToken ct)
    {
        var demo = options.Value;
        var first = await SignInAsync(http, demo.FirstEmail, FirstName, ct);
        var second = await SignInAsync(http, demo.SecondEmail, SecondName, ct);
        foreach (var (key, name) in Colleagues)
        {
            _colleagues[key] = await SignInAsync(http, $"{key}@chat.demo", name, ct);
        }

        var conversations = await SendAsync<List<Conversation>>(http, HttpMethod.Get, "/api/conversations", first.Token, null, ct);
        if (!conversations.Any(c => c.Title == GroupTitle))
        {
            await CreateConversationsAsync(http, first, second, ct);
        }

        _signedInAt = clock.GetUtcNow();
        LogReady(logger);
    }

    private async Task CreateConversationsAsync(HttpClient http, Person first, Person second, CancellationToken ct)
    {
        var elif = _colleagues["elif"];
        var can = _colleagues["can"];
        var group = await SendAsync<Conversation>(http, HttpMethod.Post, "/api/conversations/groups", first.Token,
            new { title = GroupTitle, memberIds = new[] { second.Id, elif.Id, can.Id } }, ct);
        await SayAsync(http, first, group.Id, "Hi all! Next meetup is Thursday at 19:00, same café as last time.", ct);
        await SayAsync(http, elif, group.Id, "Great, I'll bring the slides about SignalR.", ct);
        await SayAsync(http, can, group.Id, "Can someone book a bigger table? We were twelve last time.", ct);
        await SayAsync(http, second, group.Id, "I'll call them tomorrow morning.", ct);
        await SayAsync(http, elif, group.Id, "Write here and one of us answers, even if the others are offline 🙂", ct);

        var withElif = await SendAsync<Conversation>(http, HttpMethod.Post, "/api/conversations/direct", first.Token, new { userId = elif.Id }, ct);
        await SayAsync(http, elif, withElif.Id, "Did you see the new message ordering fix? Reconnects catch up without gaps now.", ct);
        await SayAsync(http, first, withElif.Id, "Yes, nice! Sequence numbers per conversation made it easy.", ct);

        var withCan = await SendAsync<Conversation>(http, HttpMethod.Post, "/api/conversations/direct", second.Token, new { userId = can.Id }, ct);
        await SayAsync(http, can, withCan.Id, "Are you coming on Thursday?", ct);

        var together = await SendAsync<Conversation>(http, HttpMethod.Post, "/api/conversations/direct", first.Token, new { userId = second.Id }, ct);
        await SayAsync(http, first, together.Id, "Open this chat as me in one window and as Mehmet in another: you'll see typing and read receipts live.", ct);

        // The colleagues have seen what was there before any visitor came.
        foreach (var colleague in _colleagues.Values)
        {
            foreach (var conversation in await SendAsync<List<Conversation>>(http, HttpMethod.Get, "/api/conversations", colleague.Token, null, ct))
            {
                await MarkReadAsync(http, colleague, conversation, ct);
            }
        }
    }

    /// <summary>Each colleague answers the conversations with something new from someone else in them.</summary>
    public async Task TickAsync(HttpClient http, CancellationToken ct)
    {
        var colleagueIds = _colleagues.Values.Select(c => c.Id).ToHashSet();
        foreach (var colleague in _colleagues.Values)
        {
            var conversations = await SendAsync<List<Conversation>>(http, HttpMethod.Get, "/api/conversations", colleague.Token, null, ct);
            foreach (var conversation in conversations.Where(c => c.UnreadCount > 0 && c.LastMessage is not null))
            {
                var last = conversation.LastMessage!;
                if (colleagueIds.Contains(last.SenderId) || last.DeletedAt is not null)
                {
                    await MarkReadAsync(http, colleague, conversation, ct);
                    continue;
                }

                // In the group they take turns; a short pause keeps them from talking over a visitor.
                var answerer = conversation.Kind == "Group" ? _colleagues.Values.ElementAt((int)(last.Seq % _colleagues.Count)) : colleague;
                if (answerer.Id != colleague.Id)
                {
                    await MarkReadAsync(http, colleague, conversation, ct);
                    continue;
                }

                if (_lastAnswer.TryGetValue(conversation.Id, out var at) && clock.GetUtcNow() - at < TimeSpan.FromSeconds(10))
                {
                    continue;
                }

                await MarkReadAsync(http, colleague, conversation, ct);
                await SayAsync(http, colleague, conversation.Id, Answer(last.Body), ct);
                _lastAnswer[conversation.Id] = clock.GetUtcNow();
            }
        }
    }

    private string Answer(string message)
    {
        var pool = message.TrimEnd().EndsWith('?') ? QuestionAnswers : Answers;
        return pool[_next++ % pool.Length];
    }

    private static Task MarkReadAsync(HttpClient http, Person person, Conversation conversation, CancellationToken ct) =>
        SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/conversations/{conversation.Id}/read", person.Token, new { seq = conversation.LastSeq }, ct);

    private static Task SayAsync(HttpClient http, Person person, Guid conversation, string body, CancellationToken ct) =>
        SendAsync<JsonElement?>(http, HttpMethod.Post, $"/api/conversations/{conversation}/messages", person.Token,
            new { clientMessageId = Guid.NewGuid(), body }, ct);

    private async Task<Person> SignInAsync(HttpClient http, string email, string name, CancellationToken ct)
    {
        var password = options.Value.Password;
        using var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
        using var register = login.IsSuccessStatusCode
            ? null
            : await http.PostAsJsonAsync("/api/auth/register", new { email, displayName = name, password }, ct);
        var response = register ?? login;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The demo account {email} could not sign in: {(int)response.StatusCode}.");
        }

        var session = (await response.Content.ReadFromJsonAsync<Session>(ct))!;
        return new Person(session.User.Id, session.AccessToken);
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }

        return response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
            ? default!
            : (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private sealed record Person(Guid Id, string Token);

    private sealed record Session(string AccessToken, SessionUser User);

    private sealed record SessionUser(Guid Id);

    private sealed record Conversation(Guid Id, string Kind, string Title, long LastSeq, long UnreadCount, LastMessageInfo? LastMessage);

    private sealed record LastMessageInfo(long Seq, Guid SenderId, string Body, DateTimeOffset? DeletedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "The demo people are signed in")]
    private static partial void LogReady(ILogger log);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A demo tick failed")]
    private static partial void LogTickFailed(ILogger log, Exception exception);
}
