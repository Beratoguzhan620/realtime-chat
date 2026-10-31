using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RealtimeChat.Api.Common;
using RealtimeChat.Api.Features.Auth;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Api.Features.Users;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users", Search)
            .WithTags("Users")
            .RequireAuthorization()
            .WithSummary("Find people to talk to")
            .WithDescription("Matches part of a display name, or a whole email address. Emails are never returned.");
        return app;
    }

    private static async Task<Results<Ok<List<UserResponse>>, ValidationProblem>> Search(
        ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct, string? query = null, int limit = 20)
    {
        var text = query?.Trim() ?? string.Empty;
        var validation = new Validation()
            .Require(text.Length is >= 2 and <= 100, "query", "Search with 2 to 100 characters.")
            .Require(limit is >= 1 and <= 50, "limit", "The limit is 1 to 50.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var me = principal.UserId();
        var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var email = text.ToLowerInvariant();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Id != me && (EF.Functions.ILike(u.DisplayName, pattern) || u.Email == email))
            .OrderBy(u => u.DisplayName)
            .Take(limit)
            .Select(u => new UserResponse(u.Id, u.DisplayName))
            .ToListAsync(ct);
        return TypedResults.Ok(users);
    }
}
