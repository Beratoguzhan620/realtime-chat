using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RealtimeChat.Infrastructure.Presence;

/// <summary>Called with the users the sweeper took offline, so their contacts can be told.</summary>
public interface IPresenceListener
{
    Task WentOfflineAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>Takes users offline whose connections expired without saying goodbye.</summary>
internal sealed partial class PresenceSweeper(
    PresenceTracker presence, IPresenceListener listener, IOptions<PresenceOptions> options, TimeProvider clock, ILogger<PresenceSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var offline = await presence.SweepAsync();
                if (offline.Count > 0)
                {
                    await listener.WentOfflineAsync(offline, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Presence sweep failed; it runs again at the next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
