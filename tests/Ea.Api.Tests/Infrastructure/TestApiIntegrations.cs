using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ea.Api.Integrations;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>Hjælpere til integrations-API'et (kaldt over HTTP som en rigtig klient).</summary>
public static class TestApiIntegrations
{
    public static IntegrationCreateRequest NewIntegration(
        Guid from,
        Guid to,
        IntegrationType? type = IntegrationType.Api,
        Guid? via = null,
        string? name = null,
        string? description = null,
        IReadOnlyList<Guid>? dataObjectIds = null) =>
        new(from, to, via, type, name, description, dataObjectIds);

    public static async Task<IntegrationDto> CreateIntegrationAsync(this HttpClient client, IntegrationCreateRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/integrations", request, TestApp.Json);
        await response.ExpectAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IntegrationDto>(TestApp.Json))!;
    }

    public static Task<HttpResponseMessage> PostIntegrationAsync(this HttpClient client, IntegrationCreateRequest request) =>
        client.PostAsJsonAsync("/api/integrations", request, TestApp.Json);

    public static Task<HttpResponseMessage> PutIntegrationAsync(this HttpClient client, Guid id, IntegrationUpdateRequest request) =>
        client.PutAsJsonAsync($"/api/integrations/{id}", request, TestApp.Json);

    public static async Task<IntegrationDto> GetIntegrationAsync(this HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/integrations/{id}");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<IntegrationDto>(TestApp.Json))!;
    }

    public static async Task<SystemIntegrationsResponse> SystemIntegrationsAsync(this HttpClient client, Guid systemId)
    {
        var response = await client.GetAsync($"/api/systems/{systemId}/integrations");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SystemIntegrationsResponse>(TestApp.Json))!;
    }

    public static async Task<DataObjectDto> CreateDataObjectAsync(this HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/data-objects", new CreateDataObjectRequest(name), TestApp.Json);
        await response.ExpectAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DataObjectDto>(TestApp.Json))!;
    }

    public static IntegrationUpdateRequest ToUpdate(this IntegrationDto i) => new(
        i.Via?.Id, i.Type, i.Name, i.Description, i.DataObjects.Select(d => d.Id).ToList(), i.Version);

    /// <summary>ProblemDetails' type — skelner forældet version, dublet og blokeret.</summary>
    public static async Task<string?> ProblemTypeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }
}
