using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ea.Api.Persons;
using Ea.Api.Systems;

namespace Ea.Api.Tests.Infrastructure;

/// <summary>Små hjælpere til at kalde API'et som en rigtig klient (via HTTP, ikke direkte i databasen).</summary>
public static class TestApi
{
    public static SystemWriteRequest NewSystem(
        string name,
        LifecycleStatus status = LifecycleStatus.IDrift,
        Guid? parentId = null,
        Guid? teamId = null,
        SystemType? type = null,
        string? description = null,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<RoleAssignmentInput>? roles = null,
        uint? version = null) =>
        new(name, aliases, description, type, status, teamId, parentId, roles, version);

    public static async Task<SystemDetail> CreateSystemAsync(this HttpClient client, SystemWriteRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/systems", request, TestApp.Json);
        await response.ExpectAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SystemDetail>(TestApp.Json))!;
    }

    public static Task<SystemDetail> CreateSystemAsync(this HttpClient client, string name, Guid? parentId = null) =>
        client.CreateSystemAsync(NewSystem(name, parentId: parentId));

    public static async Task<SystemDetail> GetSystemAsync(this HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/systems/{id}");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SystemDetail>(TestApp.Json))!;
    }

    public static Task<HttpResponseMessage> PutSystemAsync(this HttpClient client, Guid id, SystemWriteRequest request) =>
        client.PutAsJsonAsync($"/api/systems/{id}", request, TestApp.Json);

    public static async Task<SystemListResponse> ListSystemsAsync(this HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/systems" + query);
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SystemListResponse>(TestApp.Json))!;
    }

    public static async Task<PersonDto> CreatePersonAsync(this HttpClient client, string name, string? department = null)
    {
        var response = await client.PostAsJsonAsync("/api/persons", new CreatePersonRequest(name, null, department), TestApp.Json);
        await response.ExpectAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PersonDto>(TestApp.Json))!;
    }

    /// <summary>Kopi af et systems nuværende data som redigeringsanmodning.</summary>
    public static SystemWriteRequest ToWrite(this SystemDetail s) => new(
        s.Name,
        s.Aliases,
        s.Description,
        s.Type,
        s.LifecycleStatus,
        s.ManagingTeam?.Id,
        s.Parent?.Id,
        s.Roles.Select(r => new RoleAssignmentInput(r.Role, r.Person.Id)).ToList(),
        s.Version);

    public static async Task ExpectAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Forventede {(int)expected} {expected}, fik {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>Læser en ProblemDetails' detail-felt (den besked, brugeren ser).</summary>
    public static async Task<string> ProblemDetailAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString()!;
    }

    /// <summary>Læser feltfejl fra et ValidationProblem.</summary>
    public static async Task<Dictionary<string, string[]>> ValidationErrorsAsync(this HttpResponseMessage response)
    {
        await response.ExpectAsync(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
