using Ea.Api.Systems;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.Authorization;

public sealed class MoveSystemRequirement : IAuthorizationRequirement;

/// <summary>Et skift af forælder: systemet, forælderen det har nu, og den ønskede (null = selvstændigt system).</summary>
public sealed record ParentChange(SystemEntity System, Guid? From, Guid? To);

/// <summary>
/// Et moduls koblinger og integrationer følger med, når det flytter, og forælderens dækning og overlap ændres. Derfor
/// kræver et skift ret over systemet, over den forælder det forlader, og over den forælder det flytter til.
/// </summary>
public sealed class MoveSystemHandler(SystemAccess access) : AuthorizationHandler<MoveSystemRequirement, ParentChange>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, MoveSystemRequirement requirement, ParentChange resource)
    {
        var user = context.User;
        if (!await access.CanEditAsync(user, resource.System.Id))
        {
            return;
        }

        if (resource.From == resource.To)
        {
            context.Succeed(requirement);
            return;
        }

        if (resource.From is { } from && !await access.CanEditAsync(user, from))
        {
            return;
        }

        if (resource.To is { } to && !await access.CanEditAsync(user, to))
        {
            return;
        }

        context.Succeed(requirement);
    }
}
