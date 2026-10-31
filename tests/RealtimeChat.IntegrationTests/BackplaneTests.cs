using Microsoft.AspNetCore.SignalR.Client;
using RealtimeChat.IntegrationTests.Support;

namespace RealtimeChat.IntegrationTests;

/// <summary>Two API instances share PostgreSQL and Redis, as the two containers behind nginx do.</summary>
[Collection(ApiCollection.Name)]
public sealed class BackplaneTests(ApiFactory factory)
{
    [Fact]
    public async Task People_on_different_instances_talk_to_each_other()
    {
        await using var second = new SecondInstance(factory);
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await second.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var onFirst = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var onSecond = await ChatConnection.OpenAsync(second, mehmet.Token);

        var fromFirst = await onFirst.SendAsync(conversation.Id, "Birinci sunucudan merhaba");
        await Waiting.UntilAsync(() => onSecond.Received.Any(m => m.Id == fromFirst.Id), "delivery to the second instance");

        var fromSecond = await onSecond.SendAsync(conversation.Id, "İkinciden cevap");
        await Waiting.UntilAsync(() => onFirst.Received.Any(m => m.Id == fromSecond.Id), "delivery to the first instance");
        Assert.Equal(fromFirst.Seq + 1, fromSecond.Seq);

        await onSecond.Hub.InvokeAsync("Typing", conversation.Id);
        await Waiting.UntilAsync(() => onFirst.Typing.Any(t => t.UserId == mehmet.Id), "typing across instances");

        // Mehmet's own message already marked everything before it as read for him; Ayşe has not read his yet.
        await onFirst.Hub.InvokeAsync<long?>("MarkRead", conversation.Id, fromSecond.Seq);
        await Waiting.UntilAsync(() => onSecond.Reads.Any(r => r.UserId == ayse.Id && r.Seq == fromSecond.Seq), "the read receipt across instances");
    }

    [Fact]
    public async Task Presence_is_shared_between_instances()
    {
        await using var second = new SecondInstance(factory);
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        await ayse.DirectAsync(mehmet);
        await using var ayseOnFirst = await ChatConnection.OpenAsync(factory, ayse.Token);

        var mehmetOnSecond = await ChatConnection.OpenAsync(second, mehmet.Token);
        await Waiting.UntilAsync(() => ayseOnFirst.Presence.Any(p => p.UserId == mehmet.Id && p.Online), "online seen from the other instance");

        await mehmetOnSecond.DisposeAsync();
        await Waiting.UntilAsync(() => ayseOnFirst.Presence.Any(p => p.UserId == mehmet.Id && !p.Online), "offline seen from the other instance");
    }
}
