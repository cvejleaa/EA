using System.Security.Cryptography;
using System.Text.Json;
using Ea.Api.Common;
using Ea.Api.Data;

namespace Ea.Api.Capabilities;

/// <summary>En gyldig række fra filen (koden og forælderkoden som skrevet; sammenlignes via NormalizeCode).</summary>
public sealed record CapabilityImportRow(int Line, string Code, string Name, string? ParentCode, string? Description);

/// <summary>Det, importen vil gøre — beregnet ens i tør-kørslen og når importen gennemføres.</summary>
public sealed record CapabilityImportPlan(
    IReadOnlyList<CapabilityImportRow> Rows,
    IReadOnlyList<CapabilityChange> Changes,
    CapabilityImportSummary Summary);

/// <summary>
/// Import af kapabilitetskortet: filen er HELE kortet. Nye koder oprettes, kendte opdateres, og koder, der ikke
/// står i filen, slettes. Fejl i filen stopper alt — der gemmes aldrig en halv import.
/// </summary>
public static class CapabilityImport
{
    /// <summary>Nok til at rette filen; flere fejl ville kun gøre svaret uoverskueligt.</summary>
    public const int MaxErrors = 200;

    public static (List<CapabilityImportRow> Rows, List<ImportRowError> Errors) Parse(CsvReadResult csv)
    {
        if (csv.Error is not null)
        {
            return ([], [csv.Error]);
        }

        var header = WithoutTrailingEmpty(csv.Header);
        if (header.Count == 0)
        {
            return ([], [new ImportRowError(1, null, "Filen er tom.")]);
        }

        if (!header.SequenceEqual(CapabilityCsv.Header))
        {
            var hint = header.Count == 1 && header[0].Contains(',', StringComparison.Ordinal)
                ? " Filen ser ud til at bruge komma som skilletegn. " + Csv.SaveAsUtf8Advice
                : "";
            return ([], [new ImportRowError(1, null,
                $"Første linje skal være præcis: {string.Join(Csv.Separator, CapabilityCsv.Header)}.{hint}")]);
        }

        if (csv.Rows.Count == 0)
        {
            return ([], [new ImportRowError(1, null,
                "Filen indeholder ingen kapabiliteter. En import erstatter hele kortet, så en tom fil ville slette det.")]);
        }

        if (csv.Rows.Count > CapabilityRules.MaxRows)
        {
            return ([], [new ImportRowError(1, null,
                $"Filen har {csv.Rows.Count} rækker; højst {CapabilityRules.MaxRows} kan indlæses ad gangen.")]);
        }

        var errors = new List<ImportRowError>();
        var rows = new List<CapabilityImportRow>();
        var firstLine = new Dictionary<string, int>();

        foreach (var row in csv.Rows)
        {
            var fields = WithoutTrailingEmpty(row.Fields);
            if (fields.Count > CapabilityCsv.Header.Count)
            {
                errors.Add(new ImportRowError(row.Line, null,
                    $"Linjen har {fields.Count} felter, men skal have {CapabilityCsv.Header.Count}. Står der et semikolon i en tekst uden citationstegn?"));
                continue;
            }

            string Field(int i) => i < fields.Count ? fields[i].Trim() : "";
            var code = Field(0);
            var name = Field(1);
            var parent = NullIfEmpty(Field(2));
            var description = NullIfEmpty(Field(3));

            if (code.Length == 0)
            {
                errors.Add(new ImportRowError(row.Line, "Kode", "Koden mangler."));
                continue;
            }

            if (code.Length > CapabilityRules.CodeMaxLength)
            {
                errors.Add(new ImportRowError(row.Line, "Kode", $"Koden er længere end {CapabilityRules.CodeMaxLength} tegn."));
            }

            if (firstLine.TryGetValue(CapabilityRules.NormalizeCode(code), out var earlier))
            {
                errors.Add(new ImportRowError(row.Line, "Kode", $"Koden \"{code}\" står også i linje {earlier}."));
                continue;
            }

            firstLine[CapabilityRules.NormalizeCode(code)] = row.Line;

            if (name.Length == 0)
            {
                errors.Add(new ImportRowError(row.Line, "Navn", "Navnet mangler."));
            }
            else if (name.Length > CapabilityRules.NameMaxLength)
            {
                errors.Add(new ImportRowError(row.Line, "Navn", $"Navnet er længere end {CapabilityRules.NameMaxLength} tegn."));
            }

            if (description?.Length > CapabilityRules.DescriptionMaxLength)
            {
                errors.Add(new ImportRowError(row.Line, "Beskrivelse",
                    $"Beskrivelsen er længere end {CapabilityRules.DescriptionMaxLength} tegn."));
            }

            rows.Add(new CapabilityImportRow(row.Line, code, name, parent, description));
        }

        errors.AddRange(TreeErrors(rows));
        return (rows, errors.OrderBy(e => e.Line).Take(MaxErrors).ToList());
    }

    /// <summary>
    /// Forældre, der ikke findes, ringe i forælder-kæden og for mange niveauer. Hver række besøges én gang
    /// (lineært), så en stor eller ondsindet fil ikke kan binde serveren.
    /// </summary>
    private static List<ImportRowError> TreeErrors(List<CapabilityImportRow> rows)
    {
        var errors = new List<ImportRowError>();
        var byCode = rows.ToDictionary(r => CapabilityRules.NormalizeCode(r.Code));

        CapabilityImportRow? ParentOf(CapabilityImportRow row) =>
            row.ParentCode is { } parent ? byCode.GetValueOrDefault(CapabilityRules.NormalizeCode(parent)) : null;

        foreach (var row in rows)
        {
            if (row.ParentCode is { } parent && ParentOf(row) is null)
            {
                errors.Add(new ImportRowError(row.Line, "ForælderKode", $"Forælderen \"{parent}\" findes ikke i filen."));
            }
        }

        // Niveau pr. række: 1 for det øverste niveau; null = ukendt (ring eller manglende forælder højere oppe).
        var level = new Dictionary<CapabilityImportRow, int?>();
        foreach (var start in rows)
        {
            var path = new List<CapabilityImportRow>();
            var onPath = new Dictionary<CapabilityImportRow, int>();
            var current = start;
            int? known = null;
            while (true)
            {
                if (level.TryGetValue(current, out var done))
                {
                    known = done;
                    break;
                }

                if (onPath.TryGetValue(current, out var ringStart))
                {
                    var ring = path.GetRange(ringStart, path.Count - ringStart);
                    foreach (var member in ring)
                    {
                        errors.Add(new ImportRowError(member.Line, "ForælderKode", RingMessage(ring, member)));
                        level[member] = null;
                    }

                    path.RemoveRange(ringStart, ring.Count);
                    known = null;
                    break;
                }

                onPath[current] = path.Count;
                path.Add(current);
                var parent = ParentOf(current);
                if (parent is null)
                {
                    // Øverste niveau — eller en manglende forælder, der allerede er meldt.
                    known = current.ParentCode is null ? 0 : null;
                    break;
                }

                current = parent;
            }

            // Tildel niveauer baglæns: rækken nærmest det kendte niveau først.
            for (var i = path.Count - 1; i >= 0; i--)
            {
                known = known is { } above ? above + 1 : null;
                level[path[i]] = known;
                if (known > CapabilityRules.MaxDepth)
                {
                    errors.Add(new ImportRowError(path[i].Line, "ForælderKode",
                        $"Kapabiliteten ligger på niveau {known}, men kortet må højst have {CapabilityRules.MaxDepth} niveauer."));
                }
            }
        }

        return errors;
    }

    /// <summary>Ringen set fra én af dens rækker, afkortet så beskeden forbliver læsbar.</summary>
    private static string RingMessage(List<CapabilityImportRow> ring, CapabilityImportRow member)
    {
        var from = ring.IndexOf(member);
        var codes = ring.Skip(from).Concat(ring.Take(from)).Select(r => r.Code).ToList();
        var shown = codes.Count <= 5 ? codes.Append(member.Code) : codes.Take(5).Append("…");
        return $"Forælder-kæden går i ring: {string.Join(" → ", shown)}.";
    }

    public static CapabilityImportPlan Plan(IReadOnlyList<CapabilityImportRow> rows, IReadOnlyCollection<Capability> existing)
    {
        var existingByCode = existing.ToDictionary(c => c.CodeNormalized);
        var existingCode = existing.ToDictionary(c => c.Id, c => c.Code);
        var rowByCode = rows.ToDictionary(r => CapabilityRules.NormalizeCode(r.Code));

        CapabilitySnapshot Before(Capability c) =>
            new(c.Code, c.Name, c.ParentId is { } p ? existingCode[p] : null, c.Description);

        // Forælderkoden skrives som forælderens egen række staver den — "ek-1" og "EK-1" er ikke en ændring.
        CapabilitySnapshot After(CapabilityImportRow r) =>
            new(r.Code, r.Name, r.ParentCode is { } p ? rowByCode[CapabilityRules.NormalizeCode(p)].Code : null, r.Description);

        var added = new List<CapabilityChange>();
        var changed = new List<CapabilityChange>();
        foreach (var row in rows)
        {
            if (!existingByCode.TryGetValue(CapabilityRules.NormalizeCode(row.Code), out var current))
            {
                added.Add(new CapabilityChange(CapabilityChangeKind.Ny, row.Code, null, After(row)));
            }
            else if (Before(current) != After(row))
            {
                changed.Add(new CapabilityChange(CapabilityChangeKind.Aendret, row.Code, Before(current), After(row)));
            }
        }

        var removed = existing
            .Where(c => !rowByCode.ContainsKey(c.CodeNormalized))
            .Select(c => new CapabilityChange(CapabilityChangeKind.Slettes, c.Code, Before(c), null))
            .ToList();

        // Det, der forsvinder, først — det er dét, en fejl i filen koster.
        var changes = removed.OrderBy(c => c.Code, CapabilityRules.CodeOrder)
            .Concat(changed.OrderBy(c => c.Code, CapabilityRules.CodeOrder))
            .Concat(added.OrderBy(c => c.Code, CapabilityRules.CodeOrder))
            .ToList();

        var summary = new CapabilityImportSummary(
            New: added.Count,
            Changed: changed.Count,
            Removed: removed.Count,
            Unchanged: rows.Count - added.Count - changed.Count,
            CurrentTotal: existing.Count,
            LargeRemoval: existing.Count > 0 && removed.Count > existing.Count * CapabilityRules.LargeRemovalShare);

        return new CapabilityImportPlan(rows, changes, summary);
    }

    /// <summary>
    /// Et fingeraftryk af ændringerne. Er kortet ændret, siden tør-kørslen blev lavet, giver den samme fil et andet
    /// aftryk — og importen afvises i stedet for at gemme noget, brugeren ikke har set.
    /// </summary>
    public static string Fingerprint(CapabilityImportPlan plan) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan.Changes)));

    public static void Apply(CapabilityImportPlan plan, IReadOnlyCollection<Capability> existing, EaDbContext db, DateTimeOffset now)
    {
        var byCode = existing.ToDictionary(c => c.CodeNormalized);
        var ids = existing.ToDictionary(c => c.CodeNormalized, c => c.Id);
        foreach (var row in plan.Rows)
        {
            ids.TryAdd(CapabilityRules.NormalizeCode(row.Code), Guid.CreateVersion7(now));
        }

        foreach (var row in plan.Rows)
        {
            var code = CapabilityRules.NormalizeCode(row.Code);
            if (!byCode.TryGetValue(code, out var capability))
            {
                capability = new Capability { Id = ids[code] };
                db.Capabilities.Add(capability);
            }

            capability.Code = row.Code;
            capability.Name = row.Name;
            capability.Description = row.Description;
            capability.ParentId = row.ParentCode is { } parent ? ids[CapabilityRules.NormalizeCode(parent)] : null;
        }

        foreach (var change in plan.Changes.Where(c => c.Kind == CapabilityChangeKind.Slettes))
        {
            db.Capabilities.Remove(byCode[CapabilityRules.NormalizeCode(change.Code)]);
        }
    }

    private static List<string> WithoutTrailingEmpty(IReadOnlyList<string> fields)
    {
        var count = fields.Count;
        while (count > 0 && fields[count - 1].Trim().Length == 0)
        {
            count--;
        }

        return fields.Take(count).ToList();
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>Læser request-kroppen, men aldrig mere end <paramref name="maxBytes"/> (null = for stor).</summary>
    public static async Task<byte[]?> ReadBodyAsync(Stream body, long? contentLength, int maxBytes, CancellationToken ct)
    {
        if (contentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
