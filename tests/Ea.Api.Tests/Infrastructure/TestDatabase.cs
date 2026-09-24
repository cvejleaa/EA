using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>
/// Rigtig PostgreSQL pr. test: en skabelon-database migreres én gang pr. kørsel, og hver test får sin egen
/// kopi (CREATE DATABASE … TEMPLATE). Forbindelsen styres af EA_TEST_CONNECTION (samme standard som CI).
/// </summary>
public static class TestDatabase
{
    private const string TemplateName = "ea_test_template";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _templateReady;

    private static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("EA_TEST_CONNECTION")
        ?? "Host=localhost;Username=ea;Password=ea;Database=postgres";

    public static string ConnectionStringFor(string database, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database, Pooling = pooling }.ConnectionString;

    public static async Task<string> CreateAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!_templateReady)
            {
                await CreateTemplateAsync();
                _templateReady = true;
            }

            var name = "ea_test_" + Guid.NewGuid().ToString("N");
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"");
            return name;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task DropAsync(string name) => ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

    private static async Task CreateTemplateAsync()
    {
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{TemplateName}\" WITH (FORCE)");
        await ExecuteAsync($"CREATE DATABASE \"{TemplateName}\"");

        // Uden pooling: skabelonen må ikke have åbne forbindelser, når den kopieres.
        var options = DatabaseSetup.Configure(
            new DbContextOptionsBuilder<EaDbContext>(), ConnectionStringFor(TemplateName, pooling: false));
        await using var db = new EaDbContext((DbContextOptions<EaDbContext>)options.Options);
        await db.Database.MigrateAsync();
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres", pooling: false));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
