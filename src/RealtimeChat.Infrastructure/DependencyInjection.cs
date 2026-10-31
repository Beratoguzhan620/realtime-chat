using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChatPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ChatDbContext>(options => ConfigureDbContext(options, connectionString));
        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
