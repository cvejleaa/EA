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

    private static IReadOnlyList<string?> Row(
        SystemEntity system, string name, Capability? capability, string? path, OverlapAssessment? overlap, MoveReason? moveReason)
    {
        var own = overlap?.Members.First(m => m.Holder.SystemId == system.Id);
        var sharedWith = overlap?.Members
            .Where(m => m.Holder.GroupKey != own!.Holder.GroupKey)
            .Select(m => m.Exclusion is { } exclusion ? $"{m.Holder.Name} ({exclusion})" : m.Holder.Name);
        string? Holder(SystemRole role) => system.Roles.FirstOrDefault(r => r.Role == role)?.Person.DisplayName;

        return
        [
            system.Id.ToString(),
            name,
            capability?.Code,
            capability?.Name,
            path,
            system.ParentSystem?.Name,
            system.LifecycleStatus.ToString(),
            system.Type?.ToString(),
            system.ManagingTeam?.Name,
            Holder(SystemRole.Forretningsejer),
            Holder(SystemRole.Systemejer),
            system.LastConfirmedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            system.UpdatedAt.UtcDateTime.ToString(ChangedFormat, CultureInfo.InvariantCulture),
            overlap is null ? null : overlap.IsOverlap ? "Ja" : "Nej",
            overlap?.Counted.ToString(CultureInfo.InvariantCulture),
            overlap?.Planned.ToString(CultureInfo.InvariantCulture),
            own?.Exclusion?.ToString(),
            sharedWith is null ? null : string.Join(Joiner, sharedWith),
            moveReason?.ToString(),
            system.Description,
        ];
    }
}
