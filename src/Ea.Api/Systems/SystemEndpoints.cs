using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Ea.Api.Capabilities;
using Ea.Api.Common;
using Ea.Api.Data;
using Ea.Api.Integrations;
using Ea.Api.Persons;
using Ea.Api.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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
        group.MapGet("/export.csv", ExportSystems).Produces(StatusCodes.Status200OK, contentType: "text/csv");
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
        string? capabilityId,
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

        if (capabilityId == None)
        {
            query = query.Missing();
        }
        else if (Guid.TryParse(capabilityId, out var capability))
        {
            query = query.Where(s => s.CapabilityLinks.Any(l => l.CapabilityId == capability));
        }
        else if (capabilityId is not null)
        {
            return Problems.Validation("capabilityId", $"capabilityId skal være et id eller '{None}'.");
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

    /// <summary>Referenceliste over alle systemer (præcise navne til den, der udfylder integrations-CSV'en).</summary>
    private static async Task<FileContentHttpResult> ExportSystems(EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var systems = await db.Systems.AsNoTracking().Include(s => s.ParentSystem).ToListAsync(ct);
        var date = time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.File(SystemCsv.Write(systems), "text/csv; charset=utf-8", $"systemer-{date}.csv");
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

        return TypedResults.Ok(await ToDetail(system, user, auth, db, ct));
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

        var (errors, failure) = await WithCouplingLock(db, request.CapabilityIds is not null, async () =>
        {
            var errors = await Apply(request, system, moduleCount: 0, requireVersion: false, db, ct);
            if (errors is not null)
            {
                return (errors, null);
            }

            Touch(system, user, now);
            db.Systems.Add(system);
            return (null, await Save(db, system, ct));
        }, ct);

        if (errors is not null)
        {
            return Problems.Validation(errors);
        }

        if (failure is not null)
        {
            return failure;
        }

        var created = await LoadAggregate(db.Systems.AsNoTracking(), system.Id, ct);
        return TypedResults.Created($"/api/systems/{system.Id}", await ToDetail(created!, user, auth, db, ct));
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

        var (errors, failure) = await WithCouplingLock(db, request.CapabilityIds is not null, async () =>
        {
            // Koblingerne læses EFTER låsen: en import, der lige er gennemført, skal ses — ellers tilføjer formularen
            // en kobling, importen allerede har lavet (500), eller validerer mod en forældet liste.
            await db.Entry(system).Collection(s => s.CapabilityLinks).LoadAsync(ct);
            var errors = await Apply(request, system, moduleCount, requireVersion: true, db, ct);
            if (errors is not null)
            {
                return (errors, null);
            }

            // Samtidighedstjek: gem kun, hvis databasens version stadig er den, brugeren så. Rækken skrives ALTID
            // (IsModified), så tjekket også sker, når kun rollerne eller koblingerne ændres, og uret ikke har flyttet sig.
            db.Entry(system).Property(s => s.Version).OriginalValue = request.Version!.Value;
            Touch(system, user, time.GetUtcNow());
            db.Entry(system).Property(s => s.UpdatedAt).IsModified = true;
            return (null, await Save(db, system, ct));
        }, ct);

        if (errors is not null)
        {
            return Problems.Validation(errors);
        }

        if (failure is not null)
        {
            return failure;
        }

        var updated = await LoadAggregate(db.Systems.AsNoTracking(), id, ct);
        return TypedResults.Ok(await ToDetail(updated!, user, auth, db, ct));
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
        // Skrives altid — også i samme øjeblik som sidste bekræftelse — så versionen altid tjekkes.
        db.Entry(system).Property(s => s.LastConfirmedAt).IsModified = true;

        var failure = await Save(db, system, ct);
        if (failure is not null)
        {
            return failure;
        }

        var confirmed = await LoadAggregate(db.Systems.AsNoTracking(), id, ct);
        return TypedResults.Ok(await ToDetail(confirmed!, user, auth, db, ct));
    }

    /// <summary>
    /// Sletning fjerner systemets koblinger (cascade). Opslag og adgangstjek sker FØR låsen, så en afvisning er billig
    /// og en læser ikke kan stå i kø ved den. Koblingslåsen tages derefter FØR systemrækken — samme rækkefølge som
    /// formularen og importerne — så en samtidig koblingsimport ikke kan ende i en deadlock.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> DeleteSystem(
        Guid id, EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var found = await db.Systems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (found is null)
        {
            return TypedResults.NotFound();
        }

        if (!(await auth.AuthorizeAsync(user, found, Policies.EditSystem)).Succeeded)
        {
            return TypedResults.Forbid();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE ea.system_capabilities IN ROW EXCLUSIVE MODE", ct);

            var system = await db.Systems.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (system is null)
            {
                return (Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>)TypedResults.NotFound();
            }

            var blocked = SystemRules.DeleteBlockedReason(system.Name, await CountUsage(db, id, ct));
            if (blocked is not null)
            {
                return Problems.Blocked(blocked);
            }

            db.Systems.Remove(system);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Ændret af en anden (fx gemt i formularen) mens sletningen ventede: slet ikke noget, brugeren ikke så.
                return Problems.StaleVersion("Systemet");
            }

            await transaction.CommitAsync(ct);
            return TypedResults.NoContent();
        });
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

        var capabilityErrors = await ValidateCapabilities(request.CapabilityIds, system, db, ct);
        if (capabilityErrors is not null)
        {
            errors["capabilityIds"] = capabilityErrors;
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

        // null = uændret (se SystemWriteRequest.CapabilityIds). Diff, ikke Clear+Add.
        if (request.CapabilityIds is { } capabilityIds)
        {
            var wantedCapabilities = capabilityIds.ToHashSet();
            system.CapabilityLinks.RemoveAll(l => !wantedCapabilities.Contains(l.CapabilityId));
            foreach (var capabilityId in wantedCapabilities.Where(id => system.CapabilityLinks.All(l => l.CapabilityId != id)))
            {
                system.CapabilityLinks.Add(new SystemCapability { SystemId = system.Id, CapabilityId = capabilityId });
            }
        }

        return null;
    }

    /// <summary>
    /// Ændrer anmodningen koblinger, valideres og gemmes de under en lås, der udelukker en samtidig import af kortet
    /// (importen tager SHARE ROW EXCLUSIVE på koblingerne). Ellers kunne importen slette en kapabilitet eller give et
    /// blad børn mellem valideringen og gemningen: en 500 (fremmednøgle) eller en kobling til et ikke-blad.
    /// Låsene tages i samme rækkefølge som importens (kun koblingerne her), så de ikke kan låse hinanden fast.
    /// </summary>
    private static async Task<(Dictionary<string, string[]>? Errors, ProblemHttpResult? Failure)> WithCouplingLock(
        EaDbContext db,
        bool changesCouplings,
        Func<Task<(Dictionary<string, string[]>? Errors, ProblemHttpResult? Failure)>> work,
        CancellationToken ct)
    {
        if (!changesCouplings)
        {
            return await work();
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE ea.system_capabilities IN ROW EXCLUSIVE MODE", ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    /// <summary>
    /// Nye koblinger skal pege på et blad, der ikke er udgået (CapabilityRules.CoupleBlockedReason). Eksisterende
    /// koblinger bevares, selv om kapabiliteten siden er udgået eller har fået børn.
    /// </summary>
    private static async Task<string[]?> ValidateCapabilities(
        IReadOnlyList<Guid>? capabilityIds, SystemEntity system, EaDbContext db, CancellationToken ct)
    {
        if (capabilityIds is null)
        {
            return null;
        }

        // Grænsen tjekkes på den rå liste, før der arbejdes med den.
        if (capabilityIds.Count > CapabilityRules.MaxCouplingsPerSystem)
        {
            return [$"Højst {CapabilityRules.MaxCouplingsPerSystem} kapabiliteter pr. system."];
        }

        var wanted = capabilityIds.Distinct().ToList();

        var added = wanted.Where(id => system.CapabilityLinks.All(l => l.CapabilityId != id)).ToList();
        if (added.Count == 0)
        {
            return null;
        }

        var found = await db.Capabilities.AsNoTracking().Where(c => added.Contains(c.Id)).ToListAsync(ct);
        if (found.Count != added.Count)
        {
            return ["En eller flere af de valgte kapabiliteter findes ikke."];
        }

        var withChildren = await db.Capabilities.AsNoTracking()
            .Where(c => c.RetiredAt == null && c.ParentId != null && added.Contains(c.ParentId.Value))
            .Select(c => c.ParentId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var reasons = found
            .OrderBy(c => c.Code, CapabilityRules.CodeOrder)
            .Select(c => CapabilityRules.CoupleBlockedReason(c, withChildren.Contains(c.Id)))
            .OfType<string>()
            .ToArray();
        return reasons.Length > 0 ? reasons : null;
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
            return Problems.StaleVersion("Systemet");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, EaDbContext.SystemNameIndex))
        {
            if (system.ParentSystemId is null)
            {
                return Problems.Duplicate($"Der findes allerede et system med navnet \"{system.Name}\".");
            }

            var parentName = await db.Systems.AsNoTracking()
                .Where(s => s.Id == system.ParentSystemId)
                .Select(s => s.Name)
                .FirstAsync(ct);
            return Problems.Duplicate($"{parentName} har allerede et modul med navnet \"{system.Name}\".");
        }
    }

    /// <summary>
    /// Systemets egne koblinger plus familiens: på en forælder modulernes ("via modul"), på et modul forælderens.
    /// Egne først, derefter i kortets rækkefølge (udgåede sidst).
    /// </summary>
    private static async Task<List<SystemCapabilityDto>> CapabilitiesOf(SystemEntity s, EaDbContext db, CancellationToken ct)
    {
        var family = new Dictionary<Guid, string> { [s.Id] = s.Name };
        if (s.ParentSystem is { } parent)
        {
            family[parent.Id] = parent.Name;
        }

        foreach (var module in s.Modules)
        {
            family[module.Id] = module.Name;
        }

        var ids = family.Keys.ToList();
        var links = await db.SystemCapabilities.AsNoTracking().Where(l => ids.Contains(l.SystemId)).ToListAsync(ct);
        if (links.Count == 0)
        {
            return [];
        }

        var byId = await db.Capabilities.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var parents = CapabilityRules.WithChildren(byId.Values);
        var treeOrder = CapabilityRules.Ordered(byId.Values)
            .Select((n, index) => (n.Capability.Id, index))
            .ToDictionary(x => x.Id, x => x.index);
        var holders = await CapabilityQueries.OverlapHoldersAsync(db, links.Select(l => l.CapabilityId).Distinct().ToList(), ct);
        var ownGroup = SystemRules.OverlapGroupKey(s.Id, s.ParentSystemId);
        return links
            .Select(l => (Link: l, Capability: byId[l.CapabilityId]))
            .Select(x =>
            {
                // Ikke vurderet (udgået eller ikke et blad): intet overlap og ingen "deles med", før koblingen er flyttet.
                var overlap = CapabilityRules.OverlapOf(x.Capability, parents.Contains(x.Capability.Id), holders[x.Capability.Id]);
                return new SystemCapabilityDto(
                    new CapabilityRef(
                        x.Capability.Id,
                        x.Capability.Code,
                        x.Capability.Name,
                        CapabilityRules.DisplayPath(x.Capability, byId),
                        CapabilityRules.MoveReasonOf(x.Capability, parents.Contains(x.Capability.Id))),
                    x.Link.SystemId == s.Id ? null : new SystemRef(x.Link.SystemId, family[x.Link.SystemId]),
                    CapabilityQueries.ToDto(overlap),
                    overlap?.Members.First(m => m.Holder.SystemId == x.Link.SystemId).Exclusion,
                    overlap?.Members
                        .Where(m => m.Holder.GroupKey != ownGroup)
                        .Select(m => new OverlapMember(m.Holder.SystemId, m.Holder.Name, m.Exclusion))
                        .ToList() ?? []);
            })
            .OrderBy(c => c.HeldBy is not null)
            .ThenBy(c => treeOrder.GetValueOrDefault(c.Capability.Id, int.MaxValue)) // Kortets rækkefølge; udgåede sidst.
            .ThenBy(c => c.Capability.Code, CapabilityRules.CodeOrder)
            .ThenBy(c => c.HeldBy?.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Hvad afhænger af systemet — bruges af både sletning og permissions (én kilde).</summary>
    internal static async Task<SystemUsage> CountUsage(EaDbContext db, Guid id, CancellationToken ct) => new(
        await db.Systems.CountAsync(s => s.ParentSystemId == id, ct),
        await db.Integrations.CountAsync(i => i.SourceSystemId == id || i.TargetSystemId == id, ct),
        await db.Integrations.CountAsync(i => i.ViaPlatformId == id, ct));

    private static async Task<SystemDetail> ToDetail(
        SystemEntity s, ClaimsPrincipal user, IAuthorizationService auth, EaDbContext db, CancellationToken ct)
    {
        var canEdit = (await auth.AuthorizeAsync(user, s, Policies.EditSystem)).Succeeded;
        var deleteBlocked = SystemRules.DeleteBlockedReason(s.Name, await CountUsage(db, s.Id, ct));

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
            await CapabilitiesOf(s, db, ct),
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
