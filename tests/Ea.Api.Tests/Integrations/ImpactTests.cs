using System.Net;
using System.Text;
using Ea.Api.Integrations;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using Microsoft.VisualBasic.FileIO;

namespace Ea.Api.Tests.Integrations;

/// <summary>
/// "Hvad rammes": ét fixture med en forælder P (moduler M1, M2), et system X, en platform Q, en lokal løsning L og
/// en fremmed integration Y→Z via Q. Skærmen og CSV-eksporten skal vise præcis det samme udvalg.
/// </summary>
public sealed class ImpactTests
{
    private sealed record World(
        HttpClient Admin,
        SystemDetail P,
        SystemDetail M1,
        SystemDetail M2,
        SystemDetail X,
        SystemDetail Q,
        SystemDetail L,
        SystemDetail Y,
        SystemDetail Z);

    private static async Task<World> BuildAsync(TestApp app)
    {
        var admin = await app.ClientFor(TestUsers.Admin);
        var p = await admin.CreateSystemAsync("P");
        var w = new World(
            admin,
            p,
            await admin.CreateSystemAsync("M1", p.Id),
            await admin.CreateSystemAsync("M2", p.Id),
            await admin.CreateSystemAsync("X"),
            await admin.CreateSystemAsync(TestApi.NewSystem("Q", type: SystemType.Platform)),
            await admin.CreateSystemAsync(TestApi.NewSystem("L", type: SystemType.LokalLoesning)),
            await admin.CreateSystemAsync("Y"),
            await admin.CreateSystemAsync("Z"));

        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.X.Id, w.M1.Id, IntegrationType.Api, w.Q.Id)); // X → M1 (via Q)
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.P.Id, w.X.Id, IntegrationType.DirekteDb));  // P → X
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.M2.Id, w.X.Id, IntegrationType.Fil));       // M2 → X (samme modtager)
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.M1.Id, w.M2.Id, null));                     // M1 → M2 (internt)
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.M1.Id, w.L.Id, IntegrationType.Udtraek));   // M1 → L
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.Y.Id, w.Z.Id, IntegrationType.Event, w.Q.Id)); // Y → Z (fremmed, via Q)
        return w;
    }

    private static List<(IntegrationRelation Relation, string From, string To)> Rows(SystemIntegrationsResponse r) =>
        r.Items.Select(i => (i.Relation, i.Integration.From.Name, i.Integration.To.Name)).ToList();

    [Fact]
    public async Task Forælderen_ser_sine_og_modulernes_integrationer_men_ikke_fremmede()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var result = await w.Admin.SystemIntegrationsAsync(w.P.Id);

        // Præcis rækkefølge: det, der rammes (Ud), først — derefter Ind og Intern.
        Assert.Equal(
            [
                (IntegrationRelation.Ud, "M1", "L"),
                (IntegrationRelation.Ud, "P", "X"),
                (IntegrationRelation.Ud, "M2", "X"),
                (IntegrationRelation.Ind, "X", "M1"),
                (IntegrationRelation.Intern, "M1", "M2"),
            ],
            Rows(result));

        var fromModule = result.Items.Single(i => i.Integration.From.Name == "M2");
        Assert.Equal("M2", fromModule.LocalModule?.Name);
        Assert.Equal("X", fromModule.Counterpart?.Name);
        var direct = result.Items.Single(i => i.Integration.From.Name == "P");
        Assert.Null(direct.LocalModule);
        Assert.True(result.CanAdd);
    }

    [Fact]
    public async Task Taellelinjen_taeller_forskellige_systemer_og_det_der_skal_handles_paa()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var summary = (await w.Admin.SystemIntegrationsAsync(w.P.Id)).Summary;

        // Modtagere: X og L (X to gange = ét system). Leverandører: X. Lokale løsninger: L. DirekteDb: P→X.
        Assert.Equal(new IntegrationSummary(Receivers: 2, Suppliers: 1, ViaPlatform: 0, LocalSolutions: 1, DirectDb: 1), summary);
    }

    [Fact]
    public async Task Et_modul_ser_kun_sine_egne_integrationer()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var result = await w.Admin.SystemIntegrationsAsync(w.M1.Id);

        Assert.Equal(
            [
                (IntegrationRelation.Ud, "M1", "L"),
                (IntegrationRelation.Ud, "M1", "M2"), // Fra M1's side er M2 bare et andet system.
                (IntegrationRelation.Ind, "X", "M1"),
            ],
            Rows(result));
    }

    [Fact]
    public async Task Platformen_ser_alt_der_gaar_via_den()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var result = await w.Admin.SystemIntegrationsAsync(w.Q.Id);

        Assert.Equal(
            [(IntegrationRelation.Via, "X", "M1"), (IntegrationRelation.Via, "Y", "Z")],
            Rows(result));
        Assert.All(result.Items, i => Assert.Null(i.Counterpart));
        Assert.Equal(new IntegrationSummary(0, 0, ViaPlatform: 2, 0, 0), result.Summary);
    }

    [Fact]
    public async Task En_platform_der_ogsaa_er_ende_viser_ud_via_ind_i_den_raekkefoelge_og_taeller_ikke_andres_db_adgang()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var q = await admin.CreateSystemAsync(TestApi.NewSystem("Q", type: SystemType.Platform));
        var a = await admin.CreateSystemAsync("A");
        var b = await admin.CreateSystemAsync("B");
        var c = await admin.CreateSystemAsync("C");
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(c.Id, q.Id, IntegrationType.Api));        // Ind
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(a.Id, b.Id, IntegrationType.DirekteDb, q.Id)); // Via
        await admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(q.Id, a.Id, IntegrationType.Fil));        // Ud

        var result = await admin.SystemIntegrationsAsync(q.Id);

        Assert.Equal(
            [(IntegrationRelation.Ud, "Q", "A"), (IntegrationRelation.Via, "A", "B"), (IntegrationRelation.Ind, "C", "Q")],
            Rows(result));
        // Direkte DB-adgang MELLEM ANDRE systemer via platformen er ikke platformens egen DB-adgang.
        Assert.Equal(new IntegrationSummary(Receivers: 1, Suppliers: 1, ViaPlatform: 1, LocalSolutions: 0, DirectDb: 0), result.Summary);
    }

    [Fact]
    public async Task System_uden_integrationer_giver_en_tom_liste()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var system = await admin.CreateSystemAsync("Alene");

        var result = await admin.SystemIntegrationsAsync(system.Id);

        Assert.Empty(result.Items);
        Assert.Equal(new IntegrationSummary(0, 0, 0, 0, 0), result.Summary);
    }

    [Fact]
    public async Task Csv_eksporten_af_et_system_har_samme_udvalg_som_skaermen()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var response = await w.Admin.GetAsync($"/api/integrations/export.csv?systemId={w.P.Id}");

        await response.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("integrationer-p-2026-09-01.csv", response.Content.Headers.ContentDisposition?.FileNameStar);
        var rows = Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(IntegrationCsv.Header, rows[0]);
        // Sorteret efter DataFra, DataTil. Modulnavne som "Forælder > Modul". Y→Z (fremmed) er ikke med.
        Assert.Equal(
            [("P", "X"), ("P > M1", "L"), ("P > M1", "P > M2"), ("P > M2", "X"), ("X", "P > M1")],
            rows.Skip(1).Select(r => (r[3], r[4])));
        var viaRow = rows.Single(r => r[3] == "X");
        Assert.Equal(("Api", "Q"), (viaRow[5], viaRow[6]));
        Assert.Equal(w.X.Id.ToString(), viaRow[9]);
        Assert.Equal(w.M1.Id.ToString(), viaRow[10]);
        Assert.Equal(w.Q.Id.ToString(), viaRow[11]);
    }

    [Fact]
    public async Task Csv_eksport_uden_system_giver_alle_integrationer()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var response = await w.Admin.GetAsync("/api/integrations/export.csv");

        await response.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("integrationer-alle-2026-09-01.csv", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(6, Parse(await response.Content.ReadAsByteArrayAsync()).Count - 1);
    }

    [Fact]
    public async Task Systemlisten_kan_eksporteres_med_fulde_navne()
    {
        await using var app = await TestApp.StartAsync();
        var w = await BuildAsync(app);

        var response = await w.Admin.GetAsync("/api/systems/export.csv");

        await response.ExpectAsync(HttpStatusCode.OK);
        var rows = Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(SystemCsv.Header, rows[0]);
        var m1 = rows.Single(r => r[0] == w.M1.Id.ToString());
        Assert.Equal(("P > M1", "M1", "P"), (m1[1], m1[2], m1[3]));
        Assert.Equal(8, rows.Count - 1); // P, M1, M2, X, Q, L, Y, Z
    }

    private static List<string[]> Parse(byte[] bytes)
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        using var parser = new TextFieldParser(new MemoryStream(bytes), Encoding.UTF8, detectEncoding: true);
        parser.SetDelimiters(";");
        parser.HasFieldsEnclosedInQuotes = true;
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        return rows;
    }
}
