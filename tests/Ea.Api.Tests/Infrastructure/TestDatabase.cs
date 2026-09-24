using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>
/// Rigtig PostgreSQL pr. test: en skabelon-database migreres én gang pr. kørsel, og hver test får sin egen
/// kopi (CREATE DATABASE … TEMPLATE). Forbindelsen styres af EA_TEST_CONNECTION (samme standard som CI).
/// Skabelonen har et unikt navn pr. testproces, så to samtidige kørsler mod samme server (to udviklere,
/// en worktree, parallelle jobs) ikke sletter hinandens skabelon. Den fjernes igen i <see cref="TestDatabaseLifetime"/>.
/// </summary>
public static class TestDatabase
{
    private const string Prefix = "ea_test_";
    private static readonly string TemplateName = $"{Prefix}tpl_{Guid.NewGuid():N}";
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

            var name = Prefix + Guid.NewGuid().ToString("N");
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"");
            return name;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Sletter testens database. WITH (FORCE) afslutter forbindelserne til den — men lokalt er rollen ikke superuser
    /// (scripts/dev-db.sh), og er PostgreSQL's autovacuum i gang på databasen, afviser serveren at afslutte den
    /// (42501). Det varer millisekunder, så der prøves igen i stedet for at gøre en grøn test rød ved oprydningen.
    /// I CI er rollen superuser, og fejlen opstår ikke.
    /// </summary>
    public static async Task DropAsync(string name)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege && attempt < 50)
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>Kaldes, når alle tests er kørt.</summary>
    internal static async Task DropTemplateAsync()
    {
        if (_templateReady)
        {
            await DropAsync(TemplateName);
        }
    }

    private static async Task CreateTemplateAsync()
    {
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

/// <summary>Rydder denne kørsels skabelon-database op, når alle tests er færdige.</summary>
public sealed class TestDatabaseLifetime : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await TestDatabase.DropTemplateAsync();
}
