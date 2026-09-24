using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Contract;

/// <summary>
/// API-kontrakten mellem server og klient: det serverede OpenAPI-dokument skal være identisk med
/// web/src/api/openapi.json, som klientens TypeScript-typer genereres fra. Ændres en DTO, bliver denne
/// test rød — kør den med UPDATE_CONTRACT=1 for at opdatere filen, og regenerér typerne (npm run gen:api).
/// </summary>
public sealed class OpenApiContractTests
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Klientens_kontrakt_matcher_serveren()
    {
        await using var app = await TestApp.StartAsync();
        var served = Normalize(await app.Anonymous().GetStringAsync("/openapi/v1.json"));
        var path = Path.Combine(RepoRoot(), "web", "src", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACT") == "1")
        {
            await File.WriteAllTextAsync(path, served);
        }

        Assert.True(File.Exists(path), $"{path} mangler — kør testen med UPDATE_CONTRACT=1.");
        var committed = await File.ReadAllTextAsync(path);
        Assert.True(committed == served,
            "API-kontrakten er ændret. Kør `UPDATE_CONTRACT=1 dotnet test --project tests/Ea.Api.Tests` og derefter `npm run gen:api` i web/.");
        // Kontrakten skal faktisk beskrive API'et (ikke være et tomt dokument).
        Assert.Contains("\"/api/systems/{id}\"", committed, StringComparison.Ordinal);
    }

    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!;
        // Serverens URL afhænger af, hvor testen kører — den er ikke en del af kontrakten.
        node.AsObject().Remove("servers");
        return node.ToJsonString(Pretty).ReplaceLineEndings("\n") + "\n";
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EA.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Kunne ikke finde repo-roden (EA.slnx).");
    }
}
