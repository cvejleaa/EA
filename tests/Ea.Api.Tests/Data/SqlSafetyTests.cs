using Ea.Api.Data;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Data;

/// <summary>
/// SQL-injektion: al dataadgang går gennem EF Core/LINQ med parametre, og API'ets eneste SQL-tekst er den faste
/// låseliste (<see cref="TableLocks"/>). Én vagt, samlet her: en ny rå SQL-sti gør testen rød. Analyzerne i bygget
/// (EF1002, CA2100, CA3001) og CodeQL i CI er det andet lag.
/// </summary>
public sealed class SqlSafetyTests
{
    /// <summary>Kald, der sender tekst direkte som SQL (eller udenom EF). Må ikke findes i API'et.</summary>
    private static readonly string[] RawSqlApis =
        ["ExecuteSqlRaw", "FromSqlRaw", "SqlQueryRaw", "NpgsqlCommand", "NpgsqlBatch", "DbCommand", "CommandText"];

    /// <summary>EF's SQL-kald med interpoleret tekst (parametriseret). Må kun bruges af den faste låseliste.</summary>
    private static readonly string[] SqlApis =
        ["ExecuteSql(", "ExecuteSqlAsync(", "ExecuteSqlInterpolated", "FromSql(", "FromSqlInterpolated", "SqlQuery<", "SqlQuery("];

    /// <summary>API'ets kildekode — uden migreringerne, der er genereret DDL og køres af migreringen, ikke af appen.</summary>
    private static List<(string Path, string Text)> ApiSources()
    {
        var root = RepoPaths.File("src", "Ea.Api");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal)
                && !f.StartsWith("obj/", StringComparison.Ordinal)
                && !f.StartsWith("Data/Migrations/", StringComparison.Ordinal))
            .Select(f => (Path: f, Text: File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        // En scanning uden filer beviser intet: de filer, der før havde rå SQL, skal være med.
        Assert.Contains(sources, s => s.Path == "Common/CsvImport.cs");
        Assert.Contains(sources, s => s.Path == "Systems/SystemEndpoints.cs");
        return sources;
    }

    [Fact]
    public void API_et_bruger_ingen_raa_SQL()
    {
        var offenders = ApiSources()
            .SelectMany(s => RawSqlApis.Where(api => s.Text.Contains(api, StringComparison.Ordinal)).Select(api => $"{s.Path}: {api}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SQL_tekst_findes_kun_i_den_faste_laaseliste()
    {
        var files = ApiSources()
            .Where(s => SqlApis.Any(api => s.Text.Contains(api, StringComparison.Ordinal)))
            .Select(s => s.Path)
            .ToList();

        Assert.Equal(["Data/TableLocks.cs"], files);
    }

    [Theory]
    [InlineData(TableLock.Couplings, "LOCK TABLE ea.system_capabilities IN ROW EXCLUSIVE MODE")]
    [InlineData(TableLock.CapabilitiesAndCouplings, "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE")]
    public void Hver_laas_er_en_konstant_tekst_uden_argumenter(TableLock tableLock, string expected)
    {
        var sql = TableLocks.Sql(tableLock);

        Assert.Equal(0, sql.ArgumentCount); // Intet udefra i SQL'en — heller ikke som parameter.
        Assert.Equal(expected, sql.Format);
    }

    [Fact]
    public void Alle_laase_har_en_tekst()
    {
        Assert.All(Enum.GetValues<TableLock>(), l => Assert.StartsWith("LOCK TABLE ea.", TableLocks.Sql(l).Format, StringComparison.Ordinal));
    }
}
