namespace RealtimeChat.Infrastructure.Notifications;

public sealed class UnreadEmailOptions
{
    public const string Section = "UnreadEmails";

    /// <summary>The notification-service address. Empty turns unread emails off.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>This service's key at the notification-service; from secrets, never from the repository.</summary>
    public string? ApiKey { get; set; }

    public string Template { get; set; } = "chat-unread";

    public string Language { get; set; } = "tr";

    /// <summary>How long a conversation must be quiet before its offline readers get an email.</summary>
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
