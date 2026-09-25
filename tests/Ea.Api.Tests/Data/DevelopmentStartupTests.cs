using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ea.Api.Data;
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

    /// <summary>
    /// Broen, der lader en udvikler prøve forvalter-adgangen lokalt: dev-brugeren "frida" i appsettings.Development.json
    /// har samme oid som den person, DevSeed giver roller — og den oid giver ret over netop hendes systemer.
    /// </summary>
    [Fact]
    public async Task Dev_brugeren_frida_er_forvalter_paa_de_seedede_systemer()
    {
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(RepoPaths.File("src", "Ea.Api", "appsettings.Development.json")))!;
        var frida = settings["Auth"]!["Dev"]!["Users"]!.AsArray().Single(u => (string?)u!["Id"] == "frida")!;
        Assert.Equal(DevSeed.FridaOid, (string?)frida["Oid"]);

        await using var app = await TestApp.StartAsync(environment: "Development");
        var admin = await app.ClientFor(TestUsers.Admin);
        var ids = (await admin.ListSystemsAsync()).Items.ToDictionary(i => i.Name, i => i.Id);
        var client = app.ClientWithOid(DevSeed.FridaOid);

        Assert.True((await client.GetSystemAsync(ids["Nordlys ERP"])).Permissions.CanEdit);
        Assert.True((await client.GetSystemAsync(ids["Nordlys HR"])).Permissions.CanEdit); // Modul under Nordlys ERP.
        Assert.True((await client.GetSystemAsync(ids["Laborant"])).Permissions.CanEdit);
        Assert.False((await client.GetSystemAsync(ids["Kompas Sag"])).Permissions.CanEdit);
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
