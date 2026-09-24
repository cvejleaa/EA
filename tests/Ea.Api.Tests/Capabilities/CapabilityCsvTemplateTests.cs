using Ea.Api.Capabilities;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Kapabilitets-CSV'en er en EKSTERN kontrakt: eksempelfilen er præcis det, eksporten skriver, og vejledningen
/// nævner hver kolonne. UPDATE_CONTRACT=1 genskriver eksempelfilen.
/// </summary>
public sealed class CapabilityCsvTemplateTests
{
    private static Capability Node(string id, string code, string name, Capability? parent = null, string? description = null) => new()
    {
        Id = Guid.Parse(id),
        Code = code,
        Name = name,
        ParentId = parent?.Id,
        Description = description,
    };

    /// <summary>FIKTIVE eksempelrækker (ikke HERM): tre niveauer, en beskrivelse med semikolon og "10" efter "2".</summary>
    private static List<Capability> Example()
    {
        var education = Node("0199b100-0000-7000-8000-000000000001", "EK", "Uddannelse (eksempel)");
        var admin = Node("0199b100-0000-7000-8000-000000000002", "EK-STUD", "Studieadministration (eksempel)", education);
        var research = Node("0199b100-0000-7000-8000-000000000003", "FO", "Forskning (eksempel)");
        return
        [
            // Bevidst i "forkert" rækkefølge: eksporten skriver altid forælder før børn, søskende efter kode.
            Node("0199b100-0000-7000-8000-000000000010", "EK-STUD-10", "Eksamen (eksempel)", admin),
            research,
            Node("0199b100-0000-7000-8000-000000000011", "EK-STUD-2", "Optagelse (eksempel)", admin,
                "Fra ansøgning til optagelse; inkl. dispensationer"),
            education,
            admin,
            Node("0199b100-0000-7000-8000-000000000020", "FO-LAB", "Laboratoriedrift (eksempel)", research),
        ];
    }

    [Fact]
    public async Task Eksempelfilen_er_praecis_det_eksporten_skriver()
    {
        var written = CapabilityCsv.Write(Example());
        var path = RepoPaths.File("docs", "csv", "kapabiliteter-eksempel.csv");

        if (RepoPaths.UpdateGoldenFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, written);
        }

        Assert.True(File.Exists(path), $"{path} mangler — kør testen med UPDATE_CONTRACT=1.");
        Assert.True((await File.ReadAllBytesAsync(path)).SequenceEqual(written),
            "CSV-formatet er ændret. Det er en ekstern kontrakt: opdatér docs/csv-kapabiliteter.md og kør med UPDATE_CONTRACT=1.");
    }

    [Fact]
    public async Task Vejledningen_naevner_hver_kolonne_og_graenserne()
    {
        var guide = await File.ReadAllTextAsync(RepoPaths.File("docs", "csv-kapabiliteter.md"));

        Assert.All(CapabilityCsv.Header, column => Assert.Contains($"`{column}`", guide, StringComparison.Ordinal));
        Assert.Contains($"højst {CapabilityRules.MaxDepth} niveauer", guide, StringComparison.Ordinal);
        Assert.Contains($"højst {CapabilityRules.CodeMaxLength} tegn", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"over {CapabilityRules.MaxRows} rækker", guide, StringComparison.Ordinal);
    }
}
