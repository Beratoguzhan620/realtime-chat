using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RealtimeChat.IntegrationTests.Support;

namespace RealtimeChat.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Registering_signs_in_and_me_shows_the_account()
    {
        using var person = await factory.RegisterAsync("Ayşe");

        var me = await person.Http.GetFromJsonAsync<Dictionary<string, object>>("/api/me");

        Assert.Equal(person.Id.ToString(), me!["id"].ToString());
    }

    [Fact]
    public async Task An_email_registers_once_whatever_its_case()
    {
        using var client = factory.CreateClient();
        var email = Api.UniqueEmail("mehmet");
        (await client.PostAsJsonAsync("/api/auth/register", new { email, displayName = "Mehmet", password = Api.Password })).EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), displayName = "Mehmet 2", password = Api.Password });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Bad_registrations_name_every_problem()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", displayName = "A", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("email", body, StringComparison.Ordinal);
        Assert.Contains("displayName", body, StringComparison.Ordinal);
        Assert.Contains("password", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_returns_a_working_token_and_wrong_details_get_one_answer()
    {
        using var client = factory.CreateClient();
        var email = Api.UniqueEmail("zeynep");
        (await client.PostAsJsonAsync("/api/auth/register", new { email, displayName = "Zeynep", password = Api.Password })).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Api.Password });
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password-123" });
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new { email = Api.UniqueEmail("nobody"), password = Api.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("invalid_credentials", await wrongPassword.ProblemCodeAsync());
        Assert.Equal("invalid_credentials", await unknownEmail.ProblemCodeAsync());

        var token = await login.ReadAsync<TokenDto>();
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not.a.jwt")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl")]
    public async Task Requests_without_a_valid_token_are_unauthorized(string? token)
    {
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/conversations")).StatusCode);
    }
}
