using System.Net;
using System.Text;
using Ea.Api.Capabilities;
using Ea.Api.Common;
using Ea.Api.Tests.Infrastructure;
using static Ea.Api.Tests.Infrastructure.TestApiCapabilities;

namespace Ea.Api.Tests.Capabilities;

/// <summary>Import af kapabilitetskortet: filen er HELE kortet, tør-kørslen gemmer intet, og fejl stopper alt.</summary>
public sealed class CapabilityImportTests
{
    /// <summary>Tallene uden koblinger (intet udgår, genaktiveres eller skal flyttes).</summary>
    private static CapabilityImportSummary Summary(int New, int Changed, int Removed, int Unchanged, int CurrentTotal) =>
        new(New, Changed, Removed, Retired: 0, Reactivated: 0, Unchanged, CurrentTotal, RemovedFromMap: Removed, LargeRemoval: false,
            CouplingsToMove: 0, SystemsToMove: 0);

    private static readonly byte[] Model = File(
        ("K1", "Uddannelse", null, null),
        ("K1.1", "Studieadministration", "K1", "Fra ansøgning til bevis"),
        ("K1.1.1", "Optagelse", "K1.1", null),
        ("K1.2", "Undervisning", "K1", null),
        ("K2", "Forskning", null, null),
        ("K2.1", "Laboratorier", "K2", null));

    [Fact]
    public async Task Toer_koersel_viser_aendringerne_og_gemmer_intet()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var preview = await admin.DryRunAsync(Model);

        Assert.False(preview.Committed);
        Assert.Empty(preview.Errors);
        Assert.Equal(Summary(New: 6, Changed: 0, Removed: 0, Unchanged: 0, CurrentTotal: 0),
            preview.Summary);
        Assert.All(preview.Changes, c => Assert.Equal(CapabilityChangeKind.Ny, c.Kind));
        Assert.Equal(new CapabilitySnapshot("K1.1", "Studieadministration", "K1", "Fra ansøgning til bevis"),
            preview.Changes.Single(c => c.Code == "K1.1").After);
        Assert.NotNull(preview.Fingerprint);
        Assert.Empty((await admin.CapabilitiesAsync()).Items);
    }

    [Fact]
    public async Task Gennemfoert_import_giver_traeet_i_visningsraekkefoelge()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var result = await admin.ImportAsync(Model);

        Assert.True(result.Committed);
        Assert.Equal(6, result.Summary.New);
        Assert.Equal(
            [
                (0, "K1", "Uddannelse", null),
                (1, "K1.1", "Studieadministration", "K1"),
                (2, "K1.1.1", "Optagelse", "K1.1"),
                (1, "K1.2", "Undervisning", "K1"),
                (0, "K2", "Forskning", null),
                (1, "K2.1", "Laboratorier", "K2"),
            ],
            await admin.TreeAsync());
        Assert.Equal("Fra ansøgning til bevis", (await admin.CapabilitiesAsync()).Items.Single(i => i.Code == "K1.1").Description);
    }

    [Fact]
    public async Task Soeskende_ordnes_efter_kode_med_tal_som_tal_uanset_filens_raekkefoelge()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        await admin.ImportAsync(File(("K1.10", "Ti", "K1", null), ("K1", "En", null, null), ("K1.2", "To", "K1", null)));

        Assert.Equal(["K1", "K1.2", "K1.10"], (await admin.TreeAsync()).Select(n => n.Code));
    }

    [Fact]
    public async Task Samme_fil_to_gange_giver_nul_aendringer()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);

        var again = await admin.DryRunAsync(Model);

        Assert.Equal(Summary(New: 0, Changed: 0, Removed: 0, Unchanged: 6, CurrentTotal: 6), again.Summary);
        Assert.Empty(again.Changes);
    }

    [Fact]
    public async Task Eksport_indlaest_igen_giver_nul_aendringer_ogsaa_med_formeltegn_og_apostroffer()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(File(
            ("K1", "-Minus først", null, "=ikke en formel"),
            ("K1.1", "'Citeret'", "K1", "tekst,'=1 og 100,- kr."),
            ("K1.2", "Semikolon; og \"citat\"", "K1", "Linje 1\r\nLinje 2")));

        var export = await admin.GetAsync("/api/capabilities/export.csv");
        await export.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("kapabiliteter-2026-09-01.csv", export.Content.Headers.ContentDisposition?.FileNameStar);
        var again = await admin.DryRunAsync(await export.Content.ReadAsByteArrayAsync());

        Assert.Empty(again.Errors);
        Assert.Equal(Summary(New: 0, Changed: 0, Removed: 0, Unchanged: 3, CurrentTotal: 3), again.Summary);
    }

    [Fact]
    public async Task Aendringer_vises_med_foer_og_efter_og_det_der_slettes_staar_foerst()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);

        // K1.1.1 flyttes til K1.2, K2.1 omdøbes, K1.1 slettes (dets barn er flyttet), K3 er ny.
        var preview = await admin.DryRunAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1.1", "Optagelse", "K1.2", null),
            ("K1.2", "Undervisning", "K1", null),
            ("K2", "Forskning", null, null),
            ("K2.1", "Laboratoriedrift", "K2", null),
            ("K3", "Understøttende", null, null)));

        Assert.Equal(Summary(New: 1, Changed: 2, Removed: 1, Unchanged: 3, CurrentTotal: 6),
            preview.Summary);
        Assert.Equal(
            [
                (CapabilityChangeKind.Slettes, "K1.1"),
                (CapabilityChangeKind.Aendret, "K1.1.1"),
                (CapabilityChangeKind.Aendret, "K2.1"),
                (CapabilityChangeKind.Ny, "K3"),
            ],
            preview.Changes.Select(c => (c.Kind, c.Code)));
        var moved = preview.Changes.Single(c => c.Code == "K1.1.1");
        Assert.Equal(("K1.1", "K1.2"), (moved.Before!.ParentCode, moved.After!.ParentCode));
        Assert.Null(preview.Changes.Single(c => c.Code == "K1.1").After);
    }

    [Fact]
    public async Task En_forælder_kan_slettes_i_samme_import_som_dens_barn_flyttes_og_et_undertrae_forsvinder()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);

        // K1.1 slettes, mens dets barn K1.1.1 flyttes; hele K2-undertræet slettes; K1.2 får en ny forælder, K9.
        await admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1.1", "Optagelse", "K1", null),
            ("K9", "Nyt", null, null),
            ("K1.2", "Undervisning", "K9", null)));

        Assert.Equal(
            [(0, "K1", "Uddannelse", null), (1, "K1.1.1", "Optagelse", "K1"), (0, "K9", "Nyt", null), (1, "K1.2", "Undervisning", "K9")],
            await admin.TreeAsync());
    }

    [Fact]
    public async Task Koden_er_noeglen_uden_hensyn_til_store_og_smaa_bogstaver()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);

        // Forælderkoden stavet med små bogstaver er ikke en ændring; koden selv med små bogstaver er (visningen).
        var preview = await admin.DryRunAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1", "Studieadministration", "k1", "Fra ansøgning til bevis"),
            ("k1.1.1", "Optagelse", "K1.1", null),
            ("K1.2", "Undervisning", "K1", null),
            ("K2", "Forskning", null, null),
            ("K2.1", "Laboratorier", "K2", null)));

        Assert.Equal((0, 1, 0), (preview.Summary.New, preview.Summary.Changed, preview.Summary.Removed));
        var changed = Assert.Single(preview.Changes);
        Assert.Equal(("K1.1.1", "k1.1.1"), (changed.Before!.Code, changed.After!.Code));
    }

    [Fact]
    public async Task En_delvis_fil_giver_en_tydelig_advarsel()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);

        // 6 → 5: én ud af seks (17 %) er under grænsen; 6 → 4: to ud af seks (33 %) er over.
        var small = await admin.DryRunAsync(File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", "Fra ansøgning til bevis"),
            ("K1.1.1", "Optagelse", "K1.1", null), ("K1.2", "Undervisning", "K1", null), ("K2", "Forskning", null, null)));
        var large = await admin.DryRunAsync(File(
            ("K1", "Uddannelse", null, null), ("K1.1", "Studieadministration", "K1", "Fra ansøgning til bevis"),
            ("K1.1.1", "Optagelse", "K1.1", null), ("K1.2", "Undervisning", "K1", null)));

        Assert.Equal((1, false), (small.Summary.Removed, small.Summary.LargeRemoval));
        Assert.Equal((2, true), (large.Summary.Removed, large.Summary.LargeRemoval));
    }

    [Fact]
    public async Task Et_forkert_fingeraftryk_afvises_og_intet_gemmes()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var preview = await admin.DryRunAsync(Model);

        var response = await admin.PostImportAsync(Model, dryRun: false, fingerprint: new string('0', preview.Fingerprint!.Length));

        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(Problems.StaleDryRunType, await response.ProblemTypeAsync());
        Assert.Contains("Kør tør-kørslen igen", await response.ProblemDetailAsync(), StringComparison.Ordinal);
        Assert.Empty((await admin.CapabilitiesAsync()).Items);
    }

    [Fact]
    public async Task Er_kortet_aendret_siden_toer_koerslen_afvises_importen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var preview = await admin.DryRunAsync(Model);

        // En anden importerer imellem — tør-kørslen viste "6 nye", men nu ville det være "0 nye".
        await admin.ImportAsync(File(("K1", "Uddannelse", null, null)));
        var response = await admin.PostImportAsync(Model, dryRun: false, preview.Fingerprint);

        await response.ExpectAsync(HttpStatusCode.Conflict);
        Assert.Equal(["K1"], (await admin.TreeAsync()).Select(n => n.Code));
    }

    [Fact]
    public async Task Gennemfoerelse_uden_fingeraftryk_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var response = await admin.PostImportAsync(Model, dryRun: false);

        await response.ExpectAsync(HttpStatusCode.BadRequest);
        Assert.Empty((await admin.CapabilitiesAsync()).Items);
    }

    public static TheoryData<string, int, string> BadFiles() => new()
    {
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;;\r\nk1;To;;\r\n", 3, "Koden \"k1\" står også i linje 2." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;K9;\r\n", 2, "Forælderen \"K9\" findes ikke i filen." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;K1;\r\n", 2, "Forælder-kæden går i ring: K1 → K1." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nA;1;;\r\nB;2;A;\r\nC;3;B;\r\nD;4;C;\r\nE;5;D;\r\n", 6,
            "Kapabiliteten ligger på niveau 5, men kortet må højst have 4 niveauer." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\n;Uden kode;;\r\n", 2, "Koden mangler." },
        { $"Kode;Navn;ForælderKode;Beskrivelse\r\n{new string('K', 41)};En;;\r\n", 2, "Koden er længere end 40 tegn." },
        { $"Kode;Navn;ForælderKode;Beskrivelse\r\nK1;{new string('n', 201)};;\r\n", 2, "Navnet er længere end 200 tegn." },
        { $"Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;;{new string('b', 4001)}\r\n", 2, "Beskrivelsen er længere end 4000 tegn." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nK1;;;\r\n", 2, "Navnet mangler." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\nK1;En;;;ekstra\r\n", 2,
            "Linjen har 5 felter, men skal have 4. Står der et semikolon i en tekst uden citationstegn?" },
        { "Kode;Navn;Forælder;Beskrivelse\r\nK1;En;;\r\n", 1, "Første linje skal være præcis: Kode;Navn;ForælderKode;Beskrivelse." },
        { "Kode;Navn;ForælderKode;Beskrivelse\r\n", 1,
            "Filen indeholder ingen kapabiliteter. En import erstatter hele kortet, så en tom fil ville slette det." },
        { "", 1, "Filen er tom." },
    };

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task Fejl_i_filen_vises_med_linjenummer_og_intet_gemmes(string csv, int line, string message)
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var preview = await admin.DryRunAsync(Text(csv));

        var error = Assert.Single(preview.Errors);
        Assert.Equal((line, message), (error.Line, error.Message));
        Assert.Null(preview.Fingerprint);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public async Task En_ring_meldes_paa_hver_af_ringens_raekker_men_ikke_paa_raekker_der_blot_fører_ind_i_den()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        // K0 og K3 fører ind i ringen K1 ↔ K2 — K0 står FØR ringen, så ringen findes via den.
        var preview = await admin.DryRunAsync(Text(
            "Kode;Navn;ForælderKode;Beskrivelse\r\nK0;Nul;K1;\r\nK1;En;K2;\r\nK2;To;K1;\r\nK3;Tre;K1;\r\n"));

        Assert.Equal(
            [(3, "Forælder-kæden går i ring: K1 → K2 → K1."), (4, "Forælder-kæden går i ring: K2 → K1 → K2.")],
            preview.Errors.Select(e => (e.Line, e.Message)));
    }

    /// <summary>
    /// Træ-tjekket er lineært: før rettelsen tog en kæde på 2000 rækker ~25 s og en ring på 2000 ~75 s (kubisk);
    /// nu skal begge være langt under 2 s. Ringens besked afkortes, og antallet af fejl er begrænset.
    /// </summary>
    [Fact]
    public void Lange_kaeder_og_store_ringe_tjekkes_hurtigt_og_giver_korte_beskeder()
    {
        var code = new string('K', 35);
        var chain = Enumerable.Range(1, 5000)
            .Select(i => new CsvRow(i + 1, [$"{code}{i}", "Navn", i == 1 ? "" : $"{code}{i - 1}", ""]))
            .ToList();
        var ring = Enumerable.Range(1, 2000)
            .Select(i => new CsvRow(i + 1, [$"{code}{i}", "Navn", $"{code}{(i % 2000) + 1}", ""]))
            .ToList();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (_, chainErrors) = CapabilityImport.Parse(new CsvReadResult(CapabilityCsv.Header, chain, null));
        var (_, ringErrors) = CapabilityImport.Parse(new CsvReadResult(CapabilityCsv.Header, ring, null));
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Træ-tjekket tog {watch.Elapsed}.");
        Assert.Equal(CsvImport.MaxErrors, chainErrors.Count);
        Assert.Equal("Kapabiliteten ligger på niveau 5, men kortet må højst have 4 niveauer.", chainErrors[0].Message);
        Assert.Equal(CsvImport.MaxErrors, ringErrors.Count);
        Assert.All(ringErrors, e => Assert.True(e.Message.Length < 300, e.Message));
        Assert.EndsWith("→ …", ringErrors[0].Message.TrimEnd('.'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Felter_paa_praecis_graensen_accepteres()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        // 40/200/4000 tegn er lovligt; 41/201/4001 er fejl (BadFiles). Tallene er skrevet ud med vilje.
        var preview = await admin.DryRunAsync(File((new string('K', 40), new string('n', 200), null, new string('b', 4000))));

        Assert.Empty(preview.Errors);
        Assert.Equal(1, preview.Summary.New);
    }

    /// <summary>
    /// Kapabiliteter har ingen versionskolonne; låsen i gennemførelsen er det eneste, der forhindrer to samtidige
    /// imports i at skrive oven i hinanden. Uden den: dublet-fejl (500) eller to "vellykkede" imports.
    /// </summary>
    [Fact]
    public async Task Samtidige_gennemfoerelser_af_samme_toer_koersel_giver_praecis_en_import()
    {
        await using var app = await TestApp.StartAsync();
        var clients = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => app.ClientFor(TestUsers.Admin)));
        var fingerprint = (await clients[0].DryRunAsync(Model)).Fingerprint;

        var responses = await Task.WhenAll(clients.Select(c => c.PostImportAsync(Model, dryRun: false, fingerprint)));

        Assert.Equal(
            [(HttpStatusCode.OK, 1), (HttpStatusCode.Conflict, 7)],
            responses.GroupBy(r => r.StatusCode).Select(g => (g.Key, g.Count())).OrderBy(g => g.Key));
        Assert.Equal(6, (await clients[0].CapabilitiesAsync()).Items.Count);
    }

    [Fact]
    public async Task En_komma_separeret_fil_faar_en_forklaring()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var preview = await admin.DryRunAsync(Text("Kode,Navn,ForælderKode,Beskrivelse\r\nK1,En,,\r\n"));

        Assert.Contains("komma som skilletegn", Assert.Single(preview.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tomme_kolonner_til_sidst_fra_excel_accepteres()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var preview = await admin.DryRunAsync(Text("Kode;Navn;ForælderKode;Beskrivelse;;\r\nK1;En;;;;\r\nK2;To;K1\r\n"));

        Assert.Empty(preview.Errors);
        Assert.Equal(2, preview.Summary.New);
    }

    [Fact]
    public async Task En_fejl_i_raekke_150_stopper_hele_importen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var rows = Enumerable.Range(1, 149).Select(i => ($"N{i}", $"Ny {i}", (string?)null, (string?)null))
            .Append(("N150", "", null, null)) // Linje 151 (række 150): navnet mangler.
            .ToArray();
        var file = File(rows);

        var preview = await admin.DryRunAsync(file);
        var commit = await admin.PostImportAsync(file, dryRun: false, fingerprint: "vilkaarligt");

        Assert.Equal((151, "Navn"), (Assert.Single(preview.Errors).Line, preview.Errors[0].Column));
        await commit.ExpectAsync(HttpStatusCode.BadRequest);
        Assert.Equal(6, (await admin.CapabilitiesAsync()).Items.Count);
    }

    [Fact]
    public async Task En_fil_i_en_aeldre_tegnkodning_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var preview = await admin.DryRunAsync(Encoding.Latin1.GetBytes("Kode;Navn;ForælderKode;Beskrivelse\r\nK1;Økonomi;;\r\n"));

        var error = Assert.Single(preview.Errors);
        Assert.Equal(1, error.Line); // "ForælderKode" i overskriften er det første ugyldige tegn.
        Assert.Contains("UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Graensen_er_2_MB_praecis()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        const int twoMegabytes = 2 * 1024 * 1024; // Skrevet som tal, ikke konstanten — så en ændret grænse bliver rød.
        static byte[] Sized(int size)
        {
            var header = Encoding.UTF8.GetBytes("Kode;Navn;ForælderKode;Beskrivelse\r\nK1;");
            return [.. header, .. Enumerable.Repeat((byte)'x', size - header.Length)];
        }

        var atLimit = await admin.PostImportAsync(Sized(twoMegabytes), dryRun: true);
        var overLimit = await admin.PostImportAsync(Sized(twoMegabytes + 1), dryRun: true);

        await atLimit.ExpectAsync(HttpStatusCode.OK); // Læst og vurderet (navnet er for langt) — ikke afvist for størrelse.
        await overLimit.ExpectAsync(HttpStatusCode.BadRequest);
        Assert.Contains("større end 2 MB", string.Join(" ", (await overLimit.ValidationErrorsAsync())["file"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hoejst_5000_raekker_ad_gangen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        static byte[] Rows(int count) =>
            File(Enumerable.Range(1, count).Select(i => ($"R{i}", $"Række {i}", (string?)null, (string?)null)).ToArray());

        var atLimit = await admin.DryRunAsync(Rows(5000));
        var overLimit = await admin.DryRunAsync(Rows(5001));

        Assert.Empty(atLimit.Errors);
        Assert.Equal(5000, atLimit.Summary.New);
        Assert.Equal("Filen har 5001 rækker; højst 5000 kan indlæses ad gangen.", Assert.Single(overLimit.Errors).Message);
    }

    [Fact]
    public async Task Laeseren_kan_se_kortet_men_ikke_importere_hverken_toer_koersel_eller_gennemfoerelse()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var reader = await app.ClientFor(TestUsers.Reader);
        await admin.ImportAsync(Model);
        var fingerprint = (await admin.DryRunAsync(File(("K9", "Andet", null, null)))).Fingerprint;

        var asReader = await reader.CapabilitiesAsync();
        var dryRun = await reader.PostImportAsync(File(("K9", "Andet", null, null)), dryRun: true);
        var commit = await reader.PostImportAsync(File(("K9", "Andet", null, null)), dryRun: false, fingerprint);

        Assert.Equal(6, asReader.Items.Count);
        Assert.False(asReader.CanImport);
        Assert.True((await admin.CapabilitiesAsync()).CanImport);
        Assert.Equal(HttpStatusCode.Forbidden, dryRun.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
        Assert.Equal(6, (await admin.CapabilitiesAsync()).Items.Count);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/capabilities/export.csv")).StatusCode);
    }
}
