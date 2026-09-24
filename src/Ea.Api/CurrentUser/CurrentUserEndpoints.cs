using System.Security.Claims;
using Ea.Api.Auth;
using Ea.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.CurrentUser;

public sealed record MePermissions(bool CanCreateSystems, bool CanManagePersons);

public sealed record MeResponse(string Oid, string Name, IReadOnlyList<string> Roles, MePermissions Permissions);

public static class CurrentUserEndpoints
{
    public static void MapMeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/me", async (ClaimsPrincipal user, IAuthorizationService auth) =>
        {
            var canCreate = await auth.AuthorizeAsync(user, Policies.CreateSystem);
            var canManagePersons = await auth.AuthorizeAsync(user, Policies.ManagePersons);

            return new MeResponse(
                user.ObjectId(),
                user.DisplayName(),
                user.FindAll(ClaimNames.Roles).Select(c => c.Value).ToList(),
                new MePermissions(canCreate.Succeeded, canManagePersons.Succeeded));
        }).WithTags("Login");
    }
}
