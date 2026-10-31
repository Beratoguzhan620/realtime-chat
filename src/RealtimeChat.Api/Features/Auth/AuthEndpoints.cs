using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RealtimeChat.Api.Common;
using RealtimeChat.Api.Logging;
using RealtimeChat.Core;
using RealtimeChat.Core.Users;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string DisplayName)
{
    public static UserResponse From(User user) => new(user.Id, user.DisplayName);
}

public sealed record MeResponse(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

internal static partial class AuthEndpoints
{
    public const string LoginRateLimit = "login";
    private const int PasswordMinLength = 10;
    private const int PasswordMaxLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/register", Register).RequireRateLimiting(LoginRateLimit).WithSummary("Create an account");
        group.MapPost("/login", Login).RequireRateLimiting(LoginRateLimit).WithSummary("Exchange email and password for an access token");

        app.MapGet("/api/me", Me).WithTags("Auth").WithSummary("The signed-in user").RequireAuthorization();
        return app;
    }

    private static async Task<Results<Created<AccessTokenResponse>, ValidationProblem>> Register(
        RegisterRequest request, ChatDbContext db, PasswordVerifier passwords, TokenService tokens, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(request.DisplayName?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength,
                "displayName", $"A display name has {User.DisplayNameMinLength} to {User.DisplayNameMaxLength} characters.")
            .Require(request.Password is { Length: >= PasswordMinLength and <= PasswordMaxLength },
                "password", $"A password has {PasswordMinLength} to {PasswordMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw EmailTaken();
        }

        var user = User.Register(email, request.DisplayName!, clock.GetUtcNow());
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Two registrations for the same email raced past the check above.
            throw EmailTaken();
        }

        LogRegistered(loggers.CreateLogger(typeof(AuthEndpoints)), user.Id);
        return TypedResults.Created("/api/me", tokens.Issue(user));
    }

    private static async Task<Results<Ok<AccessTokenResponse>, ValidationProblem>> Login(
        LoginRequest request, ChatDbContext db, PasswordVerifier passwords, TokenService tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (!passwords.Verify(user, request.Password, out var rehash) || user is null)
        {
            LogFailed(loggers.CreateLogger(typeof(AuthEndpoints)), Masking.Email(email));
            // Same answer for an unknown email and a wrong password.
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(tokens.Issue(user));
    }

    private static async Task<Ok<MeResponse>> Me(ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == principal.UserId(), ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The account no longer exists.");
        return TypedResults.Ok(new MeResponse(user.Id, user.Email, user.DisplayName, user.CreatedAt));
    }

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= User.EmailMaxLength
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal);

    private static DomainException EmailTaken() => new(ErrorCodes.EmailTaken, "An account with this email already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in for {MaskedEmail}")]
    private static partial void LogFailed(ILogger logger, string maskedEmail);
}
