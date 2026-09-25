using Ea.Api.Data;
using Npgsql;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>
/// Til deterministiske låse-race-tests: en rå forbindelse holder en lås (som en samtidig import eller et gem), og
/// testen venter, til API-kaldet står i kø på den, før låsen slippes.
/// </summary>
public static class DbLocks
{
    public static async Task Sql(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Tager præcis den lås, API'et tager (samme faste liste), så testen efterligner produktionen.</summary>
    public static Task Lock(NpgsqlConnection connection, NpgsqlTransaction transaction, TableLock tableLock) =>
        Sql(connection, transaction, FormattableString.Invariant(TableLocks.Sql(tableLock)));

    /// <summary>Venter, til en anden forbindelse står i kø på en lås til <paramref name="table"/> (så testen rammer vinduet).</summary>
    public static async Task WaitForBlockedLockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table = "ea.system_capabilities")
    {
        for (var i = 0; i < 200; i++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE NOT granted AND relation = @table::regclass", connection, transaction);
            command.Parameters.AddWithValue("table", table);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Kaldet nåede aldrig at vente på låsen til {table}.");
    }

    /// <summary>
    /// Venter, til en anden forbindelse til SAMME database venter på en lås af enhver art — også en rækkelås, som
    /// <see cref="WaitForBlockedLockAsync"/> ikke ser (pg_locks er fælles for hele serveren; pg_stat_activity kan
    /// afgrænses til testens egen database).
    /// </summary>
    public static async Task WaitForBlockedBackendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        for (var i = 0; i < 200; i++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()",
                connection, transaction);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Kaldet nåede aldrig at vente på en lås.");
    }
}
