// Load test against a running stack (nginx in front of two API containers):
//   dotnet run -c Release --project tests/RealtimeChat.LoadTest -- [users] [groupSize] [seconds] [messageEverySeconds]
// Registers the users, puts them in groups, opens one WebSocket per user, lets everyone send at a steady rate
// and measures how long each message takes to reach every other member of its group.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

var baseUrl = new Uri(Environment.GetEnvironmentVariable("BASE_URL") ?? "http://localhost:8080");
var users = Arg(0, 2000);
var groupSize = Arg(1, 10);
var seconds = Arg(2, 60);
var every = Arg(3, 5);
int Arg(int index, int fallback) => args.Length > index ? int.Parse(args[index], CultureInfo.InvariantCulture) : fallback;

using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(60) };
var run = Guid.NewGuid().ToString("N")[..6];

Console.WriteLine($"== {users} users in groups of {groupSize}, each sending every {every} s for {seconds} s, through {baseUrl}");

// 1. Accounts.
var clock = Stopwatch.StartNew();
var accounts = new Account[users];
await Parallel.ForEachAsync(Enumerable.Range(0, users), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (i, ct) =>
{
    var response = await http.PostAsJsonAsync("/api/auth/register",
        new { email = $"load.{run}.{i}@example.com", displayName = $"Load {i}", password = "load-test-password" }, ct);
    response.EnsureSuccessStatusCode();
    var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
    accounts[i] = new Account(token!.User.Id, token.AccessToken);
});
Console.WriteLine($"Registered {users} accounts in {clock.Elapsed.TotalSeconds:F1} s");

// 2. Groups.
var groups = new List<(Guid Id, Account[] Members)>();
foreach (var chunk in accounts.Chunk(groupSize))
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations/groups")
    {
        Content = JsonContent.Create(new { title = $"Load group {groups.Count}", memberIds = chunk.Skip(1).Select(a => a.Id) }),
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chunk[0].Token);
    var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    var conversation = await response.Content.ReadFromJsonAsync<ConversationResponse>();
    groups.Add((conversation!.Id, chunk));
}

// 3. Connections.
var latencies = new ConcurrentBag<double>();
long delivered = 0;
var connections = new ConcurrentDictionary<Guid, HubConnection>();
clock.Restart();
await Parallel.ForEachAsync(accounts, new ParallelOptions { MaxDegreeOfParallelism = 64 }, async (account, ct) =>
{
    var connection = new HubConnectionBuilder()
        .WithUrl(new Uri(baseUrl, "/hubs/chat"), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.AccessTokenProvider = () => Task.FromResult<string?>(account.Token);
        })
        .Build();
    connection.On<MessageResponse>("MessageReceived", message =>
    {
        if (message.SenderId == account.Id)
        {
            return;
        }

        var sentAt = long.Parse(message.Body.Split('|')[0], CultureInfo.InvariantCulture);
        latencies.Add(Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds);
        Interlocked.Increment(ref delivered);
    });
    await connection.StartAsync(ct);
    connections[account.Id] = connection;
});
Console.WriteLine($"Opened {connections.Count} WebSocket connections in {clock.Elapsed.TotalSeconds:F1} s");

// 4. Traffic: everyone sends every few seconds, spread evenly so the load is steady.
long sent = 0, failed = 0;
var sendTimes = new ConcurrentBag<double>();
clock.Restart();
var deadline = TimeSpan.FromSeconds(seconds);
await Parallel.ForEachAsync(groups.SelectMany(g => g.Members.Select(m => (g.Id, Member: m))), new ParallelOptions { MaxDegreeOfParallelism = users }, async (item, ct) =>
{
    var connection = connections[item.Member.Id];
    await Task.Delay(Random.Shared.Next(every * 1000), ct);
    while (clock.Elapsed < deadline)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await connection.InvokeAsync<MessageResponse>("Send", item.Id, Guid.NewGuid(), $"{started}|load test message", ct);
            sendTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Interlocked.Increment(ref sent);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref failed);
        }

        await Task.Delay(TimeSpan.FromSeconds(every), ct);
    }
});

// Let the last deliveries arrive.
var expected = sent * (groupSize - 1);
var wait = Stopwatch.StartNew();
while (Interlocked.Read(ref delivered) < expected && wait.Elapsed < TimeSpan.FromSeconds(30))
{
    await Task.Delay(200);
}

await Task.WhenAll(connections.Values.Select(c => c.DisposeAsync().AsTask()));

var sorted = latencies.Order().ToArray();
var sendSorted = sendTimes.Order().ToArray();
double P(double[] values, double p) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)Math.Ceiling(p * values.Length) - 1)];

Console.WriteLine();
Console.WriteLine("| Measure | Result |");
Console.WriteLine("|---|---|");
Console.WriteLine($"| Concurrent WebSocket connections | {connections.Count:N0} |");
Console.WriteLine($"| Messages sent | {sent:N0} ({sent / (double)seconds:F0}/s), {failed} failed |");
Console.WriteLine($"| Deliveries to other members | {Interlocked.Read(ref delivered):N0} of {expected:N0} expected ({Interlocked.Read(ref delivered) / (double)seconds:F0}/s) |");
Console.WriteLine($"| Send (stored and numbered) p50 / p95 / p99 | {P(sendSorted, 0.50):F0} / {P(sendSorted, 0.95):F0} / {P(sendSorted, 0.99):F0} ms |");
Console.WriteLine($"| Delivery, sender to every member, p50 / p95 / p99 | {P(sorted, 0.50):F0} / {P(sorted, 0.95):F0} / {P(sorted, 0.99):F0} ms |");

return Interlocked.Read(ref delivered) == expected && failed == 0 ? 0 : 1;

internal sealed record Account(Guid Id, string Token);

internal sealed record UserResponse(Guid Id, string DisplayName);

internal sealed record TokenResponse(string AccessToken, UserResponse User);

internal sealed record ConversationResponse(Guid Id);

internal sealed record MessageResponse(Guid Id, Guid ConversationId, long Seq, Guid SenderId, string Body);
