using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RealtimeChat.Api.Features.Demo;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(ApiFactory factory)
{
    [Fact]
    public async Task Outside_the_public_demo_there_are_no_demo_sign_ins()
    {
        using var anonymous = factory.CreateClient();

        var accounts = await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/demo");

        Assert.NotNull(accounts);
        Assert.Empty(accounts);
    }

    [Fact]
    public async Task The_demo_people_get_their_conversations_and_a_colleague_answers()
    {
        var demo = new DemoOptions { Enabled = true };
        var worker = new DemoWorker(Options.Create(demo), new TestClients(factory), factory.Services.GetRequiredService<IHostApplicationLifetime>(),
            TimeProvider.System, NullLogger<DemoWorker>.Instance);
        using var http = factory.CreateClient();

        await worker.SetUpAsync(http, CancellationToken.None);
        await worker.SetUpAsync(http, CancellationToken.None);

        using var ayse = await SignedInAsync(http, demo.FirstEmail, demo.Password);
        var conversations = await ayse.GetFromJsonAsync<List<JsonElement>>("/api/conversations");
        Assert.NotNull(conversations);
        Assert.Equal(3, conversations.Count);
        Assert.Single(conversations, c => c.GetProperty("kind").GetString() == "Group");

        // Nothing new: the colleagues stay quiet.
        await worker.TickAsync(http, CancellationToken.None);
        var withElif = conversations.Single(c => c.GetProperty("title").GetString() == "Elif Arslan").GetProperty("id").GetString();
        var before = await LastSeqAsync(ayse, withElif!);

        var sent = await ayse.PostAsJsonAsync($"/api/conversations/{withElif}/messages", new { clientMessageId = Guid.NewGuid(), body = "Are you coming?" });
        sent.EnsureSuccessStatusCode();
        await worker.TickAsync(http, CancellationToken.None);

        var page = await ayse.GetFromJsonAsync<JsonElement>($"/api/conversations/{withElif}/messages?after={before}");
        var messages = page.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.NotEqual(messages[0].GetProperty("senderId").GetString(), messages[1].GetProperty("senderId").GetString());
    }

    private static async Task<long> LastSeqAsync(HttpClient client, string conversation)
    {
        var info = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{conversation}");
        return info.GetProperty("lastSeq").GetInt64();
    }

    private async Task<HttpClient> SignedInAsync(HttpClient http, string email, string password)
    {
        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class TestClients(ApiFactory factory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => factory.CreateClient();
    }
}
