using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Integrations;

/// <summary>Dataobjekter: læses af alle, oprettes af dem med ManageDataObjects (samme mønster som personer).</summary>
public static class DataObjectEndpoints
{
    public static void MapDataObjectEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/data-objects").WithTags("Dataobjekter");

        group.MapGet("/", async (string? q, EaDbContext db, CancellationToken ct) =>
        {
            var query = db.DataObjects.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = SqlPatterns.Contains(q.Trim());
                query = query.Where(d => EF.Functions.ILike(d.Name, pattern, SqlPatterns.Escape));
            }

            return await query.OrderBy(d => d.NameNormalized).Select(d => new DataObjectDto(d.Id, d.Name)).ToListAsync(ct);
        });

        group.MapPost("/", CreateDataObject).RequireAuthorization(Policies.ManageDataObjects);
    }

    private static async Task<Results<Created<DataObjectDto>, ValidationProblem, ProblemHttpResult>> CreateDataObject(
        CreateDataObjectRequest request, EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var error = IntegrationRules.ValidateDataObjectName(request.Name);
        if (error is not null)
        {
            return Problems.Validation("name", error);
        }

        var dataObject = new DataObject { Id = Guid.CreateVersion7(time.GetUtcNow()), Name = request.Name! };
        db.DataObjects.Add(dataObject);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, EaDbContext.DataObjectNameIndex))
        {
            return Problems.Duplicate($"Dataobjektet \"{dataObject.Name}\" findes allerede — vælg det i stedet.");
        }

        return TypedResults.Created($"/api/data-objects/{dataObject.Id}", new DataObjectDto(dataObject.Id, dataObject.Name));
    }
}
