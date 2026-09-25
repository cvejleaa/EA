using System.Text.RegularExpressions;
using Ea.Api.Data;
using Ea.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ea.Api.Tests.Data;

/// <summary>
/// SQL-injektion: al dataadgang går gennem EF Core/LINQ med parametre, og API'ets eneste SQL-tekst er den faste
/// låseliste (<see cref="TableLocks"/>). Én vagt, samlet her: en ny rå SQL-sti gør testen rød. Analyzerne i bygget
/// (EF1002, CA2100, CA3001) og CodeQL i CI er det andet lag.
/// </summary>
public sealed class SqlSafetyTests
{
    /// <summary>
    /// Kald, der sender tekst direkte som SQL eller går udenom EF: EF's rå metoder, Npgsql's egne kommandoer, datakilde
    /// og COPY, EF's rå forbindelse og en FormattableString bygget af en almindelig streng. Listen er en blokliste —
    /// CodeQL i CI er bagstopperen for veje, den ikke kender.
    /// </summary>
    private static readonly string[] RawSqlApis =
    [
        "ExecuteSqlRaw", "FromSqlRaw", "SqlQueryRaw",
        "NpgsqlCommand", "NpgsqlBatch", "NpgsqlDataSource", "NpgsqlConnection",
        "DbCommand", "CommandText", "CreateCommand", "GetDbConnection",
        "BeginTextImport", "BeginTextExport", "BeginBinaryImport", "BeginBinaryExport", "BeginRawBinaryCopy",
        "FormattableStringFactory",
    ];

    /// <summary>EF's SQL-kald med interpoleret tekst (parametriseret). Må kun bruges af den faste låseliste.</summary>
    private static readonly string[] SqlApis =
        ["ExecuteSql(", "ExecuteSqlAsync(", "ExecuteSqlInterpolated", "FromSql(", "FromSqlInterpolated", "SqlQuery<", "SqlQuery("];

    /// <summary>
    /// Al API'ets kildekode — også migrations-mappen, så en håndskrevet hjælper dér ikke slipper udenom. (De genererede
    /// migreringer bruger kun migrationBuilder og rammer ingen af mønstrene.)
    /// </summary>
    private static List<(string Path, string Text)> ApiSources()
    {
        var root = RepoPaths.File("src", "Ea.Api");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal)
                && !f.StartsWith("obj/", StringComparison.Ordinal))
            .Select(f => (Path: f, Text: File.ReadAllText(Path.Combine(root, f))))
            .ToList();

        // En scanning uden filer beviser intet: de filer, der før havde rå SQL, skal være med.
        Assert.Contains(sources, s => s.Path == "Common/CsvImport.cs");
        Assert.Contains(sources, s => s.Path == "Systems/SystemEndpoints.cs");
        Assert.Contains(sources, s => s.Path == "Data/Migrations/EaDbContextModelSnapshot.cs");
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
    public void API_et_har_praecis_et_SQL_kald_og_det_tager_en_fast_laas()
    {
        // Hvert forekomst tælles — også et ekstra kald i selve låsefilen skal gøre testen rød.
        var calls = ApiSources()
            .SelectMany(s => SqlApis.SelectMany(api => Occurrences(s.Text, api).Select(_ => $"{s.Path}: {api}")))
            .ToList();

        Assert.Equal(["Data/TableLocks.cs: ExecuteSqlAsync("], calls);
    }

    /// <summary>
    /// Positivlisten: de ENESTE filer, der må bruge databasedriveren eller ADO.NET direkte, og hvorfor. Et nyt driver-
    /// API — også et, bloklisten ikke kender (Dapper, en ny Npgsql-metode) — kræver navnerummet og gør testen rød.
    /// </summary>
    private static readonly string[] DriverUsers =
    [
        "Common/Problems.cs", // Læser PostgreSQL's fejlkode for en unik-overtrædelse (PostgresErrorCodes) — ingen SQL.
    ];

    [Fact]
    public void Kun_kendte_filer_bruger_databasedriveren_direkte()
    {
        // De genererede migreringer nævner Npgsql i EF's metadata; en håndskrevet fil i mappen tæller med.
        var generated = new Regex(@"^Data/Migrations/(\d{14}_.*|EaDbContextModelSnapshot)\.cs$");
        var driver = new Regex(@"\b(Npgsql|System\.Data|Dapper)\b");

        var files = ApiSources()
            .Where(s => !generated.IsMatch(s.Path) && driver.IsMatch(s.Text))
            .Select(s => s.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(DriverUsers, files);
    }

    /// <summary>Tabeller, appen bevidst kun må læse og tilføje i (fx ændringshistorikken, beslutning W). Tom i dag.</summary>
    private static readonly string[] AppendOnlyTables = [];

    /// <summary>
    /// Mindste rettighed følger modellen: hver tabel står enten i scriptets UPDATE/DELETE-liste eller er bevidst
    /// append-only. En ny tabel gør testen rød, indtil nogen har taget stilling (scripts/db-roller.sql, beslutning X).
    /// </summary>
    [Fact]
    public async Task Rettighedsscriptet_kender_hver_tabel_i_modellen()
    {
        await using var app = await TestApp.StartAsync();
        using var scope = app.Services.CreateScope();
        var tables = scope.ServiceProvider.GetRequiredService<EaDbContext>().Model.GetEntityTypes()
            .Select(e => e.GetTableName()!)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var script = await File.ReadAllTextAsync(RepoPaths.File("scripts", "db-roller.sql"));
        var grant = Regex.Match(script, @"GRANT UPDATE, DELETE ON(?<tables>[^;]*?)TO ea_app;", RegexOptions.Singleline);
        Assert.True(grant.Success, "Scriptet mangler GRANT UPDATE, DELETE ... TO ea_app.");
        var mutable = grant.Groups["tables"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace("ea.", "", StringComparison.Ordinal));

        Assert.Contains("systems", tables);
        Assert.Equal(tables, mutable.Concat(AppendOnlyTables).Order(StringComparer.Ordinal));
    }

    private static IEnumerable<int> Occurrences(string text, string value)
    {
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            yield return i;
        }
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
