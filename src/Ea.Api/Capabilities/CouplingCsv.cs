using System.Globalization;
using Ea.Api.Common;
using Ea.Api.Integrations;
using Ea.Api.Systems;

namespace Ea.Api.Capabilities;

/// <summary>
/// Koblings-CSV'en: EA's arbejdsliste til at koble systemerne og til overlap-samtalerne — og skabelonen til
/// importen (3d). EKSTERN kontrakt: kolonner og rækkefølge ændres ikke. Spejles af docs/csv-koblinger.md og
/// docs/csv/koblinger-eksempel.csv. Nøglerne er <c>SystemId</c> og <c>Kode</c>; resten er beregnet.
/// </summary>
public static class CouplingCsv
{
    public static readonly IReadOnlyList<string> Header =
    [
        "SystemId", "FuldtNavn", "Kode", "Kapabilitet", "Sti", "Forælder", "Status", "Type", "ForvaltendeTeam",
        "Forretningsejer", "Systemejer", "SidstBekræftet", "SidstÆndret", "Overlap", "SystemerDerTæller",
        "AntalPlanlagte", "TællerIkkeMed", "DelesMed", "BørFlyttes", "Systembeskrivelse",
    ];

    /// <summary>
    /// Kolonnerne om systemet selv, som importen IKKE indlæser (de rettes på systemsiden). Retter nogen dem i filen,
    /// advarer tør-kørslen — de beregnes af <see cref="SystemValues"/>, samme funktion som eksporten bruger.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemColumns =
        ["Forælder", "Status", "Type", "ForvaltendeTeam", "Forretningsejer", "Systemejer", "SidstBekræftet", "Systembeskrivelse"];

    /// <summary>Adskiller systemerne i <c>DelesMed</c> (samme tegn som dataobjekter i integrations-CSV'en).</summary>
    public const string Joiner = IntegrationCsv.DataObjectJoiner;

    /// <summary>
    /// Fuld præcision, så importen kan se, om systemet er ændret efter eksporten. <c>T</c> og <c>Z</c> holder Excel
    /// fra at gøre feltet til en dato.
    /// </summary>
    public const string ChangedFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    /// <summary>
    /// Én række pr. kobling (systemets egne — et moduls ligger på modulet) og én række uden kode pr. system i
    /// <paramref name="missing"/>. Sorteret efter fuldt navn, derefter kode.
    /// </summary>
    /// <param name="systems">Alle systemer med forælder, team, roller (med person) og koblinger indlæst.</param>
    /// <param name="capabilities">Hele kortet, også udgåede.</param>
    /// <param name="missing">Systemer uden dækning (<see cref="CapabilityQueries.Uncovered"/>), som skal kobles.</param>
    public static byte[] Write(
        IReadOnlyCollection<SystemEntity> systems, IReadOnlyCollection<Capability> capabilities, IReadOnlySet<Guid> missing)
    {
        var byId = capabilities.ToDictionary(c => c.Id);
        var withChildren = CapabilityRules.WithChildren(capabilities);
        var holders = systems
            .SelectMany(s => s.CapabilityLinks.Select(l => (l.CapabilityId, Holder: new OverlapHolder(
                s.Id, SystemRules.OverlapGroupKey(s.Id, s.ParentSystemId), IntegrationCsv.FullName(s), s.LifecycleStatus,
                s.Type, s.ParentSystem?.LifecycleStatus))))
            .ToLookup(x => x.CapabilityId, x => x.Holder);
        var overlap = byId.Values.ToDictionary(
            c => c.Id, c => CapabilityRules.OverlapOf(c, withChildren.Contains(c.Id), holders[c.Id]));

        var rows = new List<(string Name, string Code, IReadOnlyList<string?> Row)>();
        foreach (var system in systems)
        {
            var name = IntegrationCsv.FullName(system);
            foreach (var link in system.CapabilityLinks)
            {
                var capability = byId[link.CapabilityId];
                rows.Add((name, capability.Code, Row(system, name, capability,
                    CapabilityRules.DisplayPath(capability, byId),
                    overlap[capability.Id],
                    CapabilityRules.MoveReasonOf(capability, withChildren.Contains(capability.Id)))));
            }

            if (system.CapabilityLinks.Count == 0 && missing.Contains(system.Id))
            {
                rows.Add((name, "", Row(system, name, null, null, null, null)));
            }
        }

        return Csv.Write(Header, rows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Code, CapabilityRules.CodeOrder)
            .Select(r => r.Row));
    }

    /// <summary>Værdierne i <see cref="SystemColumns"/>, som eksporten skriver dem for systemet i dag.</summary>
    public static IReadOnlyDictionary<string, string?> SystemValues(SystemEntity system)
    {
        string? Holder(SystemRole role) => system.Roles.FirstOrDefault(r => r.Role == role)?.Person.DisplayName;
        return new Dictionary<string, string?>
        {
            ["Forælder"] = system.ParentSystem?.Name,
            ["Status"] = system.LifecycleStatus.ToString(),
            ["Type"] = system.Type?.ToString(),
            ["ForvaltendeTeam"] = system.ManagingTeam?.Name,
            ["Forretningsejer"] = Holder(SystemRole.Forretningsejer),
            ["Systemejer"] = Holder(SystemRole.Systemejer),
            ["SidstBekræftet"] = system.LastConfirmedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Systembeskrivelse"] = system.Description,
        };
    }

    /// <summary>Tidspunktet i <c>SidstÆndret</c>: importen sammenligner det med systemets, som det er nu.</summary>
    public static string ChangedToken(SystemEntity system) =>
        system.UpdatedAt.UtcDateTime.ToString(ChangedFormat, CultureInfo.InvariantCulture);

    private static List<string?> Row(
        SystemEntity system, string name, Capability? capability, string? path, OverlapAssessment? overlap, MoveReason? moveReason)
    {
        var own = overlap?.Members.First(m => m.Holder.SystemId == system.Id);
        var sharedWith = overlap?.Members
            .Where(m => m.Holder.GroupKey != own!.Holder.GroupKey)
            .Select(m => m.Exclusion is { } exclusion ? $"{m.Holder.Name} ({exclusion})" : m.Holder.Name);

        var values = new Dictionary<string, string?>(SystemValues(system))
        {
            ["SystemId"] = system.Id.ToString(),
            ["FuldtNavn"] = name,
            ["Kode"] = capability?.Code,
            ["Kapabilitet"] = capability?.Name,
            ["Sti"] = path,
            ["SidstÆndret"] = ChangedToken(system),
            ["Overlap"] = overlap is null ? null : overlap.IsOverlap ? "Ja" : "Nej",
            ["SystemerDerTæller"] = overlap?.Counted.ToString(CultureInfo.InvariantCulture),
            ["AntalPlanlagte"] = overlap?.Planned.ToString(CultureInfo.InvariantCulture),
            ["TællerIkkeMed"] = own?.Exclusion?.ToString(),
            ["DelesMed"] = sharedWith is null ? null : string.Join(Joiner, sharedWith),
            ["BørFlyttes"] = moveReason?.ToString(),
        };
        return Header.Select(column => values[column]).ToList();
    }
}
