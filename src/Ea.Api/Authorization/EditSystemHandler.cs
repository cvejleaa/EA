using Ea.Api.Auth;
using Ea.Api.Systems;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.Authorization;

public sealed class EditSystemRequirement : IAuthorizationRequirement;

/// <summary>
/// Hvem må redigere/slette/bekræfte et bestemt system. Delopgave 4 udvider med: brugerens oid er
/// systemforvalter/-ejer på systemet eller dets forælder.
/// </summary>
public sealed class EditSystemHandler : AuthorizationHandler<EditSystemRequirement, SystemEntity>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EditSystemRequirement requirement, SystemEntity resource)
    {
        if (context.User.IsInRole(AppRoles.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
