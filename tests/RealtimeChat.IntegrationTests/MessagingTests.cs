using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using RealtimeChat.IntegrationTests.Support;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MessagingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_message_reaches_every_connection_of_every_member_and_nobody_else()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var outsider = await factory.RegisterAsync("Can");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseLaptop = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var aysePhone = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        await using var outsiderConnection = await ChatConnection.OpenAsync(factory, outsider.Token);

        var sent = await ayseLaptop.SendAsync(conversation.Id, "  Merhaba, kargo yola çıktı mı?  ");

        Assert.Equal(1, sent.Seq);
        Assert.Equal("Merhaba, kargo yola çıktı mı?", sent.Body);
        await Waiting.UntilAsync(() => mehmetConnection.Received.Count == 1 && aysePhone.Received.Count == 1, "delivery");
        Assert.Equal(sent.Id, mehmetConnection.Received.Single().Id);
        await Waiting.QuietPeriodAsync();
        Assert.Empty(outsiderConnection.Received);
    }

    [Fact]
    public async Task Resending_with_the_same_client_id_stores_one_message()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var connection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        var clientId = Guid.NewGuid();

        var first = await connection.SendAsync(conversation.Id, "Tek sefer", clientId);
        var again = await connection.SendAsync(conversation.Id, "Tek sefer", clientId);
        var overRest = await ayse.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = clientId, body = "Tek sefer" });
        var racing = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => connection.SendAsync(conversation.Id, "Yarış", Guid.Parse("00000000-0000-0000-0000-00000000000a"))));

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(HttpStatusCode.OK, overRest.StatusCode);
        Assert.Equal(first.Id, (await overRest.ReadAsync<MessageDto>()).Id);
        Assert.Single(racing.Select(m => m.Id).Distinct());
        var history = await ayse.Http.GetFromJsonAsync<MessagePageDto>($"/api/conversations/{conversation.Id}/messages", TestApi.Json);
        Assert.Equal([1L, 2L], history!.Messages.Select(m => m.Seq));
        await Waiting.QuietPeriodAsync();
        Assert.Equal(2, mehmetConnection.Received.Count);
    }

    [Fact]
    public async Task Concurrent_senders_get_one_number_each_without_gaps()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var zeynep = await factory.RegisterAsync("Zeynep");
        var group = await ayse.GroupAsync("Yoğun grup", mehmet, zeynep);
        var connections = await Task.WhenAll(new[] { ayse, mehmet, zeynep }.Select(p => ChatConnection.OpenAsync(factory, p.Token)));

        var sent = await Task.WhenAll(connections.SelectMany(c => Enumerable.Range(0, 15).Select(i => c.SendAsync(group.Id, $"Mesaj {i}"))));

        Assert.Equal(Enumerable.Range(1, 45).Select(i => (long)i), sent.Select(m => m.Seq).Order());
        await Waiting.UntilAsync(() => connections.All(c => c.Received.Count == 45), "every message on every connection");
        foreach (var connection in connections)
        {
            Assert.Equal(45, connection.Received.Select(m => m.Seq).Distinct().Count());
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task After_a_reconnect_the_missed_messages_come_back_in_order()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        await ayseConnection.SendAsync(conversation.Id, "Bir");
        await Waiting.UntilAsync(() => mehmetConnection.Received.Count == 1, "the first message");
        var lastSeen = mehmetConnection.Received.Last().Seq;
        await mehmetConnection.DisposeAsync();

        foreach (var text in new[] { "İki", "Üç", "Dört" })
        {
            await ayseConnection.SendAsync(conversation.Id, text);
        }

        var missed = await mehmet.Http.GetFromJsonAsync<MessagePageDto>($"/api/conversations/{conversation.Id}/messages?after={lastSeen}", TestApi.Json);

        Assert.Equal(["İki", "Üç", "Dört"], missed!.Messages.Select(m => m.Body));
        Assert.Equal([2L, 3L, 4L], missed.Messages.Select(m => m.Seq));
        Assert.False(missed.HasMore);
    }

    [Fact]
    public async Task History_pages_back_without_gaps_or_repeats()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        for (var i = 1; i <= 25; i++)
        {
            (await ayse.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), body = $"#{i}" })).EnsureSuccessStatusCode();
        }

        var seen = new List<long>();
        var page = await mehmet.Http.GetFromJsonAsync<MessagePageDto>($"/api/conversations/{conversation.Id}/messages?limit=10", TestApi.Json);
        seen.InsertRange(0, page!.Messages.Select(m => m.Seq));
        while (page!.HasMore)
        {
            page = await mehmet.Http.GetFromJsonAsync<MessagePageDto>($"/api/conversations/{conversation.Id}/messages?limit=10&before={seen[0]}", TestApi.Json);
            seen.InsertRange(0, page!.Messages.Select(m => m.Seq));
        }

        Assert.Equal(Enumerable.Range(1, 25).Select(i => (long)i), seen);
    }

    [Fact]
    public async Task Only_the_sender_edits_or_deletes_and_everyone_hears_about_it()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        var sent = await ayseConnection.SendAsync(conversation.Id, "Yarın 10'da");

        var byOther = await Assert.ThrowsAsync<HubException>(() => mehmetConnection.Hub.InvokeAsync<MessageDto>("Edit", sent.Id, "Değiştirdim"));
        var edited = await ayseConnection.Hub.InvokeAsync<MessageDto>("Edit", sent.Id, "Yarın 11'de");
        var deleted = await ayseConnection.Hub.InvokeAsync<MessageDto>("Delete", sent.Id);

        Assert.StartsWith("forbidden:", byOther.Message, StringComparison.Ordinal);
        Assert.Equal("Yarın 11'de", edited.Body);
        Assert.NotNull(edited.EditedAt);
        Assert.Equal(string.Empty, deleted.Body);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(sent.Seq, deleted.Seq);
        await Waiting.UntilAsync(() => mehmetConnection.Updated.Count == 2, "the edit and the delete");
        var editAfterDelete = await Assert.ThrowsAsync<HubException>(() => ayseConnection.Hub.InvokeAsync<MessageDto>("Edit", sent.Id, "Geri"));
        Assert.StartsWith("invalid_state:", editAfterDelete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outsiders_cannot_send_read_or_mark_anything()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var outsider = await factory.RegisterAsync("Can");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var outsiderConnection = await ChatConnection.OpenAsync(factory, outsider.Token);

        var send = await Assert.ThrowsAsync<HubException>(() => outsiderConnection.SendAsync(conversation.Id, "Merhaba"));
        var read = await outsider.Http.GetAsync($"/api/conversations/{conversation.Id}/messages");
        var mark = await outsider.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/read", new { seq = 1 });

        Assert.StartsWith("not_found:", send.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mark.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Empty_messages_are_refused(string body)
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var connection = await ChatConnection.OpenAsync(factory, ayse.Token);

        var error = await Assert.ThrowsAsync<HubException>(() => connection.SendAsync(conversation.Id, body));

        Assert.StartsWith("validation_failed:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_messages_are_refused_and_nothing_is_numbered()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);

        var response = await ayse.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), body = new string('a', 4001) });
        var after = await ayse.Http.GetFromJsonAsync<ConversationDto>($"/api/conversations/{conversation.Id}", TestApi.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, after!.LastSeq);
    }

    [Fact]
    public async Task Sending_too_fast_is_slowed_down()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);

        var responses = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            ayse.Http.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { clientMessageId = Guid.NewGuid(), body = $"spam {i}" })));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.InRange(responses.Count(r => r.StatusCode == HttpStatusCode.Created), 20, 30);
    }

    [Fact]
    public async Task Reading_moves_forward_tells_the_others_and_clears_the_unread_count()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var conversation = await ayse.DirectAsync(mehmet);
        await using var ayseConnection = await ChatConnection.OpenAsync(factory, ayse.Token);
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);
        for (var i = 0; i < 3; i++)
        {
            await ayseConnection.SendAsync(conversation.Id, $"Mesaj {i}");
        }

        var unread = (await mehmet.Http.GetFromJsonAsync<List<ConversationDto>>("/api/conversations", TestApi.Json))!.Single(c => c.Id == conversation.Id);
        var marker = await mehmetConnection.Hub.InvokeAsync<long?>("MarkRead", conversation.Id, 3L);
        var backwards = await mehmetConnection.Hub.InvokeAsync<long?>("MarkRead", conversation.Id, 1L);
        var read = (await mehmet.Http.GetFromJsonAsync<List<ConversationDto>>("/api/conversations", TestApi.Json))!.Single(c => c.Id == conversation.Id);

        Assert.Equal(3, unread.UnreadCount);
        Assert.Equal("Mesaj 2", unread.LastMessage!.Body);
        Assert.Equal(3, marker);
        Assert.Null(backwards);
        Assert.Equal(0, read.UnreadCount);
        await Waiting.UntilAsync(() => ayseConnection.Reads.Any(r => r.UserId == mehmet.Id && r.Seq == 3), "the read receipt");
    }

    [Fact]
    public async Task New_conversations_are_announced_to_the_other_people()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        await using var mehmetConnection = await ChatConnection.OpenAsync(factory, mehmet.Token);

        var conversation = await ayse.DirectAsync(mehmet);

        await Waiting.UntilAsync(() => mehmetConnection.ConversationChanges.Contains(conversation.Id), "the announcement");
    }

    [Fact]
    public async Task The_hub_refuses_connections_without_a_valid_token()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ChatConnection.OpenAsync(factory, "not-a-token"));
    }
}
