using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Ea.Api.Data;

/// <summary>
/// De tabel-låse, formularen, sletning og importerne tager. Al anden dataadgang går gennem EF Core/LINQ med parametre;
/// dette er API'ets ENESTE SQL-tekst, og den er en fast liste: en kalder vælger en værdi, aldrig en streng
/// (se <c>SqlSafetyTests</c>).
/// </summary>
public enum TableLock
{
    /// <summary>Formularen og sletning ændrer koblinger. Står i kø bag en import, men ikke bag hinanden.</summary>
    Couplings,

    /// <summary>Importerne (kortet og koblingerne): hele kortet og alle koblinger låses mod samtidige ændringer.</summary>
    CapabilitiesAndCouplings,
}

public static class TableLocks
{
    /// <summary>
    /// Låsens SQL. Konstante tekster — LOCK TABLE kan ikke tage parametre, så tabelnavne må aldrig komme udefra. Går
    /// gennem <see cref="RelationalDatabaseFacadeExtensions.ExecuteSqlAsync"/>, så et interpoleret argument ville blive
    /// en parameter (og fejle), ikke tekst i SQL'en.
    /// </summary>
    public static FormattableString Sql(TableLock tableLock) => tableLock switch
    {
        TableLock.Couplings => $"LOCK TABLE ea.system_capabilities IN ROW EXCLUSIVE MODE",
        TableLock.CapabilitiesAndCouplings => $"LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE",
        _ => throw new ArgumentOutOfRangeException(nameof(tableLock), tableLock, "Ukendt lås."),
    };

    /// <summary>Tager låsen i den aktuelle transaktion (den slippes ved commit eller rollback).</summary>
    public static Task LockAsync(this DatabaseFacade database, TableLock tableLock, CancellationToken ct) =>
        database.ExecuteSqlAsync(Sql(tableLock), ct);
}
