using System.Linq.Expressions;
using Ea.Api.Data;
using Ea.Api.Systems;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Capabilities;

public static class CapabilityQueries
{
    /// <summary>
    /// Dækning — den ENESTE definition (systemlistens "Ikke angivet" og koblings-CSV'ens tomme rækker bruger den):
    /// hverken systemet selv eller familien dækker noget. En forælder er dækket af sine modulers koblinger, et modul
    /// af forælderens ("hele systemet gør X") — men ikke af et søskendemoduls. Overlap samler anderledes, se
    /// <see cref="SystemRules.OverlapGroupKey"/>.
    /// </summary>
    public static readonly Expression<Func<SystemEntity, bool>> Uncovered = s =>
        s.CapabilityLinks.Count == 0 &&
        (s.ParentSystemId == null
            ? s.Modules.All(m => m.CapabilityLinks.Count == 0)
            : s.ParentSystem!.CapabilityLinks.Count == 0);

    /// <summary>Et system, som det vises på skærmen: "Forælder › Modul" for et modul.</summary>
    public static string DisplayName(string name, string? parent) => parent is null ? name : $"{parent} › {name}";

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
