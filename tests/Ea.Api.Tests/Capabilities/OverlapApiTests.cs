using System.Net;
using Ea.Api.Capabilities;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using static Ea.Api.Tests.Infrastructure.TestApi;
using static Ea.Api.Tests.Infrastructure.TestApiCapabilities;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Overlap, dækning og "deles med" i API'et (3c-2): serverens vurdering er den samme på kortet, på systemsiden og i
/// CSV'en, og dæknings-tallet er præcis systemlistens "Ikke angivet".
/// </summary>
public sealed class OverlapApiTests
{
    private static readonly byte[] Model = File(
        ("K1", "Uddannelse", null, null),
        ("K1.1", "Optagelse", "K1", null),
        ("K1.2", "Undervisning", "K1", null),
        ("K2", "Forskning", null, null),
        ("K2.1", "Laboratorier", "K2", null));

    private sealed record Seed(HttpClient Admin, Dictionary<string, Guid> Ids, Dictionary<string, SystemDetail> Systems);

    /// <summary>
    /// K1.1: Nordlys og modulet HR (ét system), Kompas, Ugle (udfases), Regneark (lokal løsning), Rune (planlagt).
    /// K1.2: kun Kompas. K2.1: Kompas og Gammel (nedlagt).
    /// </summary>
    private static async Task<Seed> SeedAsync(TestApp app)
    {
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var ids = (await admin.CapabilitiesAsync()).Items.ToDictionary(i => i.Code, i => i.Id);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var systems = new Dictionary<string, SystemDetail>
        {
            ["Nordlys"] = nordlys,
            ["HR"] = await admin.CreateSystemAsync("HR", nordlys.Id),
            ["Kompas"] = await admin.CreateSystemAsync("Kompas"),
            ["Ugle"] = await admin.CreateSystemAsync(NewSystem("Ugle", LifecycleStatus.Udfases)),
            ["Regneark"] = await admin.CreateSystemAsync(NewSystem("Regneark", type: SystemType.LokalLoesning)),
            ["Rune"] = await admin.CreateSystemAsync(NewSystem("Rune", LifecycleStatus.Planlagt)),
            ["Gammel"] = await admin.CreateSystemAsync(NewSystem("Gammel", LifecycleStatus.Nedlagt)),
        };
        foreach (var name in new[] { "Nordlys", "HR", "Ugle", "Regneark", "Rune" })
        {
            await (await admin.CoupleAsync(systems[name], ids["K1.1"])).ExpectAsync(HttpStatusCode.OK);
        }

        await (await admin.CoupleAsync(systems["Kompas"], ids["K1.1"], ids["K1.2"], ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(systems["Gammel"], ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        return new Seed(admin, ids, systems);
    }

    [Fact]
    public async Task Kortet_viser_serverens_vurdering_med_medlemmer()
    {
        await using var app = await TestApp.StartAsync();
        var seed = await SeedAsync(app);

        var items = (await seed.Admin.CapabilitiesAsync()).Items.ToDictionary(i => i.Code);
        var k11 = items["K1.1"].Overlap!;

        Assert.Equal((2, 1, true, true), (k11.Counted, k11.Planned, k11.IsOverlap, k11.PlannedOnTopOfActive));
        Assert.Equal(
        [
            ("Kompas", (OverlapExclusion?)null),
            ("Nordlys", null),
            ("Nordlys › HR", null),
            ("Regneark", OverlapExclusion.LokalLoesning),
            ("Rune", OverlapExclusion.Planlagt),
            ("Ugle", OverlapExclusion.Udfases),
        ],
            k11.Members.Select(m => (m.Name, m.Exclusion)));
        Assert.Equal((1, false), (items["K2.1"].Overlap!.Counted, items["K2.1"].Overlap!.IsOverlap));
        // Grupper vurderes ikke.
        Assert.Null(items["K1"].Overlap);
    }

    [Fact]
    public async Task Kortet_og_CSVen_giver_samme_vurdering()
    {
        await using var app = await TestApp.StartAsync();
        var seed = await SeedAsync(app);

        var items = (await seed.Admin.CapabilitiesAsync()).Items.Where(i => i.Overlap is not null).ToList();
        var rows = CouplingRows(await seed.Admin.ExportCouplingsAsync());

        Assert.Equal(["K1.1", "K1.2", "K2.1"], items.Select(i => i.Code));
        foreach (var item in items)
        {
            var overlap = item.Overlap!;
            var coded = rows.Where(r => r["Kode"] == item.Code).ToList();
            Assert.Equal(overlap.Members.Count, coded.Count);
            Assert.All(coded, row =>
            {
                Assert.Equal(overlap.IsOverlap ? "Ja" : "Nej", row["Overlap"]);
                Assert.Equal(overlap.Counted.ToString(System.Globalization.CultureInfo.InvariantCulture), row["SystemerDerTæller"]);
                Assert.Equal(overlap.Planned.ToString(System.Globalization.CultureInfo.InvariantCulture), row["AntalPlanlagte"]);
                var member = overlap.Members.Single(m => m.Id.ToString() == row["SystemId"]);
                Assert.Equal(member.Exclusion?.ToString() ?? "", row["TællerIkkeMed"]);
            });
        }
    }

    [Fact]
    public async Task Systemsiden_viser_vurderingen_hvem_den_deles_med_og_om_systemet_selv_taeller()
    {
        await using var app = await TestApp.StartAsync();
        var seed = await SeedAsync(app);

        var hr = (await seed.Admin.GetSystemAsync(seed.Systems["HR"].Id)).Capabilities;
        var ugle = (await seed.Admin.GetSystemAsync(seed.Systems["Ugle"].Id)).Capabilities.Single();

        // HR's egen kobling og forælderens: samme vurdering, og hverken Nordlys eller HR "deles med" — de er ét system.
        Assert.Equal([("K1.1", (string?)null), ("K1.1", "Nordlys")], hr.Select(c => (c.Capability.Code, c.HeldBy?.Name)));
        Assert.All(hr, c =>
        {
            Assert.Equal((2, true), (c.Overlap!.Counted, c.Overlap.IsOverlap));
            Assert.Null(c.OwnExclusion);
            Assert.Equal(
                [("Kompas", (OverlapExclusion?)null), ("Regneark", OverlapExclusion.LokalLoesning), ("Rune", OverlapExclusion.Planlagt), ("Ugle", OverlapExclusion.Udfases)],
                c.SharedWith.Select(m => (m.Name, m.Exclusion)));
        });

        // Et system, der udfases: det tæller ikke selv med, og overlappet er mellem de andre.
        Assert.Equal(OverlapExclusion.Udfases, ugle.OwnExclusion);
        Assert.Equal(
            ["Kompas", "Nordlys", "Nordlys › HR", "Regneark", "Rune"],
            ugle.SharedWith.Select(m => m.Name));
    }

    [Fact]
    public async Task En_kobling_der_boer_flyttes_vurderes_ikke()
    {
        await using var app = await TestApp.StartAsync();
        var seed = await SeedAsync(app);
        await seed.Admin.ImportAsync(File(
            ("K1", "Uddannelse", null, null),
            ("K1.1", "Optagelse", "K1", null),
            ("K1.2", "Undervisning", "K1", null),
            ("K2", "Forskning", null, null)));

        var k21 = (await seed.Admin.GetSystemAsync(seed.Systems["Kompas"].Id)).Capabilities.Single(c => c.Capability.Code == "K2.1");

        Assert.Equal(MoveReason.Udgaaet, k21.Capability.MoveReason);
        Assert.Null(k21.Overlap);
        Assert.Null(k21.OwnExclusion);
        Assert.Empty(k21.SharedWith);
    }

    [Fact]
    public async Task Daekningen_er_praecis_systemlistens_Ikke_angivet_og_nedlagte_taeller_ikke()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var leaf = await admin.CapabilityIdAsync("K1.1");

        // A dækket af sit modul; B1 dækket af forælderen; C2 har kun en søskendes kobling (mangler); D mangler;
        // E og F er nedlagte og tæller hverken i Y eller på listen.
        var a = await admin.CreateSystemAsync("A");
        var a1 = await admin.CreateSystemAsync("A1", a.Id);
        var b = await admin.CreateSystemAsync("B");
        await admin.CreateSystemAsync("B1", b.Id);
        var c = await admin.CreateSystemAsync("C");
        var c1 = await admin.CreateSystemAsync("C1", c.Id);
        await admin.CreateSystemAsync("C2", c.Id);
        await admin.CreateSystemAsync("D");
        await admin.CreateSystemAsync(NewSystem("E", LifecycleStatus.Nedlagt));
        var f = await admin.CreateSystemAsync(NewSystem("F", LifecycleStatus.Nedlagt));
        foreach (var system in new[] { a1, b, c1, f })
        {
            await (await admin.CoupleAsync(system, leaf)).ExpectAsync(HttpStatusCode.OK);
        }

        var coverage = (await admin.CapabilitiesAsync()).Coverage;
        var missing = (await admin.ListSystemsAsync("?capabilityId=none")).Items;

        Assert.Equal(new CouplingCoverage(Covered: 6, Total: 8), coverage);
        Assert.Equal(["C › C2", "D"], missing.Select(s => s.Parent is null ? s.Name : $"{s.Parent.Name} › {s.Name}").Order());
        Assert.Equal(coverage.Total - coverage.Covered, missing.Count);
    }

    /// <summary>
    /// Et modul arver forælderens Udfases (vurderingen hentet fra databasen, ikke kun reglen), og på en forælders
    /// side kommer "tæller ikke med" fra det modul, der HAR koblingen — ikke fra forælderen selv.
    /// </summary>
    [Fact]
    public async Task Et_modul_arver_forælderens_status_og_holderen_afgoer_om_koblingen_taeller()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var leaf = await admin.CapabilityIdAsync("K1.2");
        var arkiv = await admin.CreateSystemAsync(NewSystem("Arkiv", LifecycleStatus.Udfases));
        var soeg = await admin.CreateSystemAsync("Søg", arkiv.Id); // Selv i drift, men forælderen udfases.
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var loen = await admin.CreateSystemAsync(NewSystem("Løn", parentId: nordlys.Id, type: SystemType.LokalLoesning));
        var kompas = await admin.CreateSystemAsync("Kompas");
        foreach (var system in new[] { soeg, loen, kompas })
        {
            await (await admin.CoupleAsync(system, leaf)).ExpectAsync(HttpStatusCode.OK);
        }

        var node = (await admin.CapabilitiesAsync()).Items.Single(i => i.Code == "K1.2").Overlap!;
        var onKompas = (await admin.GetSystemAsync(kompas.Id)).Capabilities.Single();
        var onNordlys = (await admin.GetSystemAsync(nordlys.Id)).Capabilities.Single();
        var onArkiv = (await admin.GetSystemAsync(arkiv.Id)).Capabilities.Single();

        Assert.Equal(
            [("Arkiv › Søg", (OverlapExclusion?)OverlapExclusion.Udfases), ("Kompas", null), ("Nordlys › Løn", OverlapExclusion.LokalLoesning)],
            node.Members.Select(m => (m.Name, m.Exclusion)));
        Assert.Equal((1, false), (node.Counted, node.IsOverlap));
        Assert.Equal(
            [("Arkiv › Søg", (OverlapExclusion?)OverlapExclusion.Udfases), ("Nordlys › Løn", OverlapExclusion.LokalLoesning)],
            onKompas.SharedWith.Select(m => (m.Name, m.Exclusion)));

        // Nordlys (i drift) har ikke selv koblingen — modulet Løn har, og det er en lokal løsning.
        Assert.Equal(("Løn", (OverlapExclusion?)OverlapExclusion.LokalLoesning), (onNordlys.HeldBy?.Name, onNordlys.OwnExclusion));
        Assert.Equal(["Arkiv › Søg", "Kompas"], onNordlys.SharedWith.Select(m => m.Name));
        Assert.Equal(("Søg", (OverlapExclusion?)OverlapExclusion.Udfases), (onArkiv.HeldBy?.Name, onArkiv.OwnExclusion));
    }
}
