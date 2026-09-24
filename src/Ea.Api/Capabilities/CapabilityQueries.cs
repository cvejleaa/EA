using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Capabilities;

public static class CapabilityQueries
{
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
                    .Select(l => new CoupledSystem(l.Id, l.Parent is null ? l.Name : $"{l.Parent} › {l.Name}"))
                    .OrderBy(s => s.Name, StringComparer.Ordinal)
                    .ThenBy(s => s.Id)
                    .ToList());
    }
}
