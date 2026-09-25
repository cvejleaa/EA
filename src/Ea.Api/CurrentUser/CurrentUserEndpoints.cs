using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Ea.Api.Data;
using Ea.Api.Systems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.CurrentUser;

public sealed record MePermissions(bool CanCreateSystems, bool CanManagePersons, bool CanManageDataObjects);

/// <param name="PersonId">Personen i registret, brugerens oid er bundet til — null, hvis ingen.</param>
/// <param name="MySystemCount">Antal "Mine systemer" (samme regel som listens <c>mine=true</c>).</param>
public sealed record MeResponse(
    string Oid, string Name, IReadOnlyList<string> Roles, MePermissions Permissions, Guid? PersonId, int MySystemCount);

public static class CurrentUserEndpoints
{
    public static void MapMeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me", async (ClaimsPrincipal user, IAuthorizationService auth, EaDbContext db, CancellationToken ct) =>
        {
            var canCreate = await auth.AuthorizeAsync(user, Policies.CreateSystem);
            var canManagePersons = await auth.AuthorizeAsync(user, Policies.ManagePersons);
            var canManageDataObjects = await auth.AuthorizeAsync(user, Policies.ManageDataObjects);

            return new MeResponse(
                user.ObjectId(),
                user.DisplayName(),
                user.FindAll(ClaimNames.Roles).Select(c => c.Value).ToList(),
                new MePermissions(canCreate.Succeeded, canManagePersons.Succeeded, canManageDataObjects.Succeeded),
                user.OidOrNull() is { } oid
                    ? await db.Persons.Where(p => p.Oid == oid).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct)
                    : null,
                await db.Systems.Mine(user).CountAsync(ct));
        }).WithTags("Login");
    }
}
