using Ea.Api.Integrations;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Integrations;

/// <summary>
/// CSV-skabelonen til foranalysen er en EKSTERN kontrakt: eksempelfilen skal være præcis det, eksporten skriver,
/// og vejledningen skal nævne hver kolonne og hver typekode. UPDATE_CONTRACT=1 genskriver eksempelfilen.
/// </summary>
public sealed class CsvTemplateTests
{
    private static SystemEntity System(string id, string name, SystemType? type = null, SystemEntity? parent = null) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Type = type,
        ParentSystem = parent,
        ParentSystemId = parent?.Id,
    };

    private static Integration Flow(
        string id, SystemEntity from, SystemEntity to, IntegrationType? type, SystemEntity? via = null,
        string? name = null, string? description = null, params string[] data)
    {
        var integration = new Integration
        {
            Id = Guid.Parse(id),
            SourceSystem = from,
            SourceSystemId = from.Id,
            TargetSystem = to,
            TargetSystemId = to.Id,
            ViaPlatform = via,
            ViaPlatformId = via?.Id,
            Type = type,
            Name = name,
            Description = description,
        };
        integration.DataObjects = data
            .Select(d => new IntegrationDataObject { DataObject = new DataObject { Name = d } })
            .ToList();
        return integration;
    }

    /// <summary>Fiktive eksempelrækker, der viser hver regel i vejledningen (docs/csv-integrationer.md).</summary>
    private static List<Integration> Example()
    {
        var identity = System("0199a100-0000-7000-8000-000000000001", "Identitetskilde (eksempel)", SystemType.Egenudviklet);
        var erp = System("0199a100-0000-7000-8000-000000000002", "Nordlys ERP (eksempel)", SystemType.Saas);
        var hr = System("0199a100-0000-7000-8000-000000000003", "HR", SystemType.Saas, erp);
        var platform = System("0199a100-0000-7000-8000-000000000004", "Integrationsplatform (eksempel)", SystemType.Platform);
        var lab = System("0199a100-0000-7000-8000-000000000005", "Laborant (eksempel)", SystemType.Egenudviklet);
        var sheet = System("0199a100-0000-7000-8000-000000000006", "Lønudtræk-regneark (eksempel)", SystemType.LokalLoesning);
        var desk = System("0199a100-0000-7000-8000-000000000007", "Servicedesk (eksempel)", SystemType.Saas);

        return
        [
            // Et modul som afsender ("Forælder > Modul"), via platformen, beskrivelse med semikolon.
            Flow("0199a200-0000-7000-8000-000000000001", hr, identity, IntegrationType.Api, platform,
                description: "Nye og ændrede medarbejdere; sendes ved hver ændring", data: ["Medarbejder", "Organisationsenhed"]),
            // Laborant HENTER via API — registreres som dataflow FRA identitetskilden TIL Laborant.
            Flow("0199a200-0000-7000-8000-000000000002", identity, lab, IntegrationType.Api,
                description: "Laborant henter brugere via identitetskildens API (data går fra identitetskilden til Laborant)",
                data: ["Brugerkonto"]),
            // Direkte databaseadgang (principbrud). Beskrivelsen starter med "-" og neutraliseres med ' i filen.
            Flow("0199a200-0000-7000-8000-000000000003", identity, lab, IntegrationType.DirekteDb,
                description: "- læser direkte i databasen; skal erstattes af API", data: ["Brugerkonto"]),
            // Et udtræk til en lokal løsning (listen er et system af typen Lokal løsning), med navn og citationstegn.
            Flow("0199a200-0000-7000-8000-000000000004", hr, sheet, IntegrationType.Udtraek, name: "Månedligt lønudtræk",
                description: "Til regnearket \"Løn 2026\" hos lønkontoret", data: ["Løn", "Medarbejder"]),
            // Type ikke angivet (tom celle), via platformen og med navn.
            Flow("0199a200-0000-7000-8000-000000000005", identity, desk, null, platform, name: "Natlig brugerfil",
                data: ["Brugerkonto"]),
        ];
    }

    [Fact]
    public async Task Eksempelfilen_er_praecis_det_eksporten_skriver()
    {
        var written = IntegrationCsv.Write(Example());
        var path = RepoPaths.File("docs", "csv", "integrationer-eksempel.csv");

        if (RepoPaths.UpdateGoldenFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, written);
        }

        Assert.True(File.Exists(path), $"{path} mangler — kør testen med UPDATE_CONTRACT=1.");
        Assert.True((await File.ReadAllBytesAsync(path)).SequenceEqual(written),
            "CSV-formatet er ændret. Det er en ekstern kontrakt: opdatér docs/csv-integrationer.md og kør med UPDATE_CONTRACT=1.");
    }

    [Fact]
    public async Task Vejledningen_naevner_hver_kolonne_og_hver_typekode()
    {
        var guide = await File.ReadAllTextAsync(RepoPaths.File("docs", "csv-integrationer.md"));

        Assert.All(IntegrationCsv.Header, column => Assert.Contains($"`{column}`", guide, StringComparison.Ordinal));
        Assert.All(Enum.GetNames<IntegrationType>(), code => Assert.Contains($"`{code}`", guide, StringComparison.Ordinal));
        Assert.All(SystemCsv.Header, column => Assert.Contains($"`{column}`", guide, StringComparison.Ordinal));
    }
}
