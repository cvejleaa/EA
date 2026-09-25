using Ea.Api.Systems;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.Authorization;

public sealed class EditSystemRequirement : IAuthorizationRequirement;

/// <summary>
/// Hvem må redigere og bekræfte et bestemt system: enterprise arkitekten, eller den hvis person har en redigerende rolle
/// på systemet eller dets forælder (se <see cref="SystemAccess"/>). Sletning har sin egen policy (kun admin).
/// </summary>
public sealed class EditSystemHandler(SystemAccess access) : AuthorizationHandler<EditSystemRequirement, SystemEntity>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EditSystemRequirement requirement, SystemEntity resource)
    {
        if (await access.CanEditAsync(context.User, resource.Id))
        {
            context.Succeed(requirement);
        }
    }
}
