using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Ea.Api.Persons;
using Ea.Api.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ea.Api.Systems;

public static class SystemEndpoints
{
    /// <summary>Filterværdi for "ikke angivet" (fx systemer uden team eller uden forretningsejer).</summary>
    public const string None = "none";

    public static void MapSystemEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/systems").WithTags("Systemer");

        group.MapGet("/", ListSystems);
        group.MapGet("/parent-candidates", ParentCandidates);
        group.MapGet("/{id:guid}", GetSystem);
        group.MapPost("/", CreateSystem).RequireAuthorization(Policies.CreateSystem);
        group.MapPut("/{id:guid}", UpdateSystem);
        group.MapPost("/{id:guid}/confirm", ConfirmSystem);
        group.MapDelete("/{id:guid}", DeleteSystem);
    }

    private static async Task<Results<Ok<SystemListResponse>, ValidationProblem>> ListSystems(
        string? q,
        LifecycleStatus? status,
        SystemType? type,
        string? teamId,
        string? businessOwnerId,
        EaDbContext db,
        CancellationToken ct)
    {
        var query = db.Systems.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(s => s.LifecycleStatus == status);
        }

        if (type is not null)
        {
            query = query.Where(s => s.Type == type);
        }

        if (teamId == None)
        {
            query = query.Where(s => s.ManagingTeamId == null);
        }
        else if (Guid.TryParse(teamId, out var team))
        {
            query = query.Where(s => s.ManagingTeamId == team);
        }
        else if (teamId is not null)
        {
            return Problems.Validation("teamId", $"teamId skal være et id eller '{None}'.");
        }

        if (businessOwnerId == None)
        {
            query = query.Where(s => !s.Roles.Any(r => r.Role == SystemRole.Forretningsejer));
        }
        else if (Guid.TryParse(businessOwnerId, out var owner))
        {
            query = query.Where(s => s.Roles.Any(r => r.Role == SystemRole.Forretningsejer && r.PersonId == owner));
        }
        else if (businessOwnerId is not null)
        {
            return Problems.Validation("businessOwnerId", $"businessOwnerId skal være et id eller '{None}'.");
        }

        var search = q?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = SqlPatterns.Contains(search);
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern, SqlPatterns.Escape) ||
                s.Aliases.Any(a => EF.Functions.ILike(a, pattern, SqlPatterns.Escape)) ||
                (s.Description != null && EF.Functions.ILike(s.Description, pattern, SqlPatterns.Escape)) ||
                // Et modul findes også via forælderens navn og aliaser (fx "Fusion" → alle Fusion-moduler).
                (s.ParentSystem != null && (EF.Functions.ILike(s.ParentSystem.Name, pattern, SqlPatterns.Escape) ||
                    s.ParentSystem.Aliases.Any(a => EF.Functions.ILike(a, pattern, SqlPatterns.Escape)))));
        }

        // Moduler står lige under deres forælder.
        var rows = await query
            .OrderBy(s => s.ParentSystem != null ? s.ParentSystem.NameNormalized : s.NameNormalized)
            .ThenBy(s => s.ParentSystemId != null)
            .ThenBy(s => s.NameNormalized)
            .Select(s => new
            {
                s.Id,
                s.Name,
                Parent = s.ParentSystem == null ? null : new SystemRef(s.ParentSystem.Id, s.ParentSystem.Name),
                s.Aliases,
                s.Type,
                s.LifecycleStatus,
                Team = s.ManagingTeam == null ? null : new TeamDto(s.ManagingTeam.Id, s.ManagingTeam.Name),
                Owner = s.Roles
                    .Where(r => r.Role == SystemRole.Forretningsejer)
                    .Select(r => new PersonDto(r.Person.Id, r.Person.DisplayName, r.Person.Email, r.Person.Department))
                    .FirstOrDefault(),
                s.LastConfirmedAt,
                ModuleCount = s.Modules.Count,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new SystemListItem(
                r.Id,
                r.Name,
                r.Parent,
                MatchedAlias(search, r.Name, r.Aliases),
                r.Type,
                r.LifecycleStatus,
                r.Team,
                r.Owner,
                r.LastConfirmedAt,
                r.ModuleCount))
            .ToList();

        var total = await db.Systems.CountAsync(ct);
        return TypedResults.Ok(new SystemListResponse(items, total));
    }

    /// <summary>Viser hvilket alias, der ramte søgningen, når navnet ikke gjorde — ellers ser rækken irrelevant ud.</summary>
    private static string? MatchedAlias(string? search, string name, List<string> aliases)
    {
        if (string.IsNullOrEmpty(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return aliases.FirstOrDefault(a => a.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gyldige forældre for et (nyt eller eksisterende) system — samme regel som ved gem.</summary>
    private static async Task<Ok<List<SystemRef>>> ParentCandidates(Guid? forSystemId, EaDbContext db, CancellationToken ct)
    {
        var moduleCount = forSystemId is null
            ? 0
            : await db.Systems.CountAsync(s => s.ParentSystemId == forSystemId, ct);

        var all = await db.Systems.AsNoTracking()
            .OrderBy(s => s.NameNormalized)
            .Select(s => new ParentInfo(s.Id, s.Name, s.ParentSystemId))
            .ToListAsync(ct);

        var candidates = all
            .Where(p => SystemRules.ValidateParent(forSystemId, moduleCount, p.Id, p) is null)
            .Select(p => new SystemRef(p.Id, p.Name))
            .ToList();

        return TypedResults.Ok(candidates);
    }

    private static async Task<Results<Ok<SystemDetail>, NotFound>> GetSystem(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var system = await LoadAggregate(db.Systems.AsNoTracking(), id, ct);
        if (system is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToDetail(system, user, auth));
    }

    private static async Task<Results<Created<SystemDetail>, ValidationProblem, ProblemHttpResult>> CreateSystem(
        SystemWriteRequest request,
        EaDbContext db,
        ClaimsPrincipal user,
        IAuthorizationService auth,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var system = new SystemEntity
        {
            Id = Guid.CreateVersion7(now),
            CreatedAt = now,
        };

        var errors = await Apply(request, system, moduleCount: 0, requireVersion: false, db, ct);
        if (errors is not null)
        {
            return Problems.Validation(errors);
        }

        Touch(system, user, now);
        db.Systems.Add(system);

        var failure = await Save(db, system, ct);
        if (failure is not null)
        {
            return failure;
        }

        var created = await LoadAggregate(db.Systems.AsNoTracking(), system.Id, ct);
        return TypedResults.Created($"/api/systems/{system.Id}", await ToDetail(created!, user, auth));
    }

    private static async Task<Results<Ok<SystemDetail>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>> UpdateSystem(
        Guid id,
        SystemWriteRequest request,
        EaDbContext db,
        ClaimsPrincipal user,
        IAuthorizationService auth,
        TimeProvider time,
        CancellationToken ct)
    {
        // Billigt opslag og adgangstjek FØR de dyre operationer (roller, moduler, validering).
        var system = await db.Systems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (system is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, system, Policies.EditSystem)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        await db.Entry(system).Collection(s => s.Roles).LoadAsync(ct);
        var moduleCount = await db.Systems.CountAsync(s => s.ParentSystemId == id, ct);

        var errors = await Apply(request, system, moduleCount, requireVersion: true, db, ct);
        if (errors is not null)
        {
            return Problems.Validation(errors);
        }

        // Samtidighedstjek: gem kun, hvis databasens version stadig er den, brugeren så.
        db.Entry(system).Property(s => s.Version).OriginalValue = request.Version!.Value;
        Touch(system, user, time.GetUtcNow());

        var failure = await Save(db, system, ct);
        if (failure is not null)
        {
            return failure;
        }

        var updated = await LoadAggregate(db.Systems.AsNoTracking(), id, ct);
        return TypedResults.Ok(await ToDetail(updated!, user, auth));
    }

    /// <summary>"Bekræft uændret": sætter kun bekræftelsen — data og UpdatedAt røres ikke.</summary>
    private static async Task<Results<Ok<SystemDetail>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>> ConfirmSystem(
        Guid id,
        ConfirmSystemRequest request,
        EaDbContext db,
        ClaimsPrincipal user,
        IAuthorizationService auth,
        TimeProvider time,
        CancellationToken ct)
    {
        var system = await db.Systems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (system is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, system, Policies.EditSystem)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        if (request.Version is null)
        {
            return Problems.Validation("version", "Version mangler.");
        }

        // Man kan kun bekræfte det, man har set: en forældet version afvises.
        db.Entry(system).Property(s => s.Version).OriginalValue = request.Version.Value;
        system.LastConfirmedAt = time.GetUtcNow();
        system.LastConfirmedByOid = user.ObjectId();
        system.LastConfirmedByName = user.DisplayName();

        var failure = await Save(db, system, ct);
        if (failure is not null)
        {
            return failure;
        }

        var confirmed = await LoadAggregate(db.Systems.AsNoTracking(), id, ct);
        return TypedResults.Ok(await ToDetail(confirmed!, user, auth));
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> DeleteSystem(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var system = await db.Systems.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (system is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, system, Policies.EditSystem)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        var moduleCount = await db.Systems.CountAsync(s => s.ParentSystemId == id, ct);
        var blocked = SystemRules.DeleteBlockedReason(system.Name, moduleCount);
        if (blocked is not null)
        {
            return Problems.Conflict(blocked);
        }

        db.Systems.Remove(system);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<SystemEntity?> LoadAggregate(IQueryable<SystemEntity> source, Guid id, CancellationToken ct) =>
        source
            .Include(s => s.ManagingTeam)
            .Include(s => s.ParentSystem)
            .Include(s => s.Modules)
            .Include(s => s.Roles).ThenInclude(r => r.Person)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>Validerer og overfører anmodningen til systemet. Returnerer feltfejl eller null.</summary>
    private static async Task<Dictionary<string, string[]>?> Apply(
        SystemWriteRequest request, SystemEntity system, int moduleCount, bool requireVersion, EaDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? "";
        var roles = request.Roles ?? [];

        if (name.Length == 0)
        {
            errors["name"] = ["Navn skal udfyldes."];
        }
        else if (name.Length > SystemRules.NameMaxLength)
        {
            errors["name"] = [$"Navn må højst være {SystemRules.NameMaxLength} tegn."];
        }

        if (request.LifecycleStatus is null)
        {
            errors["lifecycleStatus"] = ["Vælg en livscyklus-status."];
        }

        if (request.Description is { Length: > SystemRules.DescriptionMaxLength })
        {
            errors["description"] = [$"Beskrivelsen må højst være {SystemRules.DescriptionMaxLength} tegn."];
        }

        var aliases = SystemRules.NormalizeAliases(request.Aliases, name);
        if (aliases.Count > SystemRules.AliasMaxCount || aliases.Any(a => a.Length > SystemRules.NameMaxLength))
        {
            errors["aliases"] = [$"Højst {SystemRules.AliasMaxCount} aliaser på hver højst {SystemRules.NameMaxLength} tegn."];
        }

        if (requireVersion && request.Version is null)
        {
            errors["version"] = ["Version mangler."];
        }

        if (request.ManagingTeamId is { } teamId && !await db.Teams.AnyAsync(t => t.Id == teamId, ct))
        {
            errors["managingTeamId"] = ["Det valgte team findes ikke."];
        }

        ParentInfo? parent = null;
        if (request.ParentSystemId is { } parentId)
        {
            parent = await db.Systems.AsNoTracking()
                .Where(s => s.Id == parentId)
                .Select(s => new ParentInfo(s.Id, s.Name, s.ParentSystemId))
                .FirstOrDefaultAsync(ct);
        }

        var parentError = SystemRules.ValidateParent(
            system.Id == Guid.Empty ? null : system.Id, moduleCount, request.ParentSystemId, parent);
        if (parentError is not null)
        {
            errors["parentSystemId"] = [parentError];
        }

        var roleError = SystemRules.ValidateRoles(roles);
        if (roleError is null)
        {
            var personIds = roles.Select(r => r.PersonId).Distinct().ToList();
            var found = await db.Persons.CountAsync(p => personIds.Contains(p.Id), ct);
            if (found != personIds.Count)
            {
                roleError = "En eller flere af de valgte personer findes ikke.";
            }
        }

        if (roleError is not null)
        {
            errors["roles"] = [roleError];
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        system.Name = name;
        system.Aliases = aliases;
        system.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        system.Type = request.Type;
        system.LifecycleStatus = request.LifecycleStatus!.Value;
        system.ManagingTeamId = request.ManagingTeamId;
        system.ParentSystemId = request.ParentSystemId;

        // Rolle-diff (ikke Clear+Add): samme nøgle må ikke både slettes og tilføjes i én kontekst.
        var wanted = roles.Select(r => (r.Role, r.PersonId)).ToHashSet();
        system.Roles.RemoveAll(r => !wanted.Contains((r.Role, r.PersonId)));
        foreach (var (role, personId) in wanted)
        {
            if (!system.Roles.Any(r => r.Role == role && r.PersonId == personId))
            {
                system.Roles.Add(new SystemRoleAssignment { SystemId = system.Id, Role = role, PersonId = personId });
            }
        }

        return null;
    }

    /// <summary>En gemt ændring er også en bekræftelse af, at oplysningerne er rigtige.</summary>
    private static void Touch(SystemEntity system, ClaimsPrincipal user, DateTimeOffset now)
    {
        system.UpdatedAt = now;
        system.LastConfirmedAt = now;
        system.LastConfirmedByOid = user.ObjectId();
        system.LastConfirmedByName = user.DisplayName();
    }

    private static async Task<ProblemHttpResult?> Save(EaDbContext db, SystemEntity system, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.StaleVersion();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EaDbContext.SystemNameIndex,
        })
        {
            if (system.ParentSystemId is null)
            {
                return Problems.Conflict($"Der findes allerede et system med navnet \"{system.Name}\".");
            }

            var parentName = await db.Systems.AsNoTracking()
                .Where(s => s.Id == system.ParentSystemId)
                .Select(s => s.Name)
                .FirstAsync(ct);
            return Problems.Conflict($"{parentName} har allerede et modul med navnet \"{system.Name}\".");
        }
    }

    private static async Task<SystemDetail> ToDetail(SystemEntity s, ClaimsPrincipal user, IAuthorizationService auth)
    {
        var canEdit = (await auth.AuthorizeAsync(user, s, Policies.EditSystem)).Succeeded;
        var deleteBlocked = SystemRules.DeleteBlockedReason(s.Name, s.Modules.Count);

        return new SystemDetail(
            s.Id,
            s.Name,
            s.Aliases,
            s.Description,
            s.Type,
            s.LifecycleStatus,
            s.ManagingTeam is null ? null : new TeamDto(s.ManagingTeam.Id, s.ManagingTeam.Name),
            s.ParentSystem is null ? null : new SystemRef(s.ParentSystem.Id, s.ParentSystem.Name),
            s.Modules.OrderBy(m => m.NameNormalized, StringComparer.Ordinal)
                .Select(m => new ModuleDto(m.Id, m.Name, m.LifecycleStatus)).ToList(),
            s.Roles.OrderBy(r => r.Role).ThenBy(r => r.Person.DisplayName, StringComparer.CurrentCulture)
                .Select(r => new RoleAssignmentDto(r.Role, PersonDto.From(r.Person))).ToList(),
            s.CreatedAt,
            s.UpdatedAt,
            s.LastConfirmedAt,
            s.LastConfirmedByName,
            s.Version,
            new SystemPermissions(
                canEdit,
                canEdit && deleteBlocked is null,
                deleteBlocked,
                SystemRules.ParentChangeBlockedReason(s.Modules.Count)));
    }
}
