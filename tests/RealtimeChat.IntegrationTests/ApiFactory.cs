using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RealtimeChat.Infrastructure.Notifications;
using RealtimeChat.IntegrationTests.Support;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace RealtimeChat.IntegrationTests;

/// <summary>
/// Boots the real API against throwaway PostgreSQL and Redis containers shared by the whole collection.
/// Unread-message emails go to a recording client instead of the notification-service.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();

    public string DatabaseConnectionString => _database.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    public RecordingNotificationClient Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Configure(builder, this, migrate: true);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<INotificationClient>();
            services.AddSingleton<INotificationClient>(Emails);
        });
    }

    /// <summary>The settings every instance in the tests shares: the same database, Redis and signing key.</summary>
    internal static void Configure(IWebHostBuilder builder, ApiFactory shared, bool migrate)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Chat", shared.DatabaseConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", shared.RedisConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", migrate ? "true" : "false");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Auth:AttemptsPerMinute", "1000");
        builder.UseSetting("UnreadEmails:QuietPeriod", "00:00:01");
        builder.UseSetting("UnreadEmails:CheckInterval", "01:00:00");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _redis.StartAsync());
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_database.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

/// <summary>A second API instance on the same database and Redis, like the second container behind nginx.</summary>
public sealed class SecondInstance(ApiFactory shared) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => ApiFactory.Configure(builder, shared, migrate: false);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
