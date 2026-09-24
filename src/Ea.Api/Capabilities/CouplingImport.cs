using Ea.Api.Common;
using Ea.Api.Data;
using Ea.Api.Integrations;
using Ea.Api.Systems;

namespace Ea.Api.Capabilities;

/// <summary>Én række fra koblingsfilen: nøglerne og det, tør-kørslen sammenligner med registret.</summary>
public sealed record CouplingImportRow(
    int Line, Guid SystemId, string FullName, string Code, string Changed, IReadOnlyDictionary<string, string> SystemValues);

/// <summary>
/// Det, importen vil gøre — beregnet ens i tør-kørslen og når importen gennemføres. <c>Diff</c> og
/// <c>Systems</c> bruges kun til at gemme; fingeraftrykket er af <c>Changes</c>.
/// </summary>
public sealed record CouplingImportPlan(
    IReadOnlyList<ImportRowError> Errors,
    IReadOnlyList<ImportRowError> Warnings,
    IReadOnlyList<CouplingChange> Changes,
    IReadOnlyList<CoupledSystem> NotInFile,
    CouplingImportSummary Summary,
    IReadOnlyDictionary<Guid, (HashSet<Guid> Add, HashSet<Guid> Remove)> Diff,
    IReadOnlyDictionary<Guid, SystemEntity> Systems);

/// <summary>
/// Import af koblings-CSV'en (docs/csv-koblinger.md): filen er HELE sandheden om egne koblinger for de systemer, der
/// står i den. Systemer, der ikke står i filen, røres ikke. Nøglerne er <c>SystemId</c> og <c>Kode</c>; de andre
/// kolonner er beregnede og indlæses ikke. Fejl i filen stopper alt — der gemmes aldrig en halv import.
/// </summary>
public static class CouplingImport
{
    /// <summary>
    /// Plads til ca. 2000 systemer (hele DTU) med 5 koblinger hver, med dobbelt margin. En eksport af den størrelse er
    /// målt i <c>CouplingCsvTemplateTests.En_eksport_af_hele_DTU_kan_indlaeses_igen</c>.
    /// </summary>
    public const int MaxRows = 20_000;

    /// <summary>
    /// <c>DelesMed</c> og beskrivelsen gentages på hver række, så en eksport af hele DTU er stor. Grænsen giver plads
    /// til den med margin — se testen nævnt ved <see cref="MaxRows"/>. Den er over Kestrels standard (30 MB), så
    /// endpointet hæver grænsen for netop denne request; kun EA.Admin når så langt (policyen kører først).
    /// </summary>
    public const int MaxFileBytes = 64 * 1024 * 1024;

    public static (List<CouplingImportRow> Rows, List<ImportRowError> Errors) Parse(CsvReadResult csv)
    {
        if (csv.Error is not null)
        {
            return ([], [csv.Error]);
        }

        if (CsvImport.HeaderError(csv.Header, CouplingCsv.Header) is { } headerError)
        {
            return ([], [headerError]);
        }

        if (csv.Rows.Count == 0)
        {
            return ([], [new ImportRowError(1, null,
                "Filen indeholder ingen rækker. Hent koblingerne, ret dem i Excel, og indlæs filen igen.")]);
        }

        if (csv.Rows.Count > MaxRows)
        {
            return ([], [new ImportRowError(1, null,
                $"Filen har flere end {MaxRows} rækker; højst {MaxRows} kan indlæses ad gangen. Del den op efter system.")]);
        }

        var index = CouplingCsv.Header.Select((column, i) => (column, i)).ToDictionary(x => x.column, x => x.i);
        var errors = new List<ImportRowError>();
        var rows = new List<CouplingImportRow>(csv.Rows.Count);
        var seen = new Dictionary<(Guid, string), int>();
        foreach (var row in csv.Rows)
        {
            string Field(string column) => index[column] < row.Fields.Count ? row.Fields[index[column]].Trim() : "";

            var id = Field("SystemId");
            if (id.Length == 0)
            {
                errors.Add(new ImportRowError(row.Line, "SystemId",
                    "SystemId mangler. Kopiér en række fra eksporten, eller find id'et med \"Hent systemliste (CSV)\"."));
                continue;
            }

            if (!Guid.TryParse(id, out var systemId))
            {
                errors.Add(new ImportRowError(row.Line, "SystemId", $"\"{id}\" er ikke et gyldigt SystemId."));
                continue;
            }

            var code = Field("Kode");
            if (code.Length > 0 && !seen.TryAdd((systemId, CapabilityRules.NormalizeCode(code)), row.Line))
            {
                errors.Add(new ImportRowError(row.Line, "Kode",
                    $"{code} står også for samme system i linje {seen[(systemId, CapabilityRules.NormalizeCode(code))]}."));
                continue;
            }

            rows.Add(new CouplingImportRow(row.Line, systemId, Field("FuldtNavn"), code, Field("SidstÆndret"),
                CouplingCsv.SystemColumns.ToDictionary(column => column, Field)));
        }

        return (rows, errors.OrderBy(e => e.Line).Take(CsvImport.MaxErrors).ToList());
    }

    /// <param name="systems">Systemerne i filen, der findes, med forælder, team, roller og koblinger indlæst.</param>
    /// <param name="capabilities">Hele kortet, også udgåede (en uændret kobling til en udgået bevares).</param>
    /// <param name="notInFile">Systemer med koblinger, som ikke står i filen.</param>
    public static CouplingImportPlan Plan(
        IReadOnlyList<CouplingImportRow> rows,
        IReadOnlyDictionary<Guid, SystemEntity> systems,
        IReadOnlyCollection<Capability> capabilities,
        IReadOnlyList<CoupledSystem> notInFile)
    {
        var byCode = capabilities.ToDictionary(c => c.CodeNormalized);
        var byId = capabilities.ToDictionary(c => c.Id);
        var withChildren = CapabilityRules.WithChildren(capabilities);
        var errors = new List<ImportRowError>();
        var warnings = new List<ImportRowError>();
        var changes = new List<CouplingChange>();
        var diff = new Dictionary<Guid, (HashSet<Guid> Add, HashSet<Guid> Remove)>();
        int added = 0, removed = 0, unchanged = 0, cleared = 0, changedSinceExport = 0, ignoredEdits = 0;

        foreach (var group in rows.GroupBy(r => r.SystemId))
        {
            if (!systems.TryGetValue(group.Key, out var system))
            {
                errors.Add(new ImportRowError(group.First().Line, "SystemId",
                    $"Systemet med id {group.Key} findes ikke i registret (slettet?). Fjern rækkerne, eller hent en ny eksport."));
                continue;
            }

            var fullName = IntegrationCsv.FullName(system);
            var wanted = new List<(CouplingImportRow Row, Capability Capability)>();
            foreach (var row in group)
            {
                if (row.FullName.Length > 0 && !string.Equals(row.FullName, fullName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ImportRowError(row.Line, "FuldtNavn",
                        $"FuldtNavn er \"{row.FullName}\", men systemet hedder nu \"{fullName}\". Ret navnet, eller tøm cellen."));
                }

                if (row.Code.Length == 0)
                {
                    continue;
                }

                if (byCode.TryGetValue(CapabilityRules.NormalizeCode(row.Code), out var capability))
                {
                    wanted.Add((row, capability));
                }
                else
                {
                    errors.Add(new ImportRowError(row.Line, "Kode",
                        $"Koden \"{row.Code}\" findes ikke i kortet. Koderne står i \"Hent kortet (CSV)\"."));
                }
            }

            if (wanted.Count > CapabilityRules.MaxCouplingsPerSystem)
            {
                errors.Add(new ImportRowError(wanted[CapabilityRules.MaxCouplingsPerSystem].Row.Line, "Kode",
                    $"{fullName} har {wanted.Count} koblinger i filen; højst {CapabilityRules.MaxCouplingsPerSystem}."));
            }

            // Kun NYE koblinger skal være et blad, der ikke er udgået — en uændret bevares (samme regel som formularen).
            var current = system.CapabilityLinks.Select(l => l.CapabilityId).ToHashSet();
            foreach (var (row, capability) in wanted.Where(w => !current.Contains(w.Capability.Id)))
            {
                if (CapabilityRules.CoupleBlockedReason(capability, withChildren.Contains(capability.Id)) is { } reason)
                {
                    errors.Add(new ImportRowError(row.Line, "Kode", reason));
                }
            }

            var wantedIds = wanted.Select(w => w.Capability.Id).ToHashSet();
            var add = wantedIds.Where(id => !current.Contains(id)).ToHashSet();
            var remove = current.Where(id => !wantedIds.Contains(id)).ToHashSet();
            var changed = group.Any(r => r.Changed.Length > 0 && r.Changed != CouplingCsv.ChangedToken(system));
            var display = new CoupledSystem(system.Id, CapabilityQueries.DisplayName(system.Name, system.ParentSystem?.Name));
            CouplingChange Change(CouplingChangeKind kind, Guid id) =>
                new(kind, display, byId[id].Code, byId[id].Name, CapabilityRules.DisplayPath(byId[id], byId), changed);

            changes.AddRange(remove.Select(id => Change(CouplingChangeKind.Fjernes, id)));
            changes.AddRange(add.Select(id => Change(CouplingChangeKind.Tilfoejes, id)));
            diff[system.Id] = (add, remove);
            added += add.Count;
            removed += remove.Count;
            unchanged += current.Count - remove.Count;
            cleared += wantedIds.Count == 0 && current.Count > 0 ? 1 : 0;
            changedSinceExport += changed && add.Count + remove.Count > 0 ? 1 : 0;

            var today = CouplingCsv.SystemValues(system);
            foreach (var row in group)
            {
                var edited = CouplingCsv.SystemColumns
                    .Where(column => row.SystemValues[column].Length > 0 &&
                        !string.Equals(row.SystemValues[column], (today[column] ?? "").Trim(), StringComparison.Ordinal))
                    .ToList();
                ignoredEdits += edited.Count > 0 ? 1 : 0;
                warnings.AddRange(edited.Select(column => new ImportRowError(row.Line, column,
                    $"{column} er rettet i filen, men indlæses ikke. Ret det på systemsiden.")));
            }
        }

        var ordered = changes
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.System.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Code, CapabilityRules.CodeOrder)
            .ToList();
        var summary = new CouplingImportSummary(
            SystemsInFile: systems.Count,
            SystemsChanged: diff.Values.Count(d => d.Add.Count + d.Remove.Count > 0),
            Added: added,
            Removed: removed,
            Unchanged: unchanged,
            SystemsCleared: cleared,
            LargeRemoval: removed > 0 && removed > (removed + unchanged) * CapabilityRules.LargeRemovalShare,
            SystemsChangedSinceExport: changedSinceExport,
            SystemsNotInFile: notInFile.Count,
            IgnoredEdits: ignoredEdits);

        return new CouplingImportPlan(
            errors.OrderBy(e => e.Line).Take(CsvImport.MaxErrors).ToList(),
            warnings.OrderBy(w => w.Line).Take(CsvImport.MaxErrors).ToList(),
            ordered, notInFile, summary, diff, systems);
    }

    /// <summary>Fingeraftrykket af ændringerne (inkl. om systemet er ændret efter eksporten).</summary>
    public static string Fingerprint(CouplingImportPlan plan) => CsvImport.Fingerprint(plan.Changes);

    /// <summary>
    /// Gemmer planen på de (sporede) systemer. Et ændret system får ny <c>UpdatedAt</c> — så en formular, der var åben
    /// før importen, får "ændret af en anden" i stedet for at rulle importen tilbage — men IKKE en ny bekræftelse:
    /// den er forvalterens udsagn om, at data er rigtige.
    /// </summary>
    public static void Apply(CouplingImportPlan plan, EaDbContext db, DateTimeOffset now)
    {
        foreach (var (systemId, (add, remove)) in plan.Diff.Where(d => d.Value.Add.Count + d.Value.Remove.Count > 0))
        {
            var system = plan.Systems[systemId];
            system.CapabilityLinks.RemoveAll(l => remove.Contains(l.CapabilityId));
            system.CapabilityLinks.AddRange(add.Select(id => new SystemCapability { SystemId = systemId, CapabilityId = id }));
            system.UpdatedAt = now;
            // Rækken skrives ALTID (som i formularen), så samtidighedstjekket (xmin) også sker, når uret ikke har
            // flyttet sig — en samtidig ændring af systemet afviser så importen i stedet for at blive overset.
            db.Entry(system).Property(s => s.UpdatedAt).IsModified = true;
        }
    }
}
