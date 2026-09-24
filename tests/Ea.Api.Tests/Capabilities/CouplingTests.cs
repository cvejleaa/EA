using System.Net;
using System.Net.Http.Json;
using Ea.Api.Capabilities;
using Ea.Api.Common;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using Npgsql;
using static Ea.Api.Tests.Infrastructure.TestApiCapabilities;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Koblinger mellem systemer og kapabiliteter (delopgave 3b): kun blade og ikke udgåede kan vælges, null betyder
/// uændret, familien vises, og importen lader kapabiliteter med koblinger udgå i stedet for at slette dem.
/// </summary>
public sealed class CouplingTests
{
    // Blade: K1.1.1, K1.2, K2.1. Grupper: K1, K1.1, K2.
    private static readonly byte[] Model = File(
        ("K1", "Uddannelse", null, null),
        ("K1.1", "Studieadministration", "K1", null),
        ("K1.1.1", "Optagelse", "K1.1", null),
        ("K1.2", "Undervisning", "K1", null),
        ("K2", "Forskning", null, null),
        ("K2.1", "Laboratorier", "K2", null));

    private static async Task<(HttpClient Admin, Dictionary<string, Guid> Ids)> StartAsync(TestApp app)
    {
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var ids = (await admin.CapabilitiesAsync()).Items.ToDictionary(i => i.Code, i => i.Id);
        return (admin, ids);
    }

    private static List<(string Code, string? HeldBy)> Codes(SystemDetail s) =>
        s.Capabilities.Select(c => (c.Capability.Code, c.HeldBy?.Name)).ToList();

    [Fact]
    public async Task Koblinger_saettes_og_fjernes_og_null_betyder_uaendret()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");

        await (await admin.CoupleAsync(system, ids["K2.1"], ids["K1.1.1"])).ExpectAsync(HttpStatusCode.OK);
        var coupled = await admin.GetSystemAsync(system.Id);
        Assert.Equal([("K1.1.1", null), ("K2.1", null)], Codes(coupled));
        Assert.Equal(("Optagelse", "Uddannelse › Studieadministration", false),
            (coupled.Capabilities[0].Capability.Name, coupled.Capabilities[0].Capability.Path, coupled.Capabilities[0].Capability.Retired));

        // En klient, der ikke sender feltet (null), må ikke slette koblingerne.
        var untouched = await admin.PutSystemAsync(system.Id, coupled.ToWrite() with { Description = "Ny beskrivelse", CapabilityIds = null });
        await untouched.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([("K1.1.1", null), ("K2.1", null)], Codes(await admin.GetSystemAsync(system.Id)));

        // En tom liste fjerner alle.
        var cleared = await admin.CoupleAsync(await admin.GetSystemAsync(system.Id));
        await cleared.ExpectAsync(HttpStatusCode.OK);
        Assert.Empty((await admin.GetSystemAsync(system.Id)).Capabilities);
    }

    [Fact]
    public async Task En_ny_kobling_skal_vaere_et_blad()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");

        var response = await admin.CoupleAsync(system, ids["K1.1"]);

        var errors = await response.ValidationErrorsAsync();
        Assert.Equal(["\"K1.1 Studieadministration\" har underkapabiliteter — vælg den, der passer bedst."], errors["capabilityIds"]);
        Assert.Empty((await admin.GetSystemAsync(system.Id)).Capabilities);
    }

    [Fact]
    public async Task En_kobling_bevares_naar_bladet_faar_boern_men_kan_ikke_oprettes_igen_paa_et_andet_system()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var a = await admin.CreateSystemAsync("A");
        var b = await admin.CreateSystemAsync("B");
        await (await admin.CoupleAsync(a, ids["K1.2"])).ExpectAsync(HttpStatusCode.OK);
        await admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", null), ("K1.1.1", "Optagelse", "K1.1", null),
            ("K1.2", "Undervisning", "K1", null), ("K1.2.1", "Kursusindhold", "K1.2", null),
            ("K2", "Forskning", null, null), ("K2.1", "Laboratorier", "K2", null)));

        // A beholder sin kobling ved et almindeligt gem; B kan ikke få en ny.
        var keep = await admin.PutSystemAsync(a.Id, (await admin.GetSystemAsync(a.Id)).ToWrite() with
        {
            Description = "Ændret",
            CapabilityIds = [ids["K1.2"]],
        });
        var add = await admin.CoupleAsync(b, ids["K1.2"]);

        await keep.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([("K1.2", null)], Codes(await admin.GetSystemAsync(a.Id)));
        Assert.Contains("har underkapabiliteter", (await add.ValidationErrorsAsync())["capabilityIds"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_kobling_bevares_naar_kapabiliteten_udgaar_men_kan_ikke_oprettes_igen_paa_et_andet_system()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var a = await admin.CreateSystemAsync("A");
        var b = await admin.CreateSystemAsync("B");
        await (await admin.CoupleAsync(a, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", null), ("K1.1.1", "Optagelse", "K1.1", null),
            ("K1.2", "Undervisning", "K1", null), ("K2", "Forskning", null, null)));

        var keep = await admin.PutSystemAsync(a.Id, (await admin.GetSystemAsync(a.Id)).ToWrite() with
        {
            Description = "Ændret",
            CapabilityIds = [ids["K2.1"]],
        });
        var add = await admin.CoupleAsync(b, ids["K2.1"]);

        await keep.ExpectAsync(HttpStatusCode.OK);
        var kept = Assert.Single((await admin.GetSystemAsync(a.Id)).Capabilities).Capability;
        Assert.Equal(("K2.1", true, "Forskning"), (kept.Code, kept.Retired, kept.Path));
        Assert.Equal(["\"K2.1 Laboratorier\" er udgået af kortet og kan ikke vælges."], (await add.ValidationErrorsAsync())["capabilityIds"]);
    }

    [Fact]
    public async Task En_ukendt_kapabilitet_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");

        var response = await admin.CoupleAsync(system, Guid.NewGuid());

        Assert.Equal(["En eller flere af de valgte kapabiliteter findes ikke."], (await response.ValidationErrorsAsync())["capabilityIds"]);
    }

    [Fact]
    public async Task En_ren_kapabilitetsaendring_med_en_foraeldet_version_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");
        await (await admin.CoupleAsync(system, ids["K1.2"])).ExpectAsync(HttpStatusCode.OK);

        // "system" er den gamle version (før koblingen). Kun kapabiliteterne ændres — versionen skal stadig tjekkes.
        var stale = await admin.CoupleAsync(system, ids["K2.1"]);

        await stale.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleVersionType, await stale.ProblemTypeAsync());
        Assert.Equal([("K1.2", null)], Codes(await admin.GetSystemAsync(system.Id)));
    }

    [Fact]
    public async Task Laeseren_kan_se_koblingerne_men_ikke_aendre_dem()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var reader = await app.ClientFor(TestUsers.Reader);
        var system = await admin.CreateSystemAsync("Kompas");
        await (await admin.CoupleAsync(system, ids["K1.2"])).ExpectAsync(HttpStatusCode.OK);

        var current = await reader.GetSystemAsync(system.Id);
        var response = await reader.CoupleAsync(current, ids["K2.1"]);

        Assert.Equal([("K1.2", null)], Codes(current));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal([("K1.2", null)], Codes(await admin.GetSystemAsync(system.Id)));
    }

    [Fact]
    public async Task Familien_vises_egne_koblinger_foerst_og_via_modul_eller_forælder_bagefter()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var parent = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", parent.Id);
        await (await admin.CoupleAsync(await admin.GetSystemAsync(parent.Id), ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(hr, ids["K1.1.1"])).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal([("K2.1", null), ("K1.1.1", "HR")], Codes(await admin.GetSystemAsync(parent.Id)));
        Assert.Equal([("K1.1.1", null), ("K2.1", "Nordlys")], Codes(await admin.GetSystemAsync(hr.Id)));
    }

    [Fact]
    public async Task Filteret_viser_systemer_med_en_kapabilitet_og_dem_hvor_familien_ingen_har()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var p = await admin.CreateSystemAsync("P");        // Kun modulet M er koblet → P er dækket.
        var m = await admin.CreateSystemAsync("M", p.Id);
        var q = await admin.CreateSystemAsync("Q");        // Q er koblet → modulet Q1 er dækket via forælderen.
        await admin.CreateSystemAsync("Q1", q.Id);
        var r = await admin.CreateSystemAsync("R");        // R ukoblet; R1 koblet; R2's søskende dækker IKKE R2.
        var r1 = await admin.CreateSystemAsync("R1", r.Id);
        await admin.CreateSystemAsync("R2", r.Id);
        await admin.CreateSystemAsync("X");                // Helt ukoblet.
        await (await admin.CoupleAsync(m, ids["K1.1.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(q, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(r1, ids["K1.1.1"])).ExpectAsync(HttpStatusCode.OK);

        var byCapability = await admin.ListSystemsAsync($"?capabilityId={ids["K1.1.1"]}");
        var none = await admin.ListSystemsAsync("?capabilityId=none");
        var bad = await admin.GetAsync("/api/systems?capabilityId=ingen");

        Assert.Equal(["M", "R1"], byCapability.Items.Select(i => i.Name));
        Assert.Equal(["R2", "X"], none.Items.Select(i => i.Name));
        await bad.ExpectAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Kortet_markerer_hvad_der_kan_vaelges_og_viser_stien()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);

        var items = (await admin.CapabilitiesAsync()).Items;

        Assert.Equal(
            [("K1", false, ""), ("K1.1", false, "Uddannelse"), ("K1.1.1", true, "Uddannelse › Studieadministration"),
             ("K1.2", true, "Uddannelse"), ("K2", false, ""), ("K2.1", true, "Forskning")],
            items.Select(i => (i.Code, i.Selectable, i.Path)));
    }

    [Fact]
    public async Task Import_lader_en_koblet_kapabilitet_udgaa_i_stedet_for_at_slette_den()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var parent = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", parent.Id);
        await (await admin.CoupleAsync(hr, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        var withoutK2 = File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", null),
            ("K1.1.1", "Optagelse", "K1.1", null), ("K1.2", "Undervisning", "K1", null));

        var preview = await admin.DryRunAsync(withoutK2);

        // K2 (ingen koblinger) slettes; K2.1 (koblet til HR) udgår. 2 af 6 forsvinder = 33 % → advarsel.
        Assert.Equal(
            [(CapabilityChangeKind.Slettes, "K2", 0), (CapabilityChangeKind.Udgaar, "K2.1", 1)],
            preview.Changes.Select(c => (c.Kind, c.Code, c.AffectedSystems.Count)));
        Assert.Equal("Nordlys › HR", preview.Changes[1].AffectedSystems[0].Name);
        Assert.Equal(
            new CapabilityImportSummary(New: 0, Changed: 0, Removed: 1, Retired: 1, Reactivated: 0, Unchanged: 4,
                CurrentTotal: 6, LargeRemoval: true, CouplingsToMove: 1, SystemsToMove: 1),
            preview.Summary);

        await admin.ImportAsync(withoutK2);

        var tree = await admin.CapabilitiesAsync();
        Assert.DoesNotContain(tree.Items, i => i.Code is "K2" or "K2.1");
        var retired = Assert.Single(tree.Retired);
        Assert.Equal(("K2.1", "Forskning", TestApp.Start), (retired.Code, retired.RetiredPath, retired.RetiredAt));
        Assert.Equal(["Nordlys › HR"], retired.Systems.Select(s => s.Name));
        Assert.Equal([("K2.1", null)], Codes(await admin.GetSystemAsync(hr.Id)));

        // Eksporten er kortet — uden det udgåede, så eksport → import ikke genaktiverer det.
        var export = await (await admin.GetAsync("/api/capabilities/export.csv")).Content.ReadAsByteArrayAsync();
        Assert.Empty((await admin.DryRunAsync(export)).Changes);
    }

    [Fact]
    public async Task En_udgaaet_kapabilitet_genaktiveres_naar_den_staar_i_filen_igen()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Laborant");
        await (await admin.CoupleAsync(system, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await admin.ImportAsync(File(("K1", "Uddannelse", null, null), ("K1.2", "Undervisning", "K1", null)));

        var preview = await admin.DryRunAsync(Model);
        await admin.ImportAsync(Model);

        Assert.Contains((CapabilityChangeKind.Genaktiveres, "K2.1"), preview.Changes.Select(c => (c.Kind, c.Code)));
        Assert.Equal(1, preview.Summary.Reactivated);
        var tree = await admin.CapabilitiesAsync();
        Assert.Empty(tree.Retired);
        Assert.Equal((1, "Forskning", true), tree.Items.Where(i => i.Code == "K2.1").Select(i => (i.Depth, i.Path, i.Selectable)).Single());
        Assert.False(Assert.Single((await admin.GetSystemAsync(system.Id)).Capabilities).Capability.Retired);
    }

    [Fact]
    public async Task En_udgaaet_kapabilitet_uden_koblinger_slettes_ved_naeste_import()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Laborant");
        await (await admin.CoupleAsync(system, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        var withoutK2 = File(("K1", "Uddannelse", null, null), ("K1.2", "Undervisning", "K1", null));
        await admin.ImportAsync(withoutK2);
        await (await admin.CoupleAsync(await admin.GetSystemAsync(system.Id))).ExpectAsync(HttpStatusCode.OK); // Flyttet væk.

        var preview = await admin.DryRunAsync(withoutK2);
        await admin.ImportAsync(withoutK2);

        Assert.Equal([(CapabilityChangeKind.Slettes, "K2.1")], preview.Changes.Select(c => (c.Kind, c.Code)));
        Assert.Equal(0, preview.Summary.CurrentTotal - 2); // Det udgåede tæller ikke med i kortet.
        Assert.Empty((await admin.CapabilitiesAsync()).Retired);
    }

    [Fact]
    public async Task Et_koblet_blad_der_faar_boern_meldes_saa_koblingerne_kan_flyttes()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");
        await (await admin.CoupleAsync(system, ids["K1.2"])).ExpectAsync(HttpStatusCode.OK);

        var preview = await admin.DryRunAsync(File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", null), ("K1.1.1", "Optagelse", "K1.1", null),
            ("K1.2", "Undervisning", "K1", null), ("K1.2.1", "Kursusindhold", "K1.2", null),
            ("K1.1.2", "Eksamen", "K1.1", null), // Et ukoblet blad, der får en søskende — ingen melding.
            ("K2", "Forskning", null, null), ("K2.1", "Laboratorier", "K2", null)));

        Assert.Equal(
            [(CapabilityChangeKind.FaarUnderkapabiliteter, "K1.2", 1), (CapabilityChangeKind.Ny, "K1.1.2", 0), (CapabilityChangeKind.Ny, "K1.2.1", 0)],
            preview.Changes.Select(c => (c.Kind, c.Code, c.AffectedSystems.Count)));
        Assert.Equal((2, 0, 6, 1, 1), (preview.Summary.New, preview.Summary.Changed, preview.Summary.Unchanged,
            preview.Summary.CouplingsToMove, preview.Summary.SystemsToMove));
    }

    [Fact]
    public async Task Aendres_koblingerne_efter_toer_koerslen_afvises_importen()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var a = await admin.CreateSystemAsync("A");
        var b = await admin.CreateSystemAsync("B");
        await (await admin.CoupleAsync(a, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        var withoutK2 = File(("K1", "Uddannelse", null, null), ("K1.2", "Undervisning", "K1", null));
        var fingerprint = (await admin.DryRunAsync(withoutK2)).Fingerprint;

        await (await admin.CoupleAsync(b, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK); // Tør-kørslen viste kun A.
        var response = await admin.PostImportAsync(withoutK2, dryRun: false, fingerprint);

        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Contains("koblingerne", await response.ProblemDetailAsync(), StringComparison.Ordinal);
        Assert.Contains((await admin.CapabilitiesAsync()).Items, i => i.Code == "K2.1");
    }

    /// <summary>
    /// Efterligner en import, der holder sin lås og ændrer kortet, mens en kobling gemmes. Koblingen skal vente på
    /// importen og valideres mod resultatet — ikke give 500 (kapabiliteten slettet) eller en kobling til et ikke-blad.
    /// </summary>
    [Theory]
    [InlineData("slettes", "En eller flere af de valgte kapabiliteter findes ikke.")]
    [InlineData("faar-boern", "\"K1.2 Undervisning\" har underkapabiliteter — vælg den, der passer bedst.")]
    public async Task En_kobling_venter_paa_en_samtidig_import_og_valideres_mod_resultatet(string change, string message)
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");
        var target = ids[change == "slettes" ? "K2.1" : "K1.2"];

        await using var import = new NpgsqlConnection(app.ConnectionString);
        await import.OpenAsync();
        await using var transaction = await import.BeginTransactionAsync();
        await Sql(import, transaction, "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE");

        var put = admin.CoupleAsync(system, target);
        await WaitForBlockedLockAsync(import, transaction);
        await Sql(import, transaction, change == "slettes"
            ? $"DELETE FROM ea.capabilities WHERE id = '{target}'"
            : $"INSERT INTO ea.capabilities (id, code, code_normalized, name, parent_id) VALUES ('{Guid.NewGuid()}', 'K1.2.1', 'K1.2.1', 'Barn', '{target}')");
        await transaction.CommitAsync();

        var response = await put;
        Assert.Equal([message], (await response.ValidationErrorsAsync())["capabilityIds"]);
        Assert.Empty((await admin.GetSystemAsync(system.Id)).Capabilities);
    }

    private static async Task Sql(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Venter, til en anden forbindelse står i kø på en lås til koblingerne (så testen rammer vinduet).</summary>
    private static async Task WaitForBlockedLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        for (var i = 0; i < 200; i++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE NOT granted AND relation = 'ea.system_capabilities'::regclass",
                connection, transaction);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Koblingen nåede aldrig at vente på importens lås.");
    }

    [Fact]
    public async Task Hoejst_100_kapabiliteter_pr_system_og_graensen_tjekkes_foer_alt_andet()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kompas");

        // 100 (ukendte) id'er når forbi grænsen og fejler på, at de ikke findes. 101 afvises af grænsen — også når det
        // er det samme id gentaget, for grænsen gælder den rå liste (ellers kunne en kæmpeliste slippe forbi).
        var atLimit = await admin.CoupleAsync(system, Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray());
        var overLimit = await admin.CoupleAsync(system, Enumerable.Repeat(Guid.NewGuid(), 101).ToArray());

        Assert.Equal(["En eller flere af de valgte kapabiliteter findes ikke."], (await atLimit.ValidationErrorsAsync())["capabilityIds"]);
        Assert.Equal(["Højst 100 kapabiliteter pr. system."], (await overLimit.ValidationErrorsAsync())["capabilityIds"]);
    }

    [Fact]
    public async Task Et_slettet_system_tager_sine_koblinger_med()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Kortvarigt");
        await (await admin.CoupleAsync(system, ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);

        await (await admin.DeleteAsync($"/api/systems/{system.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        var preview = await admin.DryRunAsync(File(("K1", "Uddannelse", null, null)));

        Assert.Contains((CapabilityChangeKind.Slettes, "K2.1"), preview.Changes.Select(c => (c.Kind, c.Code)));
        Assert.Equal(0, preview.Summary.Retired);
    }
}
