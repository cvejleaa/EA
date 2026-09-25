using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Ea.Api.Auth;
using Ea.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>
/// Binder en person til en login-identitet direkte i testdatabasen. API'et har med vilje ingen måde at sætte
/// <c>Person.Oid</c> på (bindingen kommer fra login), og der laves ingen API-flade kun til tests.
/// </summary>
public static class TestAccess
{
    public static Task BindPersonAsync(this TestApp app, Guid personId, TestUser user) => app.BindPersonAsync(personId, user.Oid);

    public static async Task BindPersonAsync(this TestApp app, Guid personId, string oid)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EaDbContext>();
        var person = await db.Persons.FindAsync(personId) ?? throw new InvalidOperationException("Personen findes ikke.");
        person.Oid = oid;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// En klient med et token, testen selv har dannet med dev-nøglen — til claims, dev-login aldrig udsteder (fx et tomt
    /// oid). Samme udsteder, modtager og klokke som dev-login.
    /// </summary>
    public static HttpClient ClientWithOid(this TestApp app, string oid)
    {
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = DevAuthOptions.Issuer,
            Audience = DevAuthOptions.Audience,
            Subject = new ClaimsIdentity([new Claim(ClaimNames.ObjectId, oid), new Claim(ClaimNames.Name, "Uden identitet")]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestUsers.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        var client = app.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
