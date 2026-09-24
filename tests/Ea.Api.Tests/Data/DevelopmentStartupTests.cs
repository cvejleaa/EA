using System.Net;
using System.Text;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Data;

/// <summary>Appen, som den starter lokalt (Development): migrering + fiktive seed-data kører ved opstart.</summary>
public sealed class DevelopmentStartupTests
{
    [Fact]
    public async Task Development_opstart_seeder_de_fiktive_eksempeldata()
    {
        await using var app = await TestApp.StartAsync(environment: "Development");
        var admin = await app.ClientFor(TestUsers.Admin);

        var list = await admin.ListSystemsAsync();

        Assert.Equal(15, list.Total);
        var erp = Assert.Single(list.Items, i => i.Name == "Nordlys ERP");
        Assert.Equal(3, erp.ModuleCount);
        Assert.Equal("Bo Bogholder", erp.BusinessOwner?.DisplayName);
        // Eksempeldata må ikke kunne forveksles med rigtige DTU-systemer.
        Assert.DoesNotContain(list.Items, i => i.Name.Contains("DTU", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ugyldig_json_giver_400_ogsaa_i_development()
    {
        await using var app = await TestApp.StartAsync(environment: "Development");
        var admin = await app.ClientFor(TestUsers.Admin);

        var response = await admin.PostAsync("/api/systems",
            new StringContent("{ \"name\": ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
