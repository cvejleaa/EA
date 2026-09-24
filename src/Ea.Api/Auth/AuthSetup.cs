using System.Text;
using Ea.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Ea.Api.Auth;

public static class AuthSetup
{
    public const string ModeKey = "Auth:Mode";
    public const string DevMode = "Dev";
    public const string EntraMode = "Entra";

    public static bool IsDevMode(IConfiguration configuration) => configuration[ModeKey] == DevMode;

    /// <summary>
    /// Autentificering: Bearer-JWT i begge modes — kun udstederen er forskellig. Dev-mode er kun tilladt i
    /// Development/Testing, så en offentlig udrulning med dev-login er umulig pr. konstruktion.
    /// </summary>
    public static void AddEaAuth(this WebApplicationBuilder builder)
    {
        var mode = builder.Configuration[ModeKey];
        var env = builder.Environment;

        switch (mode)
        {
            case DevMode:
                if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
                {
                    throw new InvalidOperationException(
                        $"Auth:Mode=Dev er kun tilladt i Development/Testing (miljø: {env.EnvironmentName}).");
                }

                var dev = builder.Configuration.GetSection("Auth:Dev").Get<DevAuthOptions>() ?? new DevAuthOptions();
                if (Encoding.UTF8.GetByteCount(dev.SigningKey) < 32)
                {
                    throw new InvalidOperationException("Auth:Dev:SigningKey skal være mindst 32 bytes.");
                }

                builder.Services.AddSingleton(dev);
                builder.Services
                    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(o =>
                    {
                        o.MapInboundClaims = false;
                        o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidIssuer = DevAuthOptions.Issuer,
                            ValidAudience = DevAuthOptions.Audience,
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(dev.SigningKey)),
                            NameClaimType = ClaimNames.Name,
                            RoleClaimType = ClaimNames.Roles,
                        };
                    });
                break;

            case EntraMode:
                // Spor A: AddMicrosoftIdentityWebApi med samme claim-kontrakt (ClaimNames).
                throw new NotSupportedException("Entra-login er ikke sat op endnu (spor A i docs/plan.md).");

            default:
                throw new InvalidOperationException($"{ModeKey} skal være '{DevMode}' eller '{EntraMode}'.");
        }

        builder.Services.AddEaAuthorization();
    }

    private static void AddEaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Alt kræver login, medmindre et endpoint eksplicit er åbnet (se AnonymousAllowListTests).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.CreateSystem, p => p.RequireRole(AppRoles.Admin))
            .AddPolicy(Policies.ManagePersons, p => p.RequireRole(AppRoles.Admin))
            .AddPolicy(Policies.EditSystem, p => p.AddRequirements(new EditSystemRequirement()));

        services.AddSingleton<IAuthorizationHandler, EditSystemHandler>();
    }
}
