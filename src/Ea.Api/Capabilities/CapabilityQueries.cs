using System.Linq.Expressions;
using Ea.Api.Data;
using Ea.Api.Systems;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Capabilities;

public static class CapabilityQueries
{
    /// <summary>
    /// Dækning: hverken systemet selv eller familien dækker noget. En forælder er dækket af sine modulers koblinger, et modul
    /// af forælderens ("hele systemet gør X") — men ikke af et søskendemoduls. Overlap samler anderledes, se
    /// <see cref="SystemRules.OverlapGroupKey"/>.
    /// </summary>
    /// <summary>
    /// Systemer og moduler, der mangler at blive koblet (beslutning K): ikke dækket (<see cref="Uncovered"/>) og ikke
    /// nedlagt. DEN definition bruger systemlistens "Ikke angivet", koblings-CSV'ens tomme rækker og dæknings-tallet —
    /// så de altid stemmer.
    /// </summary>
    public static IQueryable<SystemEntity> Missing(this IQueryable<SystemEntity> systems) =>
        systems.Where(Uncovered).Where(s => s.LifecycleStatus != LifecycleStatus.Nedlagt);

    /// <summary>Systemerne, dækningen regnes over: alle undtagen nedlagte.</summary>
    public static IQueryable<SystemEntity> InUse(this IQueryable<SystemEntity> systems) =>
        systems.Where(s => s.LifecycleStatus != LifecycleStatus.Nedlagt);

    private static readonly Expression<Func<SystemEntity, bool>> Uncovered = s =>
        s.CapabilityLinks.Count == 0 &&
        (s.ParentSystemId == null
            ? s.Modules.All(m => m.CapabilityLinks.Count == 0)
            : s.ParentSystem!.CapabilityLinks.Count == 0);

    /// <summary>Et system, som det vises på skærmen: "Forælder › Modul" for et modul.</summary>
    public static string DisplayName(string name, string? parent) => parent is null ? name : $"{parent} › {name}";

    /// <summary>
    /// Holderne til overlap-vurderingen (<see cref="CapabilityRules.OverlapOf"/>) pr. kapabilitet — alle, eller kun de
    /// givne kapabiliteters. Navnet er som på skærmen ("Forælder › Modul").
    /// </summary>
    public static async Task<ILookup<Guid, OverlapHolder>> OverlapHoldersAsync(
        EaDbContext db, IReadOnlyCollection<Guid>? capabilityIds, CancellationToken ct)
    {
        var links = db.SystemCapabilities.AsNoTracking();
        if (capabilityIds is not null)
        {
            links = links.Where(l => capabilityIds.Contains(l.CapabilityId));
        }

        var rows = await (
                from link in links
                join system in db.Systems on link.SystemId equals system.Id
                select new
                {
                    link.CapabilityId,
                    system.Id,
                    system.Name,
                    system.ParentSystemId,
                    Parent = system.ParentSystem == null ? null : system.ParentSystem.Name,
                    system.LifecycleStatus,
                    system.Type,
                    ParentStatus = system.ParentSystem == null ? (LifecycleStatus?)null : system.ParentSystem.LifecycleStatus,
                })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.CapabilityId, r => new OverlapHolder(
            r.Id, SystemRules.OverlapGroupKey(r.Id, r.ParentSystemId), DisplayName(r.Name, r.Parent),
            r.LifecycleStatus, r.Type, r.ParentStatus));
    }

    /// <summary>Vurderingen som den sendes til klienten.</summary>
    public static CapabilityOverlap? ToDto(OverlapAssessment? assessment) =>
        assessment is null
            ? null
            : new CapabilityOverlap(assessment.Counted, assessment.Planned, assessment.IsOverlap,
                assessment.PlannedOnTopOfActive,
                assessment.Members.Select(m => new OverlapMember(m.Holder.SystemId, m.Holder.Name, m.Exclusion)).ToList());

    /// <summary>Systemerne koblet til hver kapabilitet, med fuldt navn og i navneorden.</summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<CoupledSystem>>> CoupledSystemsAsync(EaDbContext db, CancellationToken ct)
    {
        var links = await (
                from link in db.SystemCapabilities
                join system in db.Systems on link.SystemId equals system.Id
                select new
                {
                    link.CapabilityId,
                    system.Id,
                    system.Name,
                    Parent = system.ParentSystem == null ? null : system.ParentSystem.Name,
                })
            .AsNoTracking()
            .ToListAsync(ct);

        return links
            .GroupBy(l => l.CapabilityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CoupledSystem>)g
                    .Select(l => new CoupledSystem(l.Id, DisplayName(l.Name, l.Parent)))
                    .OrderBy(s => s.Name, StringComparer.Ordinal)
                    .ThenBy(s => s.Id)
                    .ToList());
    }
}
