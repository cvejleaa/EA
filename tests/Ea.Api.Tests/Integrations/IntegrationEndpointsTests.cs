using System.Net;
using System.Net.Http.Json;
using Ea.Api.Common;
using Ea.Api.Integrations;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Integrations;

public sealed class IntegrationEndpointsTests
{
    private sealed record Landscape(
        HttpClient Admin, SystemDetail Hr, SystemDetail Id, SystemDetail Platform, SystemDetail Kompas);

    private static async Task<Landscape> BuildAsync(TestApp app)
    {
        var admin = await app.ClientFor(TestUsers.Admin);
        return new Landscape(
            admin,
            await admin.CreateSystemAsync("HR"),
            await admin.CreateSystemAsync("Identitetskilde"),
            await admin.CreateSystemAsync(TestApi.NewSystem("Platformen", type: SystemType.Platform)),
            await admin.CreateSystemAsync("Kompas"));
    }

    [Fact]
    public async Task Oprettet_integration_kan_hentes_med_alle_felter()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var medarbejder = await l.Admin.CreateDataObjectAsync("Medarbejder");
        var org = await l.Admin.CreateDataObjectAsync("Organisationsenhed");

        var created = await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(
            l.Hr.Id, l.Id.Id, IntegrationType.Api, l.Platform.Id, " Løbende ", "  Nye medarbejdere  ", [org.Id, medarbejder.Id]));

        var i = await l.Admin.GetIntegrationAsync(created.Id);
        Assert.Equal("Løbende", i.Name);
        Assert.Equal("Nye medarbejdere", i.Description);
        Assert.Equal((l.Hr.Id, "HR"), (i.From.Id, i.From.Name));
        Assert.Equal((l.Id.Id, "Identitetskilde"), (i.To.Id, i.To.Name));
        Assert.Equal("Platformen", i.Via?.Name);
        Assert.Equal(IntegrationType.Api, i.Type);
        Assert.Equal(["Medarbejder", "Organisationsenhed"], i.DataObjects.Select(d => d.Name));
        Assert.Equal(TestApp.Start, i.CreatedAt);
        Assert.Equal(TestApp.Start, i.UpdatedAt);
        Assert.True(i.Permissions.CanEdit);
    }

    [Fact]
    public async Task Samme_ender_type_platform_og_navn_er_en_dublet()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Api, l.Platform.Id));

        var duplicate = await l.Admin.PostIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Api, l.Platform.Id));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(Problems.DuplicateType, await duplicate.ProblemTypeAsync());
        Assert.Equal(
            "Der findes allerede en API-integration fra HR til Identitetskilde via Platformen og uden navn. " +
            "Tilføj dataobjekterne til den, eller giv den nye integration et navn, der skelner den.",
            await duplicate.ProblemDetailAsync());
        Assert.Single((await l.Admin.SystemIntegrationsAsync(l.Hr.Id)).Items);
    }

    [Fact]
    public async Task To_integrationer_uden_type_platform_og_navn_er_ogsaa_en_dublet()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, type: null));

        // NULL tæller som en værdi i nøglen (NULLS NOT DISTINCT) — ellers kunne "ukendt" registreres uendeligt mange gange.
        var duplicate = await l.Admin.PostIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, type: null));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Anden_type_andet_navn_eller_modsat_retning_er_ikke_en_dublet()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Api));

        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Fil));
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Api, name: "Natlig"));
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Id.Id, l.Hr.Id, IntegrationType.Api));

        Assert.Equal(4, (await l.Admin.SystemIntegrationsAsync(l.Hr.Id)).Items.Count);
    }

    [Fact]
    public async Task Navnet_i_noeglen_er_uafhaengigt_af_store_og_smaa_bogstaver()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, name: "Natlig fil"));

        var duplicate = await l.Admin.PostIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, name: "NATLIG FIL "));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Ugyldige_integrationer_afvises_med_feltfejl()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);

        var self = await l.Admin.PostIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Hr.Id));
        var notPlatform = await l.Admin.PostIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, via: l.Kompas.Id));
        var missing = await l.Admin.PostIntegrationAsync(new IntegrationCreateRequest(null, l.Id.Id, null, null, null, null, null));
        var unknownDataObject = await l.Admin.PostIntegrationAsync(
            TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, dataObjectIds: [Guid.NewGuid()]));

        Assert.Equal(["En integration kan ikke gå fra et system til sig selv."], (await self.ValidationErrorsAsync())["toSystemId"]);
        Assert.Equal(["Kompas er ikke registreret som platform (systemtype Platform)."],
            (await notPlatform.ValidationErrorsAsync())["viaPlatformId"]);
        Assert.Equal(["Vælg det system, data sendes fra."], (await missing.ValidationErrorsAsync())["fromSystemId"]);
        Assert.Equal(["Et eller flere af de valgte dataobjekter findes ikke."],
            (await unknownDataObject.ValidationErrorsAsync())["dataObjectIds"]);
        Assert.Empty((await l.Admin.SystemIntegrationsAsync(l.Hr.Id)).Items);
    }

    [Fact]
    public async Task Redigering_aendrer_type_platform_navn_og_dataobjekter()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var a = await l.Admin.CreateDataObjectAsync("A");
        var b = await l.Admin.CreateDataObjectAsync("B");
        var c = await l.Admin.CreateDataObjectAsync("C");
        var created = await l.Admin.CreateIntegrationAsync(
            TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, IntegrationType.Api, dataObjectIds: [a.Id, b.Id]));

        app.Time.Advance(TimeSpan.FromDays(2));
        var response = await l.Admin.PutIntegrationAsync(created.Id, created.ToUpdate() with
        {
            Type = IntegrationType.Fil,
            ViaPlatformId = l.Platform.Id,
            Name = "Natlig",
            DataObjectIds = [b.Id, c.Id],
        });
        await response.ExpectAsync(HttpStatusCode.OK);

        var updated = await l.Admin.GetIntegrationAsync(created.Id);
        Assert.Equal(IntegrationType.Fil, updated.Type);
        Assert.Equal("Platformen", updated.Via?.Name);
        Assert.Equal("Natlig", updated.Name);
        Assert.Equal(["B", "C"], updated.DataObjects.Select(d => d.Name));
        Assert.Equal(TestApp.Start, updated.CreatedAt);
        Assert.Equal(TestApp.Start.AddDays(2), updated.UpdatedAt);
        // Fra og til kan ikke ændres.
        Assert.Equal((l.Hr.Id, l.Id.Id), (updated.From.Id, updated.To.Id));
    }

    [Fact]
    public async Task En_foraeldet_version_afvises_ogsaa_naar_kun_dataobjekterne_aendres()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var a = await l.Admin.CreateDataObjectAsync("A");
        var b = await l.Admin.CreateDataObjectAsync("B");
        var seen = await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, dataObjectIds: [a.Id]));

        await (await l.Admin.PutIntegrationAsync(seen.Id, seen.ToUpdate() with { Description = "Ændret af en anden" }))
            .ExpectAsync(HttpStatusCode.OK);

        // Kun dataobjekterne (en anden tabel) ændres — versionen skal alligevel tjekkes.
        var stale = await l.Admin.PutIntegrationAsync(seen.Id, seen.ToUpdate() with { DataObjectIds = [a.Id, b.Id] });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(Problems.StaleVersionType, await stale.ProblemTypeAsync());
        Assert.StartsWith("Integrationen er ændret af en anden", await stale.ProblemDetailAsync(), StringComparison.Ordinal);
        var after = await l.Admin.GetIntegrationAsync(seen.Id);
        Assert.Equal(["A"], after.DataObjects.Select(d => d.Name));
        Assert.Equal("Ændret af en anden", after.Description);
    }

    [Fact]
    public async Task Uaendret_via_kan_gemmes_selv_om_platformen_har_skiftet_type()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var created = await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, via: l.Platform.Id));
        var platform = await l.Admin.GetSystemAsync(l.Platform.Id);
        await (await l.Admin.PutSystemAsync(platform.Id, platform.ToWrite() with { Type = SystemType.Saas })).ExpectAsync(HttpStatusCode.OK);

        var response = await l.Admin.PutIntegrationAsync(created.Id, created.ToUpdate() with { Description = "Stadig via" });

        await response.ExpectAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sletning_fjerner_integrationen_men_ikke_dataobjekterne()
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var a = await l.Admin.CreateDataObjectAsync("Medarbejder");
        var created = await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, dataObjectIds: [a.Id]));

        await (await l.Admin.DeleteAsync($"/api/integrations/{created.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.NotFound, (await l.Admin.GetAsync($"/api/integrations/{created.Id}")).StatusCode);
        var dataObjects = await l.Admin.GetFromJsonAsync<List<DataObjectDto>>("/api/data-objects", TestApp.Json);
        Assert.Equal(["Medarbejder"], dataObjects!.Select(d => d.Name));
    }

    [Fact]
    public async Task Dataobjekter_er_unikke_og_maa_ikke_indeholde_separatoren()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.CreateDataObjectAsync("Medarbejder");

        var duplicate = await admin.PostAsJsonAsync("/api/data-objects", new CreateDataObjectRequest(" MEDARBEJDER"), TestApp.Json);
        var separator = await admin.PostAsJsonAsync("/api/data-objects", new CreateDataObjectRequest("A | B"), TestApp.Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(Problems.DuplicateType, await duplicate.ProblemTypeAsync());
        Assert.Equal(["Navnet må ikke indeholde tegnet '|'."], (await separator.ValidationErrorsAsync())["name"]);
        var search = await admin.GetFromJsonAsync<List<DataObjectDto>>("/api/data-objects?q=arbejd", TestApp.Json);
        Assert.Equal(["Medarbejder"], search!.Select(d => d.Name));
    }

    [Theory]
    [InlineData("kilde", 0, 1, 0)]
    [InlineData("mål", 0, 1, 0)]
    [InlineData("platform", 0, 0, 1)]
    public async Task Et_system_i_brug_af_integrationer_kan_ikke_slettes(string role, int modules, int integrations, int platformFor)
    {
        await using var app = await TestApp.StartAsync();
        var l = await BuildAsync(app);
        var integration = role switch
        {
            "kilde" => await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Kompas.Id, l.Id.Id)),
            "mål" => await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Id.Id, l.Kompas.Id)),
            _ => await l.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(l.Hr.Id, l.Id.Id, via: l.Platform.Id)),
        };
        var subject = role == "platform" ? l.Platform : l.Kompas;
        var expected = SystemRules.DeleteBlockedReason(subject.Name, new SystemUsage(modules, integrations, platformFor));

        var detail = await l.Admin.GetSystemAsync(subject.Id);
        var blocked = await l.Admin.DeleteAsync($"/api/systems/{subject.Id}");

        Assert.NotNull(expected);
        Assert.False(detail.Permissions.CanDelete);
        Assert.Equal(expected, detail.Permissions.DeleteBlockedReason);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(Problems.BlockedType, await blocked.ProblemTypeAsync());
        Assert.Equal(expected, await blocked.ProblemDetailAsync());

        await (await l.Admin.DeleteAsync($"/api/integrations/{integration.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        await (await l.Admin.DeleteAsync($"/api/systems/{subject.Id}")).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ukendt_integration_giver_404()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/integrations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutIntegrationAsync(id, new IntegrationUpdateRequest(null, null, null, null, null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/integrations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/systems/{id}/integrations")).StatusCode);
    }
}
