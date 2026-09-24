using Ea.Api.Auth;
using Ea.Api.Integrations;
using Microsoft.AspNetCore.Authorization;

namespace Ea.Api.Authorization;

public sealed class EditIntegrationRequirement : IAuthorizationRequirement;

/// <summary>
/// Hvem må oprette, redigere og slette en bestemt integration. Resursen er selve integrationen (med kilde, mål
/// og platform udfyldt) — også ved oprettelse, hvor den endnu ikke er gemt. Delopgave 4 udvider med: brugeren må
/// redigere en af enderne (samme regel som EditSystemHandler); om platformens forvalter også må, afgøres dér.
/// </summary>
public sealed class EditIntegrationHandler : AuthorizationHandler<EditIntegrationRequirement, Integration>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EditIntegrationRequirement requirement, Integration resource)
    {
        if (context.User.IsInRole(AppRoles.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
