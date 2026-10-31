namespace RealtimeChat.Infrastructure.Presence;

public sealed class PresenceOptions
{
    public const string Section = "Presence";

    /// <summary>A connection that has not sent a heartbeat for this long is gone, even if its server died silently.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often expired connections are cleaned up.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(15);
}
