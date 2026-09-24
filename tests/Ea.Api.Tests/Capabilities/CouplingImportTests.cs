using System.Net;
using System.Net.Http.Json;
using Ea.Api.Capabilities;
using Ea.Api.Common;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using Npgsql;
using static Ea.Api.Tests.Infrastructure.DbLocks;
using static Ea.Api.Tests.Infrastructure.TestApi;
using static Ea.Api.Tests.Infrastructure.TestApiCapabilities;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Import af koblings-CSV'en (3d, docs/csv-koblinger.md): filen er hele sandheden om egne koblinger for systemerne i
/// den, systemer uden for filen røres ikke, og tør-kørslen viser og advarer, før noget gemmes.
/// </summary>
public sealed class CouplingImportTests
{
    // Blade: K1.1, K1.2, K2.1. Grupper: K1, K2.
    private static readonly byte[] Model = File(
        ("K1", "Uddannelse", null, null),
        ("K1.1", "Optagelse", "K1", null),
        ("K1.2", "Undervisning", "K1", null),
        ("K2", "Forskning", null, null),
        ("K2.1", "Laboratorier", "K2", null));

    private static async Task<(HttpClient Admin, Dictionary<string, Guid> Ids)> StartAsync(TestApp app, byte[]? model = null)
    {
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(model ?? Model);
        var ids = (await admin.CapabilitiesAsync()).Items.ToDictionary(i => i.Code, i => i.Id);
        return (admin, ids);
    }

    private static async Task<SystemDetail> CoupledAsync(HttpClient admin, SystemDetail system, params Guid[] capabilities)
    {
        await (await admin.CoupleAsync(system, capabilities)).ExpectAsync(HttpStatusCode.OK);
        return await admin.GetSystemAsync(system.Id);
    }

    private static List<string> Codes(SystemDetail s) =>
        s.Capabilities.Where(c => c.HeldBy is null).Select(c => c.Capability.Code).ToList();

    private static List<(CouplingChangeKind, string, string)> Changes(CouplingImportResult result) =>
        result.Changes.Select(c => (c.Kind, c.System.Name, c.Code)).ToList();

    private static Dictionary<string, string> Row(string systemId, string fullName, string code) =>
        CouplingCsv.Header.ToDictionary(c => c, c => c switch
        {
            "SystemId" => systemId,
            "FuldtNavn" => fullName,
            "Kode" => code,
            _ => "",
        });

    [Fact]
    public async Task En_eksport_indlaest_igen_giver_nul_aendringer()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", nordlys.Id);
        var kompas = await admin.CreateSystemAsync("Kompas");
        var formula = await admin.CreateSystemAsync("=Formel+1");
        await admin.CreateSystemAsync("Tomrum");
        await CoupledAsync(admin, kompas, ids["K1.1"], ids["K2.1"]);
        await CoupledAsync(admin, hr, ids["K1.1"]);
        await CoupledAsync(admin, nordlys, ids["K1.2"]);
        await CoupledAsync(admin, formula, ids["K2.1"]);

        // En ny udgave af kortet: K2.1 udgår, og K1.2 får et barn — koblingerne bevares og står i filen.
        await admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1", "Optagelse", "K1", null),
            ("K1.2", "Undervisning", "K1", null),
            ("K1.2.1", "Holdundervisning", "K1.2", null),
            ("K2", "Forskning", null, null)));
        var before = await admin.GetSystemAsync(kompas.Id);

        var export = await admin.ExportCouplingsAsync();
        var preview = await admin.CouplingDryRunAsync(export);

        Assert.Empty(preview.Errors);
        Assert.Empty(preview.Warnings);
        Assert.Empty(preview.Changes);
        Assert.Equal(new CouplingImportSummary(
                SystemsInFile: 5, SystemsChanged: 0, Added: 0, Removed: 0, Unchanged: 5, SystemsCleared: 0,
                LargeRemoval: false, SystemsChangedSinceExport: 0, SystemsNotInFile: 0, IgnoredEdits: 0),
            preview.Summary);
        Assert.NotNull(preview.Fingerprint);

        var result = await admin.ImportCouplingsAsync(export);
        Assert.True(result.Committed);
        Assert.Empty(result.Changes);
        var after = await admin.GetSystemAsync(kompas.Id);
        Assert.Equal((before.UpdatedAt, before.Version), (after.UpdatedAt, after.Version));
    }

    [Fact]
    public async Task Filen_bestemmer_koblingerne_for_systemerne_i_den_og_roerer_ikke_de_andre()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var hr = await CoupledAsync(admin, await admin.CreateSystemAsync("HR", nordlys.Id), ids["K1.1"]);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"], ids["K2.1"]);
        var ugle = await CoupledAsync(admin, await admin.CreateSystemAsync("Ugle"), ids["K1.2"]);

        // Kompas: K1.1 overskrives med K1.2, K2.1 bliver. HR: koden tømmes (alle koblinger fjernes). Ugle: ikke i filen.
        var file = CouplingFile(
            (kompas.Id, "Kompas", "K1.2"),
            (kompas.Id, "Kompas", "K2.1"),
            (hr.Id, "Nordlys > HR", null));
        var preview = await admin.CouplingDryRunAsync(file);

        Assert.Empty(preview.Errors);
        Assert.Equal(
        [
            (CouplingChangeKind.Fjernes, "Kompas", "K1.1"),
            (CouplingChangeKind.Fjernes, "Nordlys › HR", "K1.1"),
            (CouplingChangeKind.Tilfoejes, "Kompas", "K1.2"),
        ],
            Changes(preview));
        Assert.Equal(new CouplingImportSummary(
                SystemsInFile: 2, SystemsChanged: 2, Added: 1, Removed: 2, Unchanged: 1, SystemsCleared: 1,
                LargeRemoval: true, SystemsChangedSinceExport: 0, SystemsNotInFile: 1, IgnoredEdits: 0),
            preview.Summary);
        Assert.Equal([new CoupledSystem(ugle.Id, "Ugle")], preview.NotInFile);

        var result = await admin.ImportCouplingsAsync(file);

        Assert.True(result.Committed);
        Assert.Equal(["K1.2", "K2.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
        Assert.Empty(Codes(await admin.GetSystemAsync(hr.Id)));
        var ugleAfter = await admin.GetSystemAsync(ugle.Id);
        Assert.Equal(["K1.2"], Codes(ugleAfter));
        Assert.Equal(ugle.Version, ugleAfter.Version);
    }

    [Fact]
    public async Task Importen_aendrer_systemet_men_ikke_bekraeftelsen_og_en_aaben_formular_faar_konflikt()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var opened = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"]);
        app.Time.Advance(TimeSpan.FromHours(1));

        await admin.ImportCouplingsAsync(CouplingFile((opened.Id, "Kompas", "K2.1")));

        var after = await admin.GetSystemAsync(opened.Id);
        Assert.Equal(["K2.1"], Codes(after));
        Assert.True(after.UpdatedAt > opened.UpdatedAt);
        Assert.Equal(opened.LastConfirmedAt, after.LastConfirmedAt);

        // Formularen var åben før importen og sender sin gamle liste: den må ikke rulle importen stille tilbage.
        var stale = await admin.PutSystemAsync(opened.Id, opened.ToWrite() with { CapabilityIds = [ids["K1.1"]] });
        await stale.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleVersionType, await stale.ProblemTypeAsync());
        Assert.Equal(["K2.1"], Codes(await admin.GetSystemAsync(opened.Id)));
    }

    [Fact]
    public async Task Fejl_i_filens_raekker_vises_med_linje_og_kolonne_og_intet_gemmes()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K2.1"]);

        var file = CouplingFile(
        [
            Row("", "Kompas", "K1.1"),
            Row("ikke-et-id", "", "K1.1"),
            Row(kompas.Id.ToString(), "Kompas", "K1.1"),
            Row(kompas.Id.ToString(), "", "k1.1"),
        ]);
        var preview = await admin.CouplingDryRunAsync(file);

        Assert.Equal(
        [
            new ImportRowError(2, "SystemId", "SystemId mangler. Kopiér en række fra eksporten, eller find id'et med \"Hent systemliste (CSV)\"."),
            new ImportRowError(3, "SystemId", "\"ikke-et-id\" er ikke et gyldigt SystemId."),
            new ImportRowError(5, "Kode", "k1.1 står også for samme system i linje 4."),
        ],
            preview.Errors);
        Assert.Null(preview.Fingerprint);
        Assert.Empty(preview.Changes);

        var commit = await admin.PostCouplingImportAsync(file, dryRun: false, "x");
        await commit.ExpectAsync(HttpStatusCode.BadRequest);
        Assert.Equal(["K2.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    [Fact]
    public async Task Fejl_mod_registret_vises_med_linje_og_kolonne_og_intet_gemmes()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K2.1"]);
        var unknown = Guid.NewGuid();

        var preview = await admin.CouplingDryRunAsync(CouplingFile(
            (unknown, "Slettet", "K1.1"),
            (kompas.Id, "Kompass", "K2.1"),
            (kompas.Id, "", "K9"),
            (kompas.Id, "", "K1")));

        Assert.Equal(
        [
            new ImportRowError(2, "SystemId", $"Systemet med id {unknown} findes ikke i registret (slettet?). Fjern rækkerne, eller hent en ny eksport."),
            new ImportRowError(3, "FuldtNavn", "FuldtNavn er \"Kompass\", men systemet hedder nu \"Kompas\". Ret navnet, eller tøm cellen."),
            new ImportRowError(4, "Kode", "Koden \"K9\" findes ikke i kortet. Koderne står i \"Hent kortet (CSV)\"."),
            new ImportRowError(5, "Kode", "\"K1 Uddannelse\" har underkapabiliteter — vælg den, der passer bedst."),
        ],
            preview.Errors);
        Assert.Null(preview.Fingerprint);
        Assert.Equal(["K2.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    [Theory]
    [InlineData("", "Filen er tom.")]
    [InlineData("Kode;Navn;ForælderKode;Beskrivelse\r\nK1;Uddannelse;;\r\n",
        "Første linje skal være præcis: SystemId;FuldtNavn;Kode;Kapabilitet;Sti;Forælder;Status;Type;ForvaltendeTeam;Forretningsejer;Systemejer;SidstBekræftet;SidstÆndret;Overlap;SystemerDerTæller;AntalPlanlagte;TællerIkkeMed;DelesMed;BørFlyttes;Systembeskrivelse.")]
    [InlineData("SystemId;FuldtNavn;Kode;Kapabilitet;Sti;Forælder;Status;Type;ForvaltendeTeam;Forretningsejer;Systemejer;SidstBekræftet;SidstÆndret;Overlap;SystemerDerTæller;AntalPlanlagte;TællerIkkeMed;DelesMed;BørFlyttes;Systembeskrivelse\r\n",
        "Filen indeholder ingen rækker. Hent koblingerne, ret dem i Excel, og indlæs filen igen.")]
    public async Task En_fil_uden_raekker_eller_med_forkerte_overskrifter_afvises(string csv, string message)
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);

        var preview = await admin.CouplingDryRunAsync(Text(csv));

        Assert.Equal([new ImportRowError(1, null, message)], preview.Errors);
    }

    [Fact]
    public async Task En_uaendret_kobling_til_en_udgaaet_kapabilitet_bevares_men_en_ny_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K2.1"]);
        var ugle = await admin.CreateSystemAsync("Ugle");
        await admin.ImportAsync(File(("K1", "Uddannelse", null, null), ("K1.1", "Optagelse", "K1", null)));

        var kept = await admin.CouplingDryRunAsync(CouplingFile((kompas.Id, "Kompas", "K2.1")));
        var added = await admin.CouplingDryRunAsync(CouplingFile((ugle.Id, "Ugle", "K2.1")));

        Assert.Empty(kept.Errors);
        Assert.Empty(kept.Changes);
        Assert.Equal([new ImportRowError(2, "Kode", "\"K2.1 Laboratorier\" er udgået af kortet og kan ikke vælges.")], added.Errors);
    }

    [Fact]
    public async Task Hoejst_100_koblinger_pr_system()
    {
        await using var app = await TestApp.StartAsync();
        var leaves = Enumerable.Range(1, CapabilityRules.MaxCouplingsPerSystem + 1).Select(i => $"B{i}").ToList();
        var (admin, _) = await StartAsync(app,
            File([("B", "Blade", null, null), .. leaves.Select(code => (code, $"Blad {code}", (string?)"B", (string?)null))]));
        var kompas = await admin.CreateSystemAsync("Kompas");

        var atLimit = await admin.CouplingDryRunAsync(CouplingFile(
            leaves.Take(CapabilityRules.MaxCouplingsPerSystem).Select(code => (kompas.Id, (string?)"Kompas", (string?)code)).ToArray()));
        var overLimit = await admin.CouplingDryRunAsync(CouplingFile(
            leaves.Select(code => (kompas.Id, (string?)"Kompas", (string?)code)).ToArray()));

        Assert.Empty(atLimit.Errors);
        Assert.Equal([new ImportRowError(102, "Kode", "Kompas har 101 koblinger i filen; højst 100.")], overLimit.Errors);
    }

    [Fact]
    public async Task Rettede_kolonner_der_ikke_indlaeses_giver_en_advarsel_men_ikke_en_fejl()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync(NewSystem("Kompas", type: SystemType.Saas)), ids["K1.1"]);
        var rows = CouplingRows(await admin.ExportCouplingsAsync());

        rows[0]["Status"] = "Udfases";
        rows[0]["Forretningsejer"] = "Bo Bogholder";
        rows[0]["Type"] = ""; // En tømt celle er ikke en rettelse (fx en række, brugeren selv har skrevet).
        var result = await admin.ImportCouplingsAsync(CouplingFile(rows));

        Assert.Equal(
        [
            new ImportRowError(2, "Status", "Status er rettet i filen, men indlæses ikke. Ret det på systemsiden."),
            new ImportRowError(2, "Forretningsejer", "Forretningsejer er rettet i filen, men indlæses ikke. Ret det på systemsiden."),
        ],
            result.Warnings);
        Assert.Equal(1, result.Summary.IgnoredEdits);
        Assert.Equal(LifecycleStatus.IDrift, (await admin.GetSystemAsync(kompas.Id)).LifecycleStatus);
    }

    [Fact]
    public async Task Et_system_aendret_efter_eksporten_markeres_men_ikke_et_der_kun_er_bekraeftet()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"], ids["K2.1"]);
        var ugle = await CoupledAsync(admin, await admin.CreateSystemAsync("Ugle"), ids["K1.1"], ids["K2.1"]);
        var rows = CouplingRows(await admin.ExportCouplingsAsync());

        // Efter eksporten: Kompas redigeres (fx en forvalter tilføjer noget), Ugle bekræftes kun.
        app.Time.Advance(TimeSpan.FromHours(1));
        await (await admin.PutSystemAsync(kompas.Id, kompas.ToWrite() with { Description = "Ny" })).ExpectAsync(HttpStatusCode.OK);
        await (await admin.PostAsJsonAsync($"/api/systems/{ugle.Id}/confirm", new ConfirmSystemRequest(ugle.Version), TestApp.Json))
            .ExpectAsync(HttpStatusCode.OK);
        foreach (var row in rows.Where(r => r["Kode"] == "K2.1"))
        {
            row["Kode"] = "";
        }

        var preview = await admin.CouplingDryRunAsync(CouplingFile(rows));

        Assert.Equal(
            [("Kompas", "K2.1", true), ("Ugle", "K2.1", false)],
            preview.Changes.Select(c => (c.System.Name, c.Code, c.ChangedSinceExport)));
        Assert.Equal(1, preview.Summary.SystemsChangedSinceExport);
    }

    [Theory]
    [InlineData(1, false)] // 1 af 5 = 20 % — ikke OVER en femtedel. Med >= ville den være true.
    [InlineData(2, true)] // 2 af 5 = 40 %.
    public async Task En_stor_fjernelse_paa_systemerne_i_filen_giver_en_advarsel(int remove, bool large)
    {
        await using var app = await TestApp.StartAsync();
        string[] codes = ["K1.1", "K1.2", "K1.3", "K1.4", "K1.5"];
        var (admin, ids) = await StartAsync(app,
            File([("K1", "Uddannelse", null, null), .. codes.Select(c => (c, $"Navn {c}", (string?)"K1", (string?)null))]));
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), codes.Select(c => ids[c]).ToArray());

        var preview = await admin.CouplingDryRunAsync(CouplingFile(
            codes.Skip(remove).Select(code => (kompas.Id, (string?)"Kompas", (string?)code)).ToArray()));

        Assert.Equal((remove, codes.Length - remove, large),
            (preview.Summary.Removed, preview.Summary.Unchanged, preview.Summary.LargeRemoval));
    }

    [Fact]
    public async Task En_laeser_kan_hverken_koere_toer_koersel_eller_importere()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"]);
        var reader = await app.ClientFor(TestUsers.Reader);
        var file = CouplingFile((kompas.Id, "Kompas", null));

        await (await reader.PostCouplingImportAsync(file, dryRun: true)).ExpectAsync(HttpStatusCode.Forbidden);
        var fingerprint = (await admin.CouplingDryRunAsync(file)).Fingerprint;
        await (await reader.PostCouplingImportAsync(file, dryRun: false, fingerprint)).ExpectAsync(HttpStatusCode.Forbidden);

        Assert.Equal(["K1.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    [Fact]
    public async Task En_aendret_kobling_efter_toer_koerslen_afviser_importen()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"]);
        var file = CouplingFile((kompas.Id, "Kompas", "K1.1"), (kompas.Id, "Kompas", "K1.2"));
        var fingerprint = (await admin.CouplingDryRunAsync(file)).Fingerprint;

        await CoupledAsync(admin, kompas, ids["K1.1"], ids["K2.1"]); // Formularen: K2.1 tilføjes efter tør-kørslen.

        var response = await admin.PostCouplingImportAsync(file, dryRun: false, fingerprint);
        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleDryRunType, await response.ProblemTypeAsync());
        Assert.Contains("Koblingerne eller systemerne i filen", await response.ProblemDetailAsync(), StringComparison.Ordinal);
        Assert.Equal(["K1.1", "K2.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    [Fact]
    public async Task En_kapabilitet_der_faar_boern_efter_toer_koerslen_afviser_importen()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var kompas = await admin.CreateSystemAsync("Kompas");
        var file = CouplingFile((kompas.Id, "Kompas", "K1.2"));
        var fingerprint = (await admin.CouplingDryRunAsync(file)).Fingerprint;

        await admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1", "Optagelse", "K1", null),
            ("K1.2", "Undervisning", "K1", null),
            ("K1.2.1", "Holdundervisning", "K1.2", null),
            ("K2", "Forskning", null, null),
            ("K2.1", "Laboratorier", "K2", null)));

        var response = await admin.PostCouplingImportAsync(file, dryRun: false, fingerprint);
        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleDryRunType, await response.ProblemTypeAsync());
        Assert.Empty(Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    /// <summary>
    /// En kobling er ved at blive gemt (formularens lås), mens importen gennemføres: importen skal vente og se
    /// koblingen — tør-kørslen passer ikke længere (409) — i stedet for at fjerne den.
    /// </summary>
    [Fact]
    public async Task En_import_venter_paa_en_samtidig_kobling_og_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var kompas = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"]);
        var file = CouplingFile((kompas.Id, "Kompas", "K1.1"), (kompas.Id, "Kompas", "K1.2"));
        var fingerprint = (await admin.CouplingDryRunAsync(file)).Fingerprint;

        await using var coupling = new NpgsqlConnection(app.ConnectionString);
        await coupling.OpenAsync();
        await using var transaction = await coupling.BeginTransactionAsync();
        await Sql(coupling, transaction, "LOCK TABLE ea.system_capabilities IN ROW EXCLUSIVE MODE");
        await Sql(coupling, transaction,
            $"INSERT INTO ea.system_capabilities (system_id, capability_id) VALUES ('{kompas.Id}', '{ids["K2.1"]}')");

        var import = admin.PostCouplingImportAsync(file, dryRun: false, fingerprint);
        await WaitForBlockedLockAsync(coupling, transaction);
        await transaction.CommitAsync();

        var response = await import;
        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleDryRunType, await response.ProblemTypeAsync());
        Assert.Equal(["K1.1", "K2.1"], Codes(await admin.GetSystemAsync(kompas.Id)));
    }

    /// <summary>
    /// Sletning tager koblingslåsen FØR systemrækken (samme rækkefølge som importen): mens en import holder låsen,
    /// venter sletningen uden at have låst systemet — ellers kunne importens opdatering af systemet give en deadlock.
    /// </summary>
    [Fact]
    public async Task En_sletning_venter_paa_en_import_uden_at_have_laast_systemet()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Tomrum");

        await using var import = new NpgsqlConnection(app.ConnectionString);
        await import.OpenAsync();
        await using var transaction = await import.BeginTransactionAsync();
        await Sql(import, transaction, "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE");

        var delete = admin.DeleteAsync($"/api/systems/{system.Id}");
        await WaitForBlockedLockAsync(import, transaction);
        // Importen kan stadig låse systemrækken (som når den opdaterer systemet) — NOWAIT fejler, hvis sletningen har den.
        await Sql(import, transaction, $"SELECT 1 FROM ea.systems WHERE id = '{system.Id}' FOR UPDATE NOWAIT");
        await transaction.CommitAsync();

        await (await delete).ExpectAsync(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// En læser, der prøver at slette, mens en import holder låsen, afvises straks — adgangstjekket ligger før låsen,
    /// så en afvist bruger ikke kan stå i kø ved den og binde forbindelser (Security Reviewer, 3d).
    /// </summary>
    [Fact]
    public async Task En_laeser_afvises_straks_ogsaa_mens_en_import_holder_laasen()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Tomrum");
        var reader = await app.ClientFor(TestUsers.Reader);

        await using var import = new NpgsqlConnection(app.ConnectionString);
        await import.OpenAsync();
        await using var transaction = await import.BeginTransactionAsync();
        await Sql(import, transaction, "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE");

        var delete = reader.DeleteAsync($"/api/systems/{system.Id}");
        var first = await Task.WhenAny(delete, Task.Delay(TimeSpan.FromSeconds(10)));
        await transaction.CommitAsync();

        Assert.Same(delete, first);
        await (await delete).ExpectAsync(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// En formular, der gemmer samme kobling, som en samtidig import lige har lavet, skal få "ændret af en anden"
    /// (409) — ikke en 500 fra et dobbelt INSERT. Koblingerne læses derfor efter låsen (Security Reviewer, 3d).
    /// </summary>
    [Fact]
    public async Task En_formular_gemt_under_en_import_faar_konflikt_ikke_en_serverfejl()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, ids) = await StartAsync(app);
        var opened = await CoupledAsync(admin, await admin.CreateSystemAsync("Kompas"), ids["K1.1"]);

        await using var import = new NpgsqlConnection(app.ConnectionString);
        await import.OpenAsync();
        await using var transaction = await import.BeginTransactionAsync();
        await Sql(import, transaction, "LOCK TABLE ea.capabilities, ea.system_capabilities IN SHARE ROW EXCLUSIVE MODE");
        await Sql(import, transaction,
            $"INSERT INTO ea.system_capabilities (system_id, capability_id) VALUES ('{opened.Id}', '{ids["K1.2"]}')");
        await Sql(import, transaction, $"UPDATE ea.systems SET updated_at = now() WHERE id = '{opened.Id}'");

        var put = admin.PutSystemAsync(opened.Id, opened.ToWrite() with { CapabilityIds = [ids["K1.1"], ids["K1.2"]] });
        await WaitForBlockedLockAsync(import, transaction);
        await transaction.CommitAsync();

        var response = await put;
        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleVersionType, await response.ProblemTypeAsync());
        Assert.Equal(["K1.1", "K1.2"], Codes(await admin.GetSystemAsync(opened.Id)));
    }

    /// <summary>En sletning, der ventede, mens nogen gemte systemet, sletter ikke det, brugeren ikke så (409, ikke 500).</summary>
    [Fact]
    public async Task En_sletning_af_et_system_der_aendres_imens_giver_konflikt()
    {
        await using var app = await TestApp.StartAsync();
        var (admin, _) = await StartAsync(app);
        var system = await admin.CreateSystemAsync("Tomrum");

        await using var form = new NpgsqlConnection(app.ConnectionString);
        await form.OpenAsync();
        await using var transaction = await form.BeginTransactionAsync();
        await Sql(form, transaction, $"UPDATE ea.systems SET description = 'Gemt imens' WHERE id = '{system.Id}'");

        var delete = admin.DeleteAsync($"/api/systems/{system.Id}");
        await WaitForBlockedBackendAsync(form, transaction);
        await transaction.CommitAsync();

        var response = await delete;
        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleVersionType, await response.ProblemTypeAsync());
        Assert.Equal("Gemt imens", (await admin.GetSystemAsync(system.Id)).Description);
    }

    [Fact]
    public void En_fil_med_flere_end_MaxRows_raekker_afvises()
    {
        var id = Guid.NewGuid();
        var file = CouplingFile(Enumerable.Range(0, CouplingImport.MaxRows + 1).Select(_ => (id, (string?)null, (string?)null)).ToArray());

        var (rows, errors) = CouplingImport.Parse(Csv.Read(file, maxRecords: CouplingImport.MaxRows + 2));

        Assert.Empty(rows);
        Assert.Equal(
            [new ImportRowError(1, null, "Filen har flere end 20000 rækker; højst 20000 kan indlæses ad gangen. Del den op efter system.")],
            errors);
    }
}
