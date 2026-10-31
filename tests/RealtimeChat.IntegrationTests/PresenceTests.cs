using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RealtimeChat.Infrastructure.Presence;
using RealtimeChat.IntegrationTests.Support;
using StackExchange.Redis;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PresenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Contacts_see_someone_come_online_once_and_go_offline_with_the_last_tab()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var stranger = await factory.RegisterAsync("Can");
        await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var strangerConnection = await ChatConnection.OpenAsync(factory, stranger.Token);

        var laptop = await ChatConnection.OpenAsync(factory, mehmet.Token);
        var phone = await ChatConnection.OpenAsync(factory, mehmet.Token);
        await Waiting.UntilAsync(() => ayseConnection.Presence.Any(p => p.UserId == mehmet.Id && p.Online), "online");
        Assert.Contains(mehmet.Id, await ayse.Http.GetFromJsonAsync<List<Guid>>("/api/presence", TestApi.Json) ?? []);

        await laptop.DisposeAsync();
        await Waiting.QuietPeriodAsync();
        Assert.Single(ayseConnection.Presence, p => p.UserId == mehmet.Id);
        Assert.Contains(mehmet.Id, await ayse.Http.GetFromJsonAsync<List<Guid>>("/api/presence", TestApi.Json) ?? []);

        await phone.DisposeAsync();
        await Waiting.UntilAsync(() => ayseConnection.Presence.Any(p => p.UserId == mehmet.Id && !p.Online), "offline");
        Assert.DoesNotContain(mehmet.Id, await ayse.Http.GetFromJsonAsync<List<Guid>>("/api/presence", TestApi.Json) ?? []);
        Assert.DoesNotContain(strangerConnection.Presence, p => p.UserId == mehmet.Id);
    }

    [Fact]
    public async Task Presence_only_lists_people_you_share_a_conversation_with()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var stranger = await factory.RegisterAsync("Can");
        await using var strangerConnection = await ChatConnection.OpenAsync(factory, stranger.Token);

        Assert.Empty(await ayse.Http.GetFromJsonAsync<List<Guid>>("/api/presence", TestApi.Json) ?? []);
    }

    [Fact]
    public async Task A_heartbeat_keeps_the_connection_alive()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        await using var connection = await ChatConnection.OpenAsync(factory, ayse.Token);

        Assert.True(await connection.Hub.InvokeAsync<bool>("Heartbeat"));
    }

    [Fact]
    public async Task Connections_that_stop_sending_heartbeats_expire_and_are_swept()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = new PresenceTracker(
            factory.Services.GetRequiredService<IConnectionMultiplexer>(),
            Options.Create(new PresenceOptions { ConnectionTimeout = TimeSpan.FromSeconds(60) }),
            clock);
        var userId = Guid.NewGuid();

        Assert.True(await tracker.ConnectAsync(userId, "tab-1"));
        Assert.False(await tracker.ConnectAsync(userId, "tab-2"));
        clock.Advance(TimeSpan.FromSeconds(45));
        Assert.True(await tracker.HeartbeatAsync(userId, "tab-2"));
        clock.Advance(TimeSpan.FromSeconds(30));

        // tab-1 expired, tab-2 is alive thanks to its heartbeat.
        Assert.Contains(userId, await tracker.OnlineAsync([userId]));
        Assert.DoesNotContain(userId, await tracker.SweepAsync());

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Empty(await tracker.OnlineAsync([userId]));
        Assert.Contains(userId, await tracker.SweepAsync());
        Assert.False(await tracker.HeartbeatAsync(userId, "tab-2"));
    }

    [Fact]
    public async Task Typing_reaches_the_other_members_but_not_the_typist_and_is_throttled()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var zeynep = await factory.RegisterAsync("Zeynep");
        var group = await ayse.GroupAsync("Ekip", mehmet, zeynep);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        await using var zeynepConnection = await ChatConnection.OpenAsync(factory, zeynep.Token);

        for (var i = 0; i < 5; i++)
        {
            await ayseConnection.Hub.InvokeAsync("Typing", group.Id);
        }

        await Waiting.UntilAsync(() => mehmetConnection.Typing.Count > 0 && zeynepConnection.Typing.Count > 0, "typing");
        await Waiting.QuietPeriodAsync();
        Assert.Single(mehmetConnection.Typing);
        Assert.Equal(ayse.Id, mehmetConnection.Typing.Single().UserId);
        Assert.Empty(ayseConnection.Typing);
    }
}
