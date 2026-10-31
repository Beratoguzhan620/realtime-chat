using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RealtimeChat.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string DisplayName);

public sealed record TokenDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserDto User);

public sealed record MemberDto(Guid UserId, string DisplayName, string Role, long LastReadSeq);

public sealed record ConversationDto(Guid Id, string Kind, string Title, List<MemberDto> Members, long LastSeq, long UnreadCount, DateTimeOffset LastActivityAt);

/// <summary>A registered person with an HTTP client that carries their token.</summary>
public sealed class Person(UserDto user, string token, HttpClient http) : IDisposable
{
    public UserDto User { get; } = user;

    public Guid Id => User.Id;

    public string Token { get; } = token;

    public HttpClient Http { get; } = http;

    public void Dispose() => Http.Dispose();
}

public static class TestApi
{
    public const string Password = "correct-horse-battery";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string UniqueEmail(string name) => $"{name.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com";

    public static async Task<Person> RegisterAsync<T>(this WebApplicationFactory<T> factory, string name = "Ayşe")
        where T : class
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { email = UniqueEmail(name.Replace('ş', 's').Replace('ı', 'i')), displayName = $"{name} {Guid.NewGuid().ToString("N")[..6]}", password = Password });
        response.EnsureSuccessStatusCode();
        var token = await response.ReadAsync<TokenDto>();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return new Person(token.User, token.AccessToken, http);
    }

    public static async Task<ConversationDto> DirectAsync(this Person person, Person other)
    {
        var response = await person.Http.PostAsJsonAsync("/api/conversations/direct", new { userId = other.Id });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ConversationDto>();
    }

    public static async Task<ConversationDto> GroupAsync(this Person owner, string title, params Person[] members)
    {
        var response = await owner.Http.PostAsJsonAsync("/api/conversations/groups", new { title, memberIds = members.Select(m => m.Id) });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ConversationDto>();
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
