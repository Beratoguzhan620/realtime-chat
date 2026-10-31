namespace RealtimeChat.Core;

/// <summary>The stable error codes returned in problem responses and hub errors.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string InvalidState = "invalid_state";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string RateLimited = "rate_limited";
}
