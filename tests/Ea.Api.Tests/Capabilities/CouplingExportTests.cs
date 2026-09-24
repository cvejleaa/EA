using System.Net;
using Ea.Api.Capabilities;
using Ea.Api.Common;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;
using static Ea.Api.Tests.Infrastructure.TestApi;
using static Ea.Api.Tests.Infrastructure.TestApiCapabilities;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Koblings-CSV'en over HTTP (3c-1): egne koblinger med serverens overlap-vurdering og en række uden kode for hvert
/// system, der mangler — efter SAMME regel som systemlistens "Ikke angivet", minus nedlagte.
/// </summary>
public sealed class CouplingExportTests
{
    private static readonly byte[] Model = File(
        ("K1", "Uddannelse", null, null),
        ("K1.1", "Optagelse", "K1", null),
        ("K2", "Forskning", null, null),
        ("K2.1", "Laboratorier", "K2", null));

    private static async Task<List<Dictionary<string, string>>> ExportAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/capabilities/couplings/export.csv");
        await response.ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var read = Csv.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(read.Error);
        Assert.Equal(CouplingCsv.Header, read.Header);
        return read.Rows.Select(r => CouplingCsv.Header.Zip(r.Fields).ToDictionary(p => p.First, p => p.Second)).ToList();
    }

    [Fact]
    public async Task Eksporten_viser_koblinger_overlap_og_de_systemer_der_mangler()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var ids = (await admin.CapabilitiesAsync()).Items.ToDictionary(i => i.Code, i => i.Id);

        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", nordlys.Id);
        await admin.CreateSystemAsync("Løn", nordlys.Id); // Dækkes ikke af søskendens kobling.
        var kompas = await admin.CreateSystemAsync("Kompas");
        var ugle = await admin.CreateSystemAsync(NewSystem("Ugle", LifecycleStatus.Udfases));
        await admin.CreateSystemAsync(NewSystem("Gammel", LifecycleStatus.Nedlagt)); // Mangler, men er nedlagt.
        await admin.CreateSystemAsync(NewSystem("Tomrum", LifecycleStatus.Planlagt));

        await (await admin.CoupleAsync(hr, ids["K1.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(kompas, ids["K1.1"], ids["K2.1"])).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(ugle, ids["K1.1"])).ExpectAsync(HttpStatusCode.OK);

        // En læser kan hente filen (samme data som systemsiderne).
        var rows = await ExportAsync(await app.ClientFor(TestUsers.Reader));

        Assert.Equal(
        [
            ("Kompas", "K1.1", "Ja", "2", "", "Nordlys > HR | Ugle (Udfases)"),
            ("Kompas", "K2.1", "Nej", "1", "", ""),
            ("Nordlys > HR", "K1.1", "Ja", "2", "", "Kompas | Ugle (Udfases)"),
            ("Nordlys > Løn", "", "", "", "", ""),
            ("Tomrum", "", "", "", "", ""),
            ("Ugle", "K1.1", "Ja", "2", "Udfases", "Kompas | Nordlys > HR"),
        ],
            rows.Select(r => (r["FuldtNavn"], r["Kode"], r["Overlap"], r["SystemerDerTæller"], r["TællerIkkeMed"], r["DelesMed"])));
        Assert.Equal(kompas.Id.ToString(), rows[0]["SystemId"]);
        Assert.Equal(("Optagelse", "Uddannelse", "IDrift"), (rows[0]["Kapabilitet"], rows[0]["Sti"], rows[0]["Status"]));
        Assert.Equal("Nordlys", rows[2]["Forælder"]);
    }

    [Fact]
    public async Task De_tomme_raekker_er_praecis_systemlistens_Ikke_angivet_uden_nedlagte()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.ImportAsync(Model);
        var leaf = await admin.CapabilityIdAsync("K1.1");

        // Forælder dækket af et modul, modul dækket af forælderen, modul med kun en søskendes kobling, nedlagt.
        var covered = await admin.CreateSystemAsync("Dækket");
        var coveredModule = await admin.CreateSystemAsync("Modul", covered.Id);
        var byModule = await admin.CreateSystemAsync("Forælder");
        var coupledModule = await admin.CreateSystemAsync("Koblet", byModule.Id);
        await admin.CreateSystemAsync("Søskende", byModule.Id);
        await admin.CreateSystemAsync(NewSystem("Nedlagt", LifecycleStatus.Nedlagt));
        await admin.CreateSystemAsync("Ingenting");
        await (await admin.CoupleAsync(covered, leaf)).ExpectAsync(HttpStatusCode.OK);
        await (await admin.CoupleAsync(coupledModule, leaf)).ExpectAsync(HttpStatusCode.OK);

        var empty = (await ExportAsync(admin)).Where(r => r["Kode"] == "").Select(r => r["FuldtNavn"]).ToList();
        var none = (await admin.ListSystemsAsync("?capabilityId=none")).Items
            .Where(s => s.LifecycleStatus != LifecycleStatus.Nedlagt)
            .Select(s => s.Parent is null ? s.Name : $"{s.Parent.Name} > {s.Name}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Forælder > Søskende", "Ingenting"], empty);
        Assert.Equal(none, empty);
        Assert.DoesNotContain(coveredModule.Name, empty);
    }
}
