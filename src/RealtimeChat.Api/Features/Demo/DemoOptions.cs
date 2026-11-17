namespace RealtimeChat.Api.Features.Demo;

/// <summary>
/// The public demo: two published people to sign in as (one per window, to watch a message arrive, typing and
/// read receipts), a group with some history, and two colleagues who answer what is written to them. Off elsewhere.
/// </summary>
public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; }

    /// <summary>Where this API listens, for the demo's own calls.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    /// <summary>How often the colleagues look for something to answer.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(4);

    public string FirstEmail { get; set; } = "ayse@chat.demo";

    public string SecondEmail { get; set; } = "mehmet@chat.demo";

    /// <summary>Shared by every demo account; published on the sign-in page, so it guards nothing.</summary>
    public string Password { get; set; } = "chat-demo-password";
}
