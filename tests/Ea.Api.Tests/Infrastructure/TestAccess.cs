using Ea.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>
/// Binder en person til en login-identitet direkte i testdatabasen. API'et har med vilje ingen måde at sætte
/// <c>Person.Oid</c> på (bindingen kommer fra login), og der laves ingen API-flade kun til tests.
/// </summary>
public static class TestAccess
{
    public static async Task BindPersonAsync(this TestApp app, Guid personId, TestUser user)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EaDbContext>();
        var person = await db.Persons.FindAsync(personId) ?? throw new InvalidOperationException("Personen findes ikke.");
        person.Oid = user.Oid;
        await db.SaveChangesAsync();
    }
}
