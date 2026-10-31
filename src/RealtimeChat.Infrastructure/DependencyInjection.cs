using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealtimeChat.Infrastructure.Notifications;
using RealtimeChat.Infrastructure.Persistence;
using RealtimeChat.Infrastructure.Presence;
using StackExchange.Redis;

namespace RealtimeChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChatPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ChatDbContext>(options => ConfigureDbContext(options, connectionString));
        return services;
    }

    /// <summary>Presence in Redis, its sweeper, and the unread-message emails.</summary>
    public static IServiceCollection AddChatPresence(this IServiceCollection services, IConfiguration configuration, IConnectionMultiplexer redis)
    {
        services.AddSingleton(redis);
        services.Configure<PresenceOptions>(configuration.GetSection(PresenceOptions.Section));
        services.AddSingleton<PresenceTracker>();
        services.AddHostedService<PresenceSweeper>();

        var emails = configuration.GetSection(UnreadEmailOptions.Section);
        services.Configure<UnreadEmailOptions>(emails);
        if (emails.Get<UnreadEmailOptions>() is { IsConfigured: true })
        {
            services.AddHttpClient<INotificationClient, NotificationServiceClient>(http => http.Timeout = TimeSpan.FromSeconds(10));
        }
        else
        {
            services.AddSingleton<INotificationClient, DisabledNotificationClient>();
        }

        services.AddSingleton<UnreadEmailSender>();
        services.AddHostedService(provider => provider.GetRequiredService<UnreadEmailSender>());
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
