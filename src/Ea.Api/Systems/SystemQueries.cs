using System.Security.Claims;
using Ea.Api.Auth;

namespace Ea.Api.Systems;

public static class SystemQueries
{
    /// <summary>
    /// "Mine systemer": de systemer, hvor brugerens person har en rolle (alle roller — også forretningsejer), og deres
    /// moduler. En rolle på et modul tager ikke forælderen med. Det er en visning, ikke en adgang (se SystemAccess), og
    /// ÉN regel for både menuens tal og listen, så de altid stemmer. Uden en gyldig identitet: ingen systemer.
    /// </summary>
    public static IQueryable<SystemEntity> Mine(this IQueryable<SystemEntity> systems, ClaimsPrincipal user)
    {
        var oid = user.OidOrNull();
        return oid is null
            ? systems.Where(_ => false)
            : systems.Where(s => s.Roles.Any(r => r.Person.Oid == oid)
                || (s.ParentSystem != null && s.ParentSystem.Roles.Any(r => r.Person.Oid == oid)));
    }
}
