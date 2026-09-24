using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Data;

public static class DatabaseSetup
{
    public const string ConnectionName = "Ea";

    /// <summary>
    /// ÉT sted for provider og forbindelse. Ved flytning til Azure (PostgreSQL Flexible Server) er det her,
    /// managed identity/Entra-token og SSL Mode=Require sættes ind.
    /// </summary>
    public static IServiceCollection AddEaDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionName} mangler.");

        services.AddDbContext<EaDbContext>(options => Configure(options, connectionString));
        return services;
    }

    /// <summary>Bruges også af testene, så migrationer og schema-opsætning aldrig driver fra hinanden.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", "ea")
                .EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention();
}
