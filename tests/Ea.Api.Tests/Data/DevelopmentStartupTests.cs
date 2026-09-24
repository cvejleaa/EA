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

        // Integrationerne er samlet om identitetskilden, så "hvad rammes" kan demonstreres.
        var identity = Assert.Single(list.Items, i => i.Name == "Identitetskilde (fiktiv)");
        var integrations = await admin.SystemIntegrationsAsync(identity.Id);
        Assert.Equal(
            [
                (Ea.Api.Integrations.IntegrationRelation.Ud, "Kompas Sag"),
                (Ea.Api.Integrations.IntegrationRelation.Ud, "Laborant"),
                (Ea.Api.Integrations.IntegrationRelation.Ud, "Servicedesk Plus (fiktiv)"),
                (Ea.Api.Integrations.IntegrationRelation.Ud, "Studium"),
                (Ea.Api.Integrations.IntegrationRelation.Ind, "Nordlys HR"),
            ],
            integrations.Items.Select(i => (i.Relation, i.Counterpart!.Name)));
        Assert.Equal(1, integrations.Summary.DirectDb);
        var all = await admin.GetAsync("/api/integrations/export.csv");
        Assert.Equal(8, (await all.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
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
