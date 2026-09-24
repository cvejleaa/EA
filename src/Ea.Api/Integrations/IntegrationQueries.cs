using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Integrations;

/// <summary>
/// Forespørgsler, som BÅDE systemsidens "hvad rammes" og CSV-eksporten bruger — så skærm og fil ikke kan
/// vise hver sin sandhed.
/// </summary>
public static class IntegrationQueries
{
    /// <summary>Integrationer med ender, forældre, platform og dataobjekter indlæst.</summary>
    public static IQueryable<Integration> WithDetails(EaDbContext db) =>
        db.Integrations.AsNoTracking()
            .Include(i => i.SourceSystem).ThenInclude(s => s.ParentSystem)
            .Include(i => i.TargetSystem).ThenInclude(s => s.ParentSystem)
            .Include(i => i.ViaPlatform).ThenInclude(s => s!.ParentSystem)
            .Include(i => i.DataObjects).ThenInclude(d => d.DataObject)
            .AsSplitQuery();

    /// <summary>Systemet selv plus dets moduler (ét niveau). Et modul har kun sig selv.</summary>
    public static async Task<HashSet<Guid>> FamilyOf(EaDbContext db, Guid systemId, CancellationToken ct)
    {
        var modules = await db.Systems.Where(s => s.ParentSystemId == systemId).Select(s => s.Id).ToListAsync(ct);
        return [systemId, .. modules];
    }

    /// <summary>Alle integrationer, hvor familien er kilde, mål eller platform.</summary>
    public static Task<List<Integration>> ForFamily(EaDbContext db, IReadOnlyCollection<Guid> family, CancellationToken ct) =>
        WithDetails(db)
            .Where(i => family.Contains(i.SourceSystemId)
                || family.Contains(i.TargetSystemId)
                || (i.ViaPlatformId != null && family.Contains(i.ViaPlatformId.Value)))
            .ToListAsync(ct);
}
