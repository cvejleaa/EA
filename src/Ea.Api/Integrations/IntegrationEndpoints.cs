using System.Security.Claims;
using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Ea.Api.Systems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Integrations;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/systems/{id:guid}/integrations", ForSystem).WithTags("Integrationer");

        var group = app.MapGroup("/api/integrations").WithTags("Integrationer");
        group.MapGet("/export.csv", Export).Produces(StatusCodes.Status200OK, contentType: "text/csv");
        group.MapGet("/{id:guid}", GetIntegration);
        group.MapPost("/", CreateIntegration);
        group.MapPut("/{id:guid}", UpdateIntegration);
        group.MapDelete("/{id:guid}", DeleteIntegration);
    }

    /// <summary>"Hvad hænger på systemet": direkte integrationer (ikke videre led), modulers rullet op.</summary>
    private static async Task<Results<Ok<SystemIntegrationsResponse>, NotFound>> ForSystem(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        if (!await db.Systems.AnyAsync(s => s.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var family = await IntegrationQueries.FamilyOf(db, id, ct);
        var integrations = await IntegrationQueries.ForFamily(db, family, ct);

        var items = new List<SystemIntegrationItem>();
        foreach (var integration in integrations)
        {
            var relation = IntegrationRules.RelationTo(family, integration.SourceSystemId, integration.TargetSystemId, integration.ViaPlatformId);
            if (relation is null)
            {
                continue;
            }

            var (counterpart, local) = relation switch
            {
                IntegrationRelation.Ud => (integration.TargetSystem, integration.SourceSystem),
                IntegrationRelation.Ind => (integration.SourceSystem, integration.TargetSystem),
                _ => ((SystemEntity?)null, (SystemEntity?)null),
            };

            items.Add(new SystemIntegrationItem(
                relation.Value,
                counterpart is null ? null : SystemLink.From(counterpart),
                local is null || local.Id == id ? null : new SystemRef(local.Id, local.Name),
                await ToDto(integration, user, auth)));
        }

        // Det, der rammes, først: modtagere og det, der går via platformen; derefter leverandører og interne.
        var ordered = items
            .OrderBy(i => i.Relation)
            .ThenBy(i => DisplayName(i.Counterpart ?? i.Integration.From), StringComparer.CurrentCulture)
            .ThenBy(i => i.Integration.Type?.ToString() ?? "", StringComparer.Ordinal)
            .ToList();

        var canAdd = (await auth.AuthorizeAsync(user,
            new Integration { SourceSystemId = id, TargetSystemId = id }, Policies.EditIntegration)).Succeeded;

        return TypedResults.Ok(new SystemIntegrationsResponse(Summarize(items), ordered, canAdd));
    }

    internal static IntegrationSummary Summarize(IReadOnlyCollection<SystemIntegrationItem> items)
    {
        var sending = items.Where(i => i.Relation == IntegrationRelation.Ud).Select(i => i.Counterpart!).ToList();
        var receiving = items.Where(i => i.Relation == IntegrationRelation.Ind).Select(i => i.Counterpart!).ToList();

        return new IntegrationSummary(
            sending.Select(c => c.Id).Distinct().Count(),
            receiving.Select(c => c.Id).Distinct().Count(),
            items.Count(i => i.Relation == IntegrationRelation.Via),
            sending.Concat(receiving).Where(c => c.Type == SystemType.LokalLoesning).Select(c => c.Id).Distinct().Count(),
            items.Count(i => i.Relation != IntegrationRelation.Via && i.Integration.Type == IntegrationType.DirekteDb));
    }

    private static string DisplayName(SystemLink s) => s.Parent is null ? s.Name : $"{s.Parent.Name} › {s.Name}";

    private static async Task<Results<Ok<IntegrationDto>, NotFound>> GetIntegration(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var integration = await IntegrationQueries.WithDetails(db).FirstOrDefaultAsync(i => i.Id == id, ct);
        return integration is null ? TypedResults.NotFound() : TypedResults.Ok(await ToDto(integration, user, auth));
    }

    private static async Task<Results<Created<IntegrationDto>, ValidationProblem, ForbidHttpResult, ProblemHttpResult>> CreateIntegration(
        IntegrationCreateRequest request,
        EaDbContext db,
        ClaimsPrincipal user,
        IAuthorizationService auth,
        TimeProvider time,
        CancellationToken ct)
    {
        // 1) Billige tjek uden database, 2) adgang, 3) validering mod databasen, 4) gem.
        if (request.FromSystemId is null || request.ToSystemId is null)
        {
            return Problems.Validation(IntegrationRules.Validate(
                request.FromSystemId, null, request.ToSystemId, null, null, null, false, null, null, 0));
        }

        var now = time.GetUtcNow();
        var integration = new Integration
        {
            Id = Guid.CreateVersion7(now),
            SourceSystemId = request.FromSystemId.Value,
            TargetSystemId = request.ToSystemId.Value,
            ViaPlatformId = request.ViaPlatformId,
            CreatedAt = now,
        };

        if (!(await auth.AuthorizeAsync(user, integration, Policies.EditIntegration)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        var infos = await LoadInfos(db, [integration.SourceSystemId, integration.TargetSystemId, request.ViaPlatformId], ct);
        var dataObjectIds = request.DataObjectIds?.Distinct().ToList() ?? [];
        var errors = IntegrationRules.Validate(
            request.FromSystemId, infos.GetValueOrDefault(integration.SourceSystemId),
            request.ToSystemId, infos.GetValueOrDefault(integration.TargetSystemId),
            request.ViaPlatformId, request.ViaPlatformId is { } v ? infos.GetValueOrDefault(v) : null,
            viaChanged: true, request.Name, request.Description, dataObjectIds.Count);
        await ValidateDataObjects(db, dataObjectIds, errors, ct);
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        integration.Type = request.Type;
        integration.Name = request.Name;
        integration.Description = Clean(request.Description);
        integration.UpdatedAt = now;
        integration.DataObjects = dataObjectIds
            .Select(d => new IntegrationDataObject { IntegrationId = integration.Id, DataObjectId = d })
            .ToList();
        db.Integrations.Add(integration);

        var failure = await Save(db, integration, infos, ct);
        if (failure is not null)
        {
            return failure;
        }

        var created = await IntegrationQueries.WithDetails(db).FirstAsync(i => i.Id == integration.Id, ct);
        return TypedResults.Created($"/api/integrations/{integration.Id}", await ToDto(created, user, auth));
    }

    private static async Task<Results<Ok<IntegrationDto>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>> UpdateIntegration(
        Guid id,
        IntegrationUpdateRequest request,
        EaDbContext db,
        ClaimsPrincipal user,
        IAuthorizationService auth,
        TimeProvider time,
        CancellationToken ct)
    {
        var integration = await db.Integrations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (integration is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, integration, Policies.EditIntegration)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        var infos = await LoadInfos(db, [integration.SourceSystemId, integration.TargetSystemId, request.ViaPlatformId], ct);
        var dataObjectIds = request.DataObjectIds?.Distinct().ToList() ?? [];
        var errors = IntegrationRules.Validate(
            integration.SourceSystemId, infos.GetValueOrDefault(integration.SourceSystemId),
            integration.TargetSystemId, infos.GetValueOrDefault(integration.TargetSystemId),
            request.ViaPlatformId, request.ViaPlatformId is { } v ? infos.GetValueOrDefault(v) : null,
            viaChanged: request.ViaPlatformId != integration.ViaPlatformId,
            request.Name, request.Description, dataObjectIds.Count);
        await ValidateDataObjects(db, dataObjectIds, errors, ct);
        if (request.Version is null)
        {
            errors["version"] = ["Version mangler."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        await db.Entry(integration).Collection(i => i.DataObjects).LoadAsync(ct);
        integration.ViaPlatformId = request.ViaPlatformId;
        integration.Type = request.Type;
        integration.Name = request.Name;
        integration.Description = Clean(request.Description);
        integration.DataObjects.RemoveAll(d => !dataObjectIds.Contains(d.DataObjectId));
        foreach (var dataObjectId in dataObjectIds.Where(d => integration.DataObjects.All(x => x.DataObjectId != d)))
        {
            integration.DataObjects.Add(new IntegrationDataObject { IntegrationId = id, DataObjectId = dataObjectId });
        }

        // UpdatedAt ændres ved HVER skrivning, så rækken altid opdateres og versionen altid tjekkes —
        // også når kun dataobjekterne (en anden tabel) ændres.
        integration.UpdatedAt = time.GetUtcNow();
        db.Entry(integration).Property(i => i.Version).OriginalValue = request.Version!.Value;

        var failure = await Save(db, integration, infos, ct);
        if (failure is not null)
        {
            return failure;
        }

        var updated = await IntegrationQueries.WithDetails(db).FirstAsync(i => i.Id == id, ct);
        return TypedResults.Ok(await ToDto(updated, user, auth));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteIntegration(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var integration = await db.Integrations.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (integration is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, integration, Policies.EditIntegration)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        db.Integrations.Remove(integration);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>CSV i importformatet. Med systemId: systemets integrationer inkl. modulers (samme udvalg som skærmen).</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> Export(
        Guid? systemId, EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        List<Integration> integrations;
        string scope;
        if (systemId is { } id)
        {
            var name = await db.Systems.Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync(ct);
            if (name is null)
            {
                return TypedResults.NotFound();
            }

            integrations = await IntegrationQueries.ForFamily(db, await IntegrationQueries.FamilyOf(db, id, ct), ct);
            scope = Csv.Slug(name);
        }
        else
        {
            integrations = await IntegrationQueries.WithDetails(db).ToListAsync(ct);
            scope = "alle";
        }

        var date = time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.File(IntegrationCsv.Write(integrations), "text/csv; charset=utf-8", $"integrationer-{scope}-{date}.csv");
    }

    private static async Task<Dictionary<Guid, SystemInfo>> LoadInfos(EaDbContext db, Guid?[] ids, CancellationToken ct)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        var rows = await db.Systems.AsNoTracking()
            .Where(s => wanted.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, Parent = s.ParentSystem != null ? s.ParentSystem.Name : null, s.Type })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new SystemInfo(r.Id, r.Parent is null ? r.Name : $"{r.Parent} › {r.Name}", r.Type));
    }

    private static async Task ValidateDataObjects(EaDbContext db, List<Guid> ids, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (ids.Count > 0 && !errors.ContainsKey("dataObjectIds")
            && await db.DataObjects.CountAsync(d => ids.Contains(d.Id), ct) != ids.Count)
        {
            errors["dataObjectIds"] = ["Et eller flere af de valgte dataobjekter findes ikke."];
        }
    }

    private static async Task<ProblemHttpResult?> Save(
        EaDbContext db, Integration integration, Dictionary<Guid, SystemInfo> infos, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.StaleVersion("Integrationen");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, EaDbContext.IntegrationKeyIndex))
        {
            return Problems.Duplicate(IntegrationRules.DuplicateMessage(
                infos[integration.SourceSystemId].Name,
                infos[integration.TargetSystemId].Name,
                integration.Type,
                integration.ViaPlatformId is { } via ? infos[via].Name : null,
                integration.Name));
        }
    }

    private static async Task<IntegrationDto> ToDto(Integration i, ClaimsPrincipal user, IAuthorizationService auth)
    {
        var canEdit = (await auth.AuthorizeAsync(user, i, Policies.EditIntegration)).Succeeded;
        return new IntegrationDto(
            i.Id,
            i.Name,
            SystemLink.From(i.SourceSystem),
            SystemLink.From(i.TargetSystem),
            i.ViaPlatform is null ? null : SystemLink.From(i.ViaPlatform),
            i.Type,
            i.Description,
            i.DataObjects.Select(d => new DataObjectDto(d.DataObject.Id, d.DataObject.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCulture).ToList(),
            i.CreatedAt,
            i.UpdatedAt,
            i.Version,
            new IntegrationPermissions(canEdit));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
