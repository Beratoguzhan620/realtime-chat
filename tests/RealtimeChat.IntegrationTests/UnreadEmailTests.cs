using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RealtimeChat.Infrastructure.Notifications;
using RealtimeChat.IntegrationTests.Support;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class UnreadEmailTests(ApiFactory factory)
{
    private UnreadEmailSender Sender => factory.Services.GetRequiredService<UnreadEmailSender>();

    [Fact]
    public async Task An_offline_reader_gets_one_email_once_the_conversation_goes_quiet()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await ayseConnection.SendAsync(conversation.Id, "Kargon yolda");
        var last = await ayseConnection.SendAsync(conversation.Id, "Yarın teslim");

        await Sender.RunOnceAsync(CancellationToken.None);
        Assert.Empty(factory.Emails.For(mehmet.Id));

        await Task.Delay(1200);
        await Sender.RunOnceAsync(CancellationToken.None);
        await Sender.RunOnceAsync(CancellationToken.None);

        var email = Assert.Single(factory.Emails.For(mehmet.Id));
        Assert.Equal(2, email.UnreadCount);
        Assert.Equal(ayse.User.DisplayName, email.ConversationTitle);
        Assert.Equal($"chat-unread-{conversation.Id:N}-{mehmet.Id:N}-{last.Seq}", email.IdempotencyKey);
        Assert.Empty(factory.Emails.For(ayse.Id));
    }

    [Fact]
    public async Task Online_and_caught_up_readers_get_no_email()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var zeynep = await factory.RegisterAsync("Zeynep");
        var group = await ayse.GroupAsync("Ekip", mehmet, zeynep);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        await using var zeynepConnection = await ChatConnection.OpenAsync(factory, zeynep.Token);
        var message = await ayseConnection.SendAsync(group.Id, "Toplantı 15:00");
        await zeynepConnection.Hub.InvokeAsync<long?>("MarkRead", group.Id, message.Seq);
        await zeynepConnection.DisposeAsync();

        await Task.Delay(1200);
        await Sender.RunOnceAsync(CancellationToken.None);

        Assert.Empty(factory.Emails.For(mehmet.Id));
        Assert.Empty(factory.Emails.For(zeynep.Id));
    }

    [Fact]
    public async Task New_messages_after_an_email_lead_to_one_more_email()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);

        await ayseConnection.SendAsync(conversation.Id, "Bir");
        await Task.Delay(1200);
        await Sender.RunOnceAsync(CancellationToken.None);
        await ayseConnection.SendAsync(conversation.Id, "İki");
        await Task.Delay(1200);
        await Sender.RunOnceAsync(CancellationToken.None);

        var emails = factory.Emails.For(mehmet.Id);
        Assert.Equal(2, emails.Count);
        Assert.Equal([1L, 2L], emails.Select(e => e.LastSeq));
    }
}
