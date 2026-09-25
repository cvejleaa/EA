using Ea.Api.Integrations;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.Authorization;

public sealed class EditIntegrationRequirement : IAuthorizationRequirement;

/// <summary>
/// Hvem må oprette, redigere og slette en bestemt integration. Resursen er selve integrationen (med kilde og mål
/// udfyldt) — også ved oprettelse, hvor den endnu ikke er gemt. Reglen: brugeren må redigere en af ENDERNE (samme
/// regel som <see cref="EditSystemHandler"/>). Platformens forvalter får ikke ret via platformen (beslutning 4 i
/// delopgave 4), og enderne kan ikke ændres efter oprettelse (beslutning B).
/// </summary>
public sealed class EditIntegrationHandler(SystemAccess access) : AuthorizationHandler<EditIntegrationRequirement, Integration>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EditIntegrationRequirement requirement, Integration resource)
    {
        if (await access.CanEditAsync(context.User, resource.SourceSystemId)
            || await access.CanEditAsync(context.User, resource.TargetSystemId))
        {
            context.Succeed(requirement);
        }
    }
}
