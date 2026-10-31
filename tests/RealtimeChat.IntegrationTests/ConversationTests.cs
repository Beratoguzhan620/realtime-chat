using System.Net;
using System.Net.Http.Json;
using RealtimeChat.IntegrationTests.Support;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConversationTests(ApiFactory factory)
{
    [Fact]
    public async Task A_pair_has_one_direct_conversation_named_after_the_other_person()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");

        var started = await ayse.Http.PostAsJsonAsync("/api/conversations/direct", new { userId = mehmet.Id });
        var reopened = await mehmet.Http.PostAsJsonAsync("/api/conversations/direct", new { userId = ayse.Id });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var fromAyse = await started.ReadAsync<ConversationDto>();
        var fromMehmet = await reopened.ReadAsync<ConversationDto>();
        Assert.Equal(fromAyse.Id, fromMehmet.Id);
        Assert.Equal(mehmet.User.DisplayName, fromAyse.Title);
        Assert.Equal(ayse.User.DisplayName, fromMehmet.Title);
    }

    [Fact]
    public async Task Both_people_starting_at_once_still_get_one_conversation()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? ayse.DirectAsync(mehmet) : mehmet.DirectAsync(ayse))));

        Assert.Single(results.Select(r => r.Id).Distinct());
    }

    [Fact]
    public async Task Nobody_starts_a_conversation_with_themselves_or_a_stranger_id()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");

        var self = await ayse.Http.PostAsJsonAsync("/api/conversations/direct", new { userId = ayse.Id });
        var unknown = await ayse.Http.PostAsJsonAsync("/api/conversations/direct", new { userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_group_is_listed_for_every_member_and_hidden_from_everyone_else()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var zeynep = await factory.RegisterAsync("Zeynep");
        using var outsider = await factory.RegisterAsync("Can");

        var group = await ayse.GroupAsync("Kargo ekibi", mehmet, zeynep);

        Assert.Equal("Group", group.Kind);
        Assert.Equal(3, group.Members.Count);
        Assert.Equal("Owner", group.Members[0].Role);
        foreach (var member in new[] { ayse, mehmet, zeynep })
        {
            var list = await member.Http.GetFromJsonAsync<List<ConversationDto>>("/api/conversations", Api.Json);
            Assert.Contains(list!, c => c.Id == group.Id);
        }

        Assert.DoesNotContain(await outsider.Http.GetFromJsonAsync<List<ConversationDto>>("/api/conversations", Api.Json) ?? [], c => c.Id == group.Id);
        var peek = await outsider.Http.GetAsync($"/api/conversations/{group.Id}");
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Equal("not_found", await peek.ProblemCodeAsync());
    }

    [Fact]
    public async Task Only_the_owner_adds_people_and_renames()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        using var zeynep = await factory.RegisterAsync("Zeynep");
        var group = await ayse.GroupAsync("Proje", mehmet);

        var byMember = await mehmet.Http.PostAsJsonAsync($"/api/conversations/{group.Id}/members", new { userIds = new[] { zeynep.Id } });
        var renameByMember = await mehmet.Http.PatchAsJsonAsync($"/api/conversations/{group.Id}", new { title = "Benim" });
        var byOwner = await ayse.Http.PostAsJsonAsync($"/api/conversations/{group.Id}/members", new { userIds = new[] { zeynep.Id } });
        var rename = await ayse.Http.PatchAsJsonAsync($"/api/conversations/{group.Id}", new { title = "Proje 2" });

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, renameByMember.StatusCode);
        Assert.Equal(3, (await byOwner.ReadAsync<ConversationDto>()).Members.Count);
        Assert.Equal("Proje 2", (await rename.ReadAsync<ConversationDto>()).Title);
    }

    [Fact]
    public async Task Leaving_hands_the_group_to_the_next_member()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var mehmet = await factory.RegisterAsync("Mehmet");
        var group = await ayse.GroupAsync("Proje", mehmet);

        Assert.Equal(HttpStatusCode.NoContent, (await ayse.Http.PostAsync($"/api/conversations/{group.Id}/leave", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await ayse.Http.GetAsync($"/api/conversations/{group.Id}")).StatusCode);
        var after = await mehmet.Http.GetFromJsonAsync<ConversationDto>($"/api/conversations/{group.Id}", Api.Json);
        Assert.Equal("Owner", Assert.Single(after!.Members).Role);
    }

    [Fact]
    public async Task People_are_found_by_name_or_whole_email_but_emails_are_never_shown()
    {
        using var ayse = await factory.RegisterAsync("Ayşe");
        using var client = factory.CreateClient();
        var email = Api.UniqueEmail("selin");
        (await client.PostAsJsonAsync("/api/auth/register", new { email, displayName = "Selin Kaya", password = Api.Password })).EnsureSuccessStatusCode();

        var byName = await ayse.Http.GetStringAsync("/api/users?query=selin%20ka");
        var byEmail = await ayse.Http.GetFromJsonAsync<List<UserDto>>($"/api/users?query={Uri.EscapeDataString(email)}", Api.Json);
        var byPartOfEmail = await ayse.Http.GetFromJsonAsync<List<UserDto>>($"/api/users?query={Uri.EscapeDataString(email[..10])}", Api.Json);

        Assert.Contains("Selin Kaya", byName, StringComparison.Ordinal);
        Assert.DoesNotContain("@", byName, StringComparison.Ordinal);
        Assert.Single(byEmail!);
        Assert.Empty(byPartOfEmail!);
    }
}
