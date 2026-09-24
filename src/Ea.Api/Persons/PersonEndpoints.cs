using Ea.Api.Authorization;
using Ea.Api.Common;
using Ea.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Persons;

public sealed record PersonDto(Guid Id, string DisplayName, string? Email, string? Department)
{
    public static PersonDto From(Person p) => new(p.Id, p.DisplayName, p.Email, p.Department);
}

public sealed record CreatePersonRequest(string? DisplayName, string? Email, string? Department);

/// <summary>
/// Personer. I produktion slås de op i Entra ID (kilden) i stedet for at blive oprettet her —
/// POST findes kun, fordi prototypen har fiktive personer.
/// </summary>
public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/persons").WithTags("Personer");

        group.MapGet("/", async (string? q, EaDbContext db, CancellationToken ct) =>
        {
            var query = db.Persons.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = SqlPatterns.Contains(q.Trim());
                query = query.Where(p =>
                    EF.Functions.ILike(p.DisplayName, pattern, SqlPatterns.Escape) ||
                    (p.Email != null && EF.Functions.ILike(p.Email, pattern, SqlPatterns.Escape)) ||
                    (p.Department != null && EF.Functions.ILike(p.Department, pattern, SqlPatterns.Escape)));
            }

            return await query.OrderBy(p => p.DisplayName)
                .Select(p => new PersonDto(p.Id, p.DisplayName, p.Email, p.Department))
                .ToListAsync(ct);
        });

        group.MapPost("/", CreatePerson).RequireAuthorization(Policies.ManagePersons);
    }

    private static async Task<Results<Created<PersonDto>, ValidationProblem>> CreatePerson(
        CreatePersonRequest request, EaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.DisplayName?.Trim() ?? "";
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim();

        if (name.Length == 0)
        {
            errors["displayName"] = ["Navn skal udfyldes."];
        }
        else if (name.Length > 200)
        {
            errors["displayName"] = ["Navn må højst være 200 tegn."];
        }

        if (email is not null && (email.Length > 320 || !email.Contains('@', StringComparison.Ordinal)))
        {
            errors["email"] = ["E-mail er ikke gyldig."];
        }

        if (department is { Length: > 200 })
        {
            errors["department"] = ["Afdeling må højst være 200 tegn."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var person = new Person
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            DisplayName = name,
            Email = email,
            Department = department,
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/persons/{person.Id}", PersonDto.From(person));
    }
}
