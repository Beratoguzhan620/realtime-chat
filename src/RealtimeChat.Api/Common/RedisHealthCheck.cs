using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace RealtimeChat.Api.Common;

/// <summary>Ready only when Redis answers: without it, presence and delivery across instances stop working.</summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis does not answer.", ex);
        }
    }
}
