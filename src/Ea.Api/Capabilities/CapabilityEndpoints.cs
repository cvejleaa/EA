using System.Globalization;
using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Microsoft.AspNetCore.Authorization;
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

        // Filen sendes som selve request-kroppen (text/csv) — ingen formular, så ingen antiforgery-undtagelse.
        // Adgangstjekket ligger i policyen og kører FØR kroppen læses.
        group.MapPost("/import", Import)
            .RequireAuthorization(Policies.ManageCapabilities)
            .Accepts<string>("text/csv");
    }

    private static async Task<Ok<CapabilityTreeResponse>> GetTree(
        EaDbContext db, ClaimsPrincipal user, IAuthorizationService auth, CancellationToken ct)
    {
        var all = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var byId = all.ToDictionary(c => c.Id);
        var parents = all.Where(c => c.RetiredAt is null && c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();
        var items = CapabilityRules.Ordered(all)
            .Select(n => new CapabilityNode(
                n.Capability.Id,
                n.Capability.Code,
                n.Capability.Name,
                n.Capability.Description,
                n.Capability.ParentId,
                n.Depth,
                CapabilityRules.PathOf(n.Capability, byId),
                CapabilityRules.CoupleBlockedReason(n.Capability, parents.Contains(n.Capability.Id)) is null))
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
        return TypedResults.Ok(new CapabilityTreeResponse(items, toMove, canImport));
    }

    private static async Task<FileContentHttpResult> Export(EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var all = await db.Capabilities.AsNoTracking().ToListAsync(ct);
        var date = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return TypedResults.File(CapabilityCsv.Write(all), "text/csv; charset=utf-8", $"kapabiliteter-{date}.csv");
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

        var bytes = await CapabilityImport.ReadBodyAsync(request.Body, request.ContentLength, CapabilityRules.MaxFileBytes, ct);
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
        // Transaktionen kører i EF's retry-strategi og starter forfra (med ny beregning) ved et forbindelsesbrud.
        var plan = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Koblingerne låses med: de afgør "slettes" eller "udgår", og en ny kobling må ikke ramme noget, der slettes.
            await db.Database.ExecuteSqlRawAsync(
                "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE", ct);

            var existing = await db.Capabilities.ToListAsync(ct);
            var current = CapabilityImport.Plan(rows, existing, await CapabilityQueries.CoupledSystemsAsync(db, ct));
            if (!string.Equals(CapabilityImport.Fingerprint(current), fingerprint, StringComparison.Ordinal))
            {
                return null;
            }

            CapabilityImport.Apply(current, existing, db, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return current;
        });

        if (plan is null)
        {
            return Problems.StaleDryRun();
        }

        var logger = loggers.CreateLogger(typeof(CapabilityEndpoints));
        var oid = user.ObjectId();
        var summary = plan.Summary;
        LogImported(logger, oid, summary.New, summary.Changed, summary.Removed, summary.Retired, summary.Reactivated);
        return TypedResults.Ok(new CapabilityImportResult(true, [], plan.Summary, plan.Changes, null));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Kapabilitetskortet er importeret af {Oid}: {New} nye, {Changed} ændrede, {Removed} slettede, {Retired} udgået, {Reactivated} genaktiveret.")]
    private static partial void LogImported(ILogger logger, string oid, int @new, int changed, int removed, int retired, int reactivated);
}
