using System.Security.Claims;

namespace Ea.Api.Auth;

/// <summary>
/// Kontrakten mellem dev-login og Entra ID: begge udsteder tokens med præcis disse claims.
/// Identitet forankres i "oid" — aldrig i e-mail eller UPN.
/// </summary>
public static class ClaimNames
{
    public const string ObjectId = "oid";
    public const string Name = "name";
    public const string Roles = "roles";
}

/// <summary>Entra app roles.</summary>
public static class AppRoles
{
    /// <summary>Enterprise arkitekt/kurator: må oprette og redigere alt.</summary>
    public const string Admin = "EA.Admin";
}

public static class ClaimsPrincipalExtensions
{
    public static string ObjectId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimNames.ObjectId) ?? throw new InvalidOperationException("Token mangler oid-claim.");

    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimNames.Name) ?? user.ObjectId();
}
