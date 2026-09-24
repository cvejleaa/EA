using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ea.Api.Capabilities;
using Ea.Api.Common;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>Kapabilitets-import via HTTP, som klienten gør det: filen er request-kroppen (text/csv).</summary>
public static class TestApiCapabilities
{
    /// <summary>En fil i registrets format. Rækker: (Kode, Navn, ForælderKode, Beskrivelse).</summary>
    public static byte[] File(params (string Code, string Name, string? Parent, string? Description)[] rows) =>
        Csv.Write(CapabilityCsv.Header, rows.Select(r => (IReadOnlyList<string?>)[r.Code, r.Name, r.Parent, r.Description]));

    public static byte[] Text(string csv) => Encoding.UTF8.GetBytes(csv);

    public static Task<HttpResponseMessage> PostImportAsync(this HttpClient client, byte[] file, bool dryRun, string? fingerprint = null)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var query = $"?dryRun={(dryRun ? "true" : "false")}" + (fingerprint is null ? "" : $"&fingerprint={fingerprint}");
        return client.PostAsync("/api/capabilities/import" + query, content);
    }

    public static async Task<CapabilityImportResult> DryRunAsync(this HttpClient client, byte[] file)
    {
        var response = await client.PostImportAsync(file, dryRun: true);
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CapabilityImportResult>(TestApp.Json))!;
    }

    /// <summary>Tør-kørsel og derefter gennemførelse med dens fingeraftryk — som brugeren gør det.</summary>
    public static async Task<CapabilityImportResult> ImportAsync(this HttpClient client, byte[] file)
    {
        var preview = await client.DryRunAsync(file);
        Assert.Empty(preview.Errors);
        var response = await client.PostImportAsync(file, dryRun: false, preview.Fingerprint);
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CapabilityImportResult>(TestApp.Json))!;
    }

    public static async Task<CapabilityTreeResponse> CapabilitiesAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/capabilities");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CapabilityTreeResponse>(TestApp.Json))!;
    }

    /// <summary>Kapabilitetens id ud fra koden (fra kortet).</summary>
    public static async Task<Guid> CapabilityIdAsync(this HttpClient client, string code) =>
        (await client.CapabilitiesAsync()).Items.Single(i => i.Code == code).Id;

    /// <summary>Sætter systemets egne koblinger (hele listen) og returnerer svaret.</summary>
    public static Task<HttpResponseMessage> CoupleAsync(this HttpClient client, Ea.Api.Systems.SystemDetail system, params Guid[] capabilityIds) =>
        client.PutSystemAsync(system.Id, system.ToWrite() with { CapabilityIds = capabilityIds });

    /// <summary>Kortet som (dybde, kode, navn, forælderens kode) i visningsrækkefølge.</summary>
    public static async Task<List<(int Depth, string Code, string Name, string? Parent)>> TreeAsync(this HttpClient client)
    {
        var items = (await client.CapabilitiesAsync()).Items;
        var codes = items.ToDictionary(i => i.Id, i => i.Code);
        return items.Select(i => (i.Depth, i.Code, i.Name, i.ParentId is { } p ? codes[p] : null)).ToList();
    }
}
