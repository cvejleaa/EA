using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ea.Api.Auth;

public sealed record AuthModeResponse(string Mode);

public sealed record DevUserResponse(string Id, string Name, IReadOnlyList<string> Roles);

public sealed record DevTokenRequest(string UserId);

public sealed record DevTokenResponse(string AccessToken);

public static class DevAuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/mode", (IConfiguration config) => new AuthModeResponse(config[AuthSetup.ModeKey] ?? ""))
            .AllowAnonymous()
            .WithTags("Login");

        if (!AuthSetup.IsDevMode(app.Configuration))
        {
            return;
        }

        var group = app.MapGroup("/api/dev").AllowAnonymous().WithTags("Login");

        group.MapGet("/users", (DevAuthOptions dev) =>
            dev.Users.Select(u => new DevUserResponse(u.Id, u.Name, u.Roles)).ToList());

        group.MapPost("/token", Results<Ok<DevTokenResponse>, ProblemHttpResult> (DevTokenRequest request, DevAuthOptions dev) =>
        {
            var user = dev.Users.FirstOrDefault(u => u.Id == request.UserId);
            if (user is null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ukendt udviklingsbruger.");
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimNames.ObjectId, user.Oid),
                new Claim(ClaimNames.Name, user.Name),
                .. user.Roles.Select(r => new Claim(ClaimNames.Roles, r)),
            ]);

            // Rigtig klokke (ikke TimeProvider): JwtBearer validerer levetiden mod den rigtige klokke.
            var now = DateTime.UtcNow;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = DevAuthOptions.Issuer,
                Audience = DevAuthOptions.Audience,
                Subject = identity,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddHours(8),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(dev.SigningKey)), SecurityAlgorithms.HmacSha256),
            });

            return TypedResults.Ok(new DevTokenResponse(token));
        });
    }
}
