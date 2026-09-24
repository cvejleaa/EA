using System.Globalization;
using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Capabilities;

public static partial class CapabilityEndpoints
{
    public static void MapCapabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/capabilities").WithTags("Kapabiliteter");
        group.MapGet("/", GetTree);
        group.MapGet("/export.csv", Export).Produces(StatusCodes.Status200OK, contentType: "text/csv");
        group.MapGet("/couplings/export.csv", ExportCouplings).Produces(StatusCodes.Status200OK, contentType: "text/csv");

        // Filen sendes som selve request-kroppen (text/csv) — ingen formular, så ingen antiforgery-undtagelse.
        // Adgangstjekket ligger i policyen og kører FØR kroppen læses.
        group.MapPost("/import", Import)
            .RequireAuthorization(Policies.ManageCapabilities)
            .Accepts<string>("text/csv");
        group.MapPost("/couplings/import", ImportCouplings)
            .RequireAuthorization(Policies.ManageCapabilities)
            .Accepts<string>("text/csv");
    }

    private static async Task<Ok<CapabilityTreeResponse>> GetTree(
        EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var all = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);
        var parents = CapabilityRules.WithChildren(all);
        var holders = await CapabilityQueries.OverlapHoldersAsync(db, null, ct);
        var items = CapabilityRules.Ordered(all)
            .Select(n => new CapabilityNode(
                n.Capability.Id,
                n.Capability.Code,
                n.Capability.Name,
                n.Capability.Description,
                n.Capability.ParentId,
                n.Depth,
                CapabilityRules.PathOf(n.Capability, byId),
                CapabilityRules.CoupleBlockedReason(n.Capability, parents.Contains(n.Capability.Id)) is null,
                CapabilityQueries.ToDto(CapabilityRules.OverlapOf(
                    n.Capability, parents.Contains(n.Capability.Id), holders[n.Capability.Id]))))
            .ToList();

        var coupled = await CapabilityQueries.CoupledSystemsAsync(db, ct);
        var toMove = all
            .Select(c => (Capability: c, Reason: CapabilityRules.MoveReasonOf(c, parents.Contains(c.Id)),
                Systems: coupled.GetValueOrDefault(c.Id) ?? []))
            .Where(x => x.Reason is not null && x.Systems.Count > 0)
            .OrderBy(x => x.Reason)
            .ThenBy(x => x.Capability.Code, CapabilityRules.CodeOrder)
            .Select(x => new CapabilityToMove(x.Capability.Id, x.Capability.Code, x.Capability.Name,
                CapabilityRules.DisplayPath(x.Capability, byId), x.Reason!.Value, x.Systems))
            .ToList();

        var canImport = (await auth.AuthorizeAsync(user, Policies.ManageCapabilities)).Succeeded;
        var total = await db.Systems.InUse().CountAsync(ct);
        var coverage = new CouplingCoverage(total - await db.Systems.Missing().CountAsync(ct), total);
        return TypedResults.Ok(new CapabilityTreeResponse(items, toMove, canImport, coverage));
    }

    private static async Task<FileContentHttpResult> Export(EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var all = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var date = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return TypedResults.File(CapabilityCsv.Write(all), "text/csv; charset=utf-8", $"kapabiliteter-{date}.csv");
    }

    /// <summary>
    /// Koblings-CSV'en (docs/csv-koblinger.md): alle systemers egne koblinger med overlap-vurderingen og en række
    /// uden kode for hvert system, der mangler — dækningsreglen er den samme som systemlistens "Ikke angivet", men
    /// nedlagte systemer skal ikke kobles.
    /// </summary>
    private static async Task<FileContentHttpResult> ExportCouplings(EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var systems = await db.Systems
            .Include(s => s.ParentSystem)
            .Include(s => s.ManagingTeam)
            .Include(s => s.Roles).ThenInclude(r => r.Person)
            .Include(s => s.CapabilityLinks)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);
        var missing = (await db.Systems.Missing().Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        var capabilities = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var date = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return TypedResults.File(
            CouplingCsv.Write(systems, capabilities, missing), "text/csv; charset=utf-8", $"koblinger-{date}.csv");
    }

    /// <summary>
    /// Tør-kørsel (<c>dryRun=true</c>, standard): viser før/efter og gemmer intet. Gennemførelse
    /// (<c>dryRun=false</c>) kræver tør-kørslens fingeraftryk og gemmer alt i én transaktion — eller intet.
    /// </summary>
    private static async Task<Results<Ok<CapabilityImportResult>, ValidationProblem, ProblemHttpResult>> Import(
        HttpRequest request, bool? dryRun, string? fingerprint, EaDbContext db, TimeProvider time, ClaimsPrincipal user,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var commit = dryRun == false;
        if (commit && string.IsNullOrWhiteSpace(fingerprint))
        {
            return Problems.Validation("fingerprint", "Kør tør-kørslen først, og gennemfør derefter med dens fingeraftryk.");
        }

        var bytes = await CsvImport.ReadBodyAsync(request.Body, request.ContentLength, CapabilityRules.MaxFileBytes, ct);
        if (bytes is null)
        {
            return Problems.Validation("file", $"Filen er større end {CapabilityRules.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var (rows, errors) = CapabilityImport.Parse(Csv.Read(bytes));
        if (errors.Count > 0)
        {
            return commit
                ? Problems.Validation("file", $"Filen har {errors.Count} fejl. Kør tør-kørslen for at se dem.")
                : TypedResults.Ok(new CapabilityImportResult(false, errors, new CapabilityImportSummary(0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0), [], null));
        }

        if (!commit)
        {
            var preview = CapabilityImport.Plan(
                rows, await db.Capabilities.AsNoTracking().ToListAsync(ct), await CapabilityQueries.CoupledSystemsAsync(db, ct));
            return TypedResults.Ok(new CapabilityImportResult(false, [], preview.Summary, preview.Changes,
                CapabilityImport.Fingerprint(preview)));
        }

        // Lås kortet, så beregning, sammenligning og skrivning sker på samme tilstand — også ved to samtidige imports.
        // Koblingerne låses med: de afgør "slettes" eller "udgår", og en ny kobling må ikke ramme noget, der slettes.
        List<Capability> existing = [];
        var plan = await CsvImport.CommitIfUnchangedAsync(
            db,
            "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE",
            fingerprint!,
            async () =>
            {
                existing = await db.Capabilities.ToListAsync(ct);
                var current = CapabilityImport.Plan(rows, existing, await CapabilityQueries.CoupledSystemsAsync(db, ct));
                return (current, CapabilityImport.Fingerprint(current));
            },
            current =>
            {
                CapabilityImport.Apply(current, existing, db, time.GetUtcNow());
                return Task.CompletedTask;
            },
            ct);

        if (plan is null)
        {
            return Problems.StaleDryRun("Kortet eller koblingerne til det");
        }

        var logger = loggers.CreateLogger(typeof(CapabilityEndpoints));
        var oid = user.ObjectId();
        var summary = plan.Summary;
        LogImported(logger, oid, summary.New, summary.Changed, summary.Removed, summary.Retired, summary.Reactivated);
        return TypedResults.Ok(new CapabilityImportResult(true, [], plan.Summary, plan.Changes, null));
    }

    /// <summary>
    /// Import af koblings-CSV'en (docs/csv-koblinger.md). Samme protokol som kortet: tør-kørsel (standard) viser før og
    /// efter og gemmer intet; gennemførelse kræver tør-kørslens fingeraftryk.
    /// </summary>
    private static async Task<Results<Ok<CouplingImportResult>, ValidationProblem, ProblemHttpResult>> ImportCouplings(
        HttpRequest request, bool? dryRun, string? fingerprint, EaDbContext db, TimeProvider time, ClaimsPrincipal user,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var commit = dryRun == false;
        if (commit && string.IsNullOrWhiteSpace(fingerprint))
        {
            return Problems.Validation("fingerprint", "Kør tør-kørslen først, og gennemfør derefter med dens fingeraftryk.");
        }

        // Kestrels standardgrænse (30 MB) er under filens; ReadBodyAsync håndhæver vores egen.
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = CouplingImport.MaxFileBytes;
        }

        var bytes = await CsvImport.ReadBodyAsync(request.Body, request.ContentLength, CouplingImport.MaxFileBytes, ct);
        if (bytes is null)
        {
            return Problems.Validation("file", $"Filen er større end {CouplingImport.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var (rows, errors) = CouplingImport.Parse(Csv.Read(bytes, maxRecords: CouplingImport.MaxRows + 2));
        if (errors.Count > 0)
        {
            return commit
                ? Problems.Validation("file", $"Filen har {errors.Count} fejl. Kør tør-kørslen for at se dem.")
                : TypedResults.Ok(new CouplingImportResult(false, errors, [], EmptyCouplingSummary, [], [], null));
        }

        if (!commit)
        {
            var preview = await PlanCouplingsAsync(db, rows, track: false, ct);
            return TypedResults.Ok(new CouplingImportResult(false, preview.Errors, preview.Warnings,
                preview.Errors.Count > 0 ? EmptyCouplingSummary : preview.Summary,
                preview.Errors.Count > 0 ? [] : preview.Changes,
                preview.NotInFile,
                preview.Errors.Count > 0 ? null : CouplingImport.Fingerprint(preview)));
        }

        // Samme lås og rækkefølge som kortimporten: serialiserer mod den, mod formularens koblinger og mod sletning.
        // En fejl ved den nye beregning (fx et system slettet siden) betyder, at tør-kørslen ikke gælder længere.
        var plan = await CsvImport.CommitIfUnchangedAsync(
            db,
            "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE",
            fingerprint!,
            async () =>
            {
                var current = await PlanCouplingsAsync(db, rows, track: true, ct);
                return current.Errors.Count > 0 ? (null, null) : (current, CouplingImport.Fingerprint(current));
            },
            current =>
            {
                CouplingImport.Apply(current, db, time.GetUtcNow());
                return Task.CompletedTask;
            },
            ct);

        if (plan is null)
        {
            return Problems.StaleDryRun("Koblingerne eller systemerne i filen");
        }

        var logger = loggers.CreateLogger(typeof(CapabilityEndpoints));
        var oid = user.ObjectId();
        var summary = plan.Summary;
        LogCouplingsImported(logger, oid, summary.Added, summary.Removed, summary.SystemsChanged, summary.SystemsInFile);
        return TypedResults.Ok(new CouplingImportResult(true, [], plan.Warnings, summary, plan.Changes, plan.NotInFile, null));
    }

    private static readonly CouplingImportSummary EmptyCouplingSummary = new(0, 0, 0, 0, 0, 0, false, 0, 0, 0);

    /// <summary>Indlæser det, planen skal bruge: systemerne i filen, hele kortet og systemerne med koblinger uden for filen.</summary>
    private static async Task<CouplingImportPlan> PlanCouplingsAsync(
        EaDbContext db, IReadOnlyList<CouplingImportRow> rows, bool track, CancellationToken ct)
    {
        var ids = rows.Select(r => r.SystemId).Distinct().ToList();
        var query = db.Systems
            .Where(s => ids.Contains(s.Id))
            .Include(s => s.ParentSystem)
            .Include(s => s.ManagingTeam)
            .Include(s => s.Roles).ThenInclude(r => r.Person)
            .Include(s => s.CapabilityLinks)
            .AsSplitQuery();
        var systems = await (track ? query : query.AsNoTracking()).ToDictionaryAsync(s => s.Id, ct);
        var capabilities = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var notInFile = (await db.Systems.AsNoTracking()
                .Where(s => !ids.Contains(s.Id) && s.CapabilityLinks.Count > 0)
                .Select(s => new { s.Id, s.Name, Parent = s.ParentSystem == null ? null : s.ParentSystem.Name })
                .ToListAsync(ct))
            .Select(s => new CoupledSystem(s.Id, CapabilityQueries.DisplayName(s.Name, s.Parent)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
        return CouplingImport.Plan(rows, systems, capabilities, notInFile);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Koblingerne er importeret af {Oid}: {Added} tilføjet, {Removed} fjernet på {SystemsChanged} af filens {SystemsInFile} systemer.")]
    private static partial void LogCouplingsImported(ILogger logger, string oid, int added, int removed, int systemsChanged, int systemsInFile);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Kapabilitetskortet er importeret af {Oid}: {New} nye, {Changed} ændrede, {Removed} slettede, {Retired} udgået, {Reactivated} genaktiveret.")]
    private static partial void LogImported(ILogger logger, string oid, int @new, int changed, int removed, int retired, int reactivated);
}
