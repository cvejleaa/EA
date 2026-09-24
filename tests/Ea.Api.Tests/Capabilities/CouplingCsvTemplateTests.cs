using Ea.Api.Capabilities;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Koblings-CSV'en er en EKSTERN kontrakt: eksempelfilen er præcis det, eksporten skriver, og vejledningen nævner
/// hver kolonne og hver kode. UPDATE_CONTRACT=1 genskriver eksempelfilen.
/// </summary>
public sealed class CouplingCsvTemplateTests
{
    private static Guid Id(int n) => Guid.Parse($"0199b200-0000-7000-8000-{n:000000000000}");

    private static Capability Node(int id, string code, string name, Capability? parent = null) =>
        new() { Id = Id(id), Code = code, Name = name, ParentId = parent?.Id };

    private static readonly DateTimeOffset Changed = new(2026, 8, 1, 10, 15, 30, TimeSpan.Zero);

    /// <summary>FIKTIVE eksempeldata (ikke HERM, ingen rigtige systemer eller personer).</summary>
    private static (List<SystemEntity> Systems, List<Capability> Capabilities, HashSet<Guid> Missing) Example()
    {
        var education = Node(1, "EK", "Uddannelse (eksempel)");
        var admin = Node(2, "EK-STUD", "Studieadministration (eksempel)", education);
        var admission = Node(3, "EK-STUD-2", "Optagelse (eksempel)", admin);
        var exams = Node(4, "EK-STUD-10", "Eksamen (eksempel)", admin);
        var research = Node(5, "FO", "Forskning (eksempel)");
        var lab = Node(6, "FO-LAB", "Laboratoriedrift (eksempel)", research);
        var retired = Node(7, "EK-GAMMEL", "Gammel eksamen (eksempel)");
        retired.RetiredAt = Changed;
        retired.RetiredPath = "Uddannelse (eksempel)";

        var team = new Team { Id = Id(100), Name = "Stab (eksempel)" };
        var owner = new Person { Id = Id(101), DisplayName = "Bo Bogholder (eksempel)" };
        var systemOwner = new Person { Id = Id(102), DisplayName = "Frida Forvalter (eksempel)" };

        SystemEntity System(int id, string name, LifecycleStatus status, SystemType? type = null, SystemEntity? parent = null,
            string? description = null, params Capability[] coupled)
        {
            var system = new SystemEntity
            {
                Id = Id(id),
                Name = name,
                LifecycleStatus = status,
                Type = type,
                ParentSystemId = parent?.Id,
                ParentSystem = parent,
                ManagingTeam = team,
                ManagingTeamId = team.Id,
                Description = description,
                UpdatedAt = Changed.AddTicks(id * 10),
                LastConfirmedAt = Changed,
            };
            system.CapabilityLinks = coupled.Select(c => new SystemCapability { SystemId = system.Id, CapabilityId = c.Id }).ToList();
            return system;
        }

        // Nordlys har ingen egne koblinger, men er dækket af modulet HR — derfor ingen række.
        var nordlys = System(10, "Nordlys (eksempel)", LifecycleStatus.IDrift, SystemType.Standardsystem);
        var kompas = System(12, "Kompas (eksempel)", LifecycleStatus.IDrift, SystemType.Saas, null,
            "Studieadministration; bruges også af efter- og videreuddannelse", lab, admission, retired);
        kompas.Roles =
        [
            new SystemRoleAssignment { SystemId = kompas.Id, Role = SystemRole.Forretningsejer, PersonId = owner.Id, Person = owner },
            new SystemRoleAssignment { SystemId = kompas.Id, Role = SystemRole.Systemejer, PersonId = systemOwner.Id, Person = systemOwner },
        ];
        List<SystemEntity> systems =
        [
            // Bevidst i "forkert" rækkefølge: eksporten sorterer efter fuldt navn, derefter kode.
            System(13, "Ugle (eksempel)", LifecycleStatus.Udfases, SystemType.Egenudviklet, null, null, admission),
            kompas,
            nordlys,
            System(11, "HR", LifecycleStatus.IDrift, null, nordlys, null, admission),
            System(14, "Rune (eksempel)", LifecycleStatus.Planlagt, SystemType.Saas, null, null, lab),
            System(15, "Regneark (eksempel)", LifecycleStatus.IDrift, SystemType.LokalLoesning, null,
                "-Udtræk til økonomi (formel-tegnet først skal neutraliseres)", admission),
            System(16, "Tomrum (eksempel)", LifecycleStatus.Indfases),
        ];
        return (systems, [education, admin, admission, exams, research, lab, retired], [Id(16)]);
    }

    [Fact]
    public async Task Eksempelfilen_er_praecis_det_eksporten_skriver()
    {
        var (systems, capabilities, missing) = Example();
        var written = CouplingCsv.Write(systems, capabilities, missing);
        var path = RepoPaths.File("docs", "csv", "koblinger-eksempel.csv");

        if (RepoPaths.UpdateGoldenFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, written);
        }

        Assert.True(File.Exists(path), $"{path} mangler — kør testen med UPDATE_CONTRACT=1.");
        Assert.True((await File.ReadAllBytesAsync(path)).SequenceEqual(written),
            "CSV-formatet er ændret. Det er en ekstern kontrakt: opdatér docs/csv-koblinger.md og kør med UPDATE_CONTRACT=1.");
    }

    [Fact]
    public async Task Vejledningen_naevner_hver_kolonne_og_hver_kode()
    {
        var guide = await File.ReadAllTextAsync(RepoPaths.File("docs", "csv-koblinger.md"));

        Assert.All(CouplingCsv.Header, column => Assert.Contains($"`{column}`", guide, StringComparison.Ordinal));
        Assert.All(Enum.GetNames<OverlapExclusion>(), code => Assert.Contains($"`{code}`", guide, StringComparison.Ordinal));
        Assert.All(Enum.GetNames<MoveReason>(), code => Assert.Contains($"`{code}`", guide, StringComparison.Ordinal));
    }

    [Fact]
    public void Eksemplet_viser_overlap_undtagelser_flytning_og_et_system_der_mangler()
    {
        // Eksempelfilen skal vise hvert tilfælde, vejledningen forklarer — ellers er den en tom skabelon.
        var (systems, capabilities, missing) = Example();
        var read = Ea.Api.Common.Csv.Read(CouplingCsv.Write(systems, capabilities, missing));
        var rows = read.Rows.Select(r => CouplingCsv.Header.Zip(r.Fields).ToDictionary(p => p.First, p => p.Second)).ToList();

        Assert.Equal(
        [
            ("Kompas (eksempel)", "EK-GAMMEL", "", "", "Udgaaet"),
            ("Kompas (eksempel)", "EK-STUD-2", "Ja", "", ""),
            ("Kompas (eksempel)", "FO-LAB", "Nej", "", ""),
            ("Nordlys (eksempel) > HR", "EK-STUD-2", "Ja", "", ""),
            ("Regneark (eksempel)", "EK-STUD-2", "Ja", "LokalLoesning", ""),
            ("Rune (eksempel)", "FO-LAB", "Nej", "Planlagt", ""),
            ("Tomrum (eksempel)", "", "", "", ""),
            ("Ugle (eksempel)", "EK-STUD-2", "Ja", "Udfases", ""),
        ],
            rows.Select(r => (r["FuldtNavn"], r["Kode"], r["Overlap"], r["TællerIkkeMed"], r["BørFlyttes"])));
        Assert.Equal(
            "Nordlys (eksempel) > HR | Regneark (eksempel) (LokalLoesning) | Ugle (eksempel) (Udfases)",
            rows[1]["DelesMed"]);
        Assert.Equal(("1", "1"), (rows[2]["SystemerDerTæller"], rows[2]["AntalPlanlagte"]));
        Assert.Equal(("Bo Bogholder (eksempel)", "Frida Forvalter (eksempel)"), (rows[0]["Forretningsejer"], rows[0]["Systemejer"]));
    }

    [Fact]
    public async Task Vejledningen_naevner_importens_graenser()
    {
        var guide = await File.ReadAllTextAsync(RepoPaths.File("docs", "csv-koblinger.md"));

        var maxRows = CouplingImport.MaxRows.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("da-DK"));
        Assert.Contains($"højst {maxRows} rækker", guide, StringComparison.Ordinal);
        Assert.Contains($"{CouplingImport.MaxFileBytes / (1024 * 1024)} MB", guide, StringComparison.Ordinal);
        Assert.Contains($"højst {CapabilityRules.MaxCouplingsPerSystem} koblinger", guide, StringComparison.Ordinal);
    }

    /// <summary>
    /// Begrundelsen for importens grænser (CouplingImport.MaxRows og MaxFileBytes): en eksport af hele DTU — 2000
    /// systemer med 5 koblinger hver og en lang beskrivelse (1000 tegn) — kan indlæses igen uden at ramme dem.
    /// Pessimistisk: kun 100 blade, så hver kapabilitet deles af ca. 100 systemer, og <c>DelesMed</c> bliver lang.
    /// (Med 50 blade fyldte filen 57 MB — derfor er grænsen ikke Kestrels 30 MB.)
    /// </summary>
    [Fact]
    public void En_eksport_af_hele_DTU_kan_indlaeses_igen()
    {
        var capabilities = Enumerable.Range(1, 100).Select(i => Node(1000 + i, $"B{i}", $"Blad {i} (eksempel)")).ToList();
        var description = new string('x', 1000);
        var systems = Enumerable.Range(1, 2000).Select(i =>
        {
            var system = new SystemEntity
            {
                Id = Id(10_000 + i),
                Name = $"System {i} (eksempel)",
                LifecycleStatus = LifecycleStatus.IDrift,
                Description = description,
                UpdatedAt = Changed,
                LastConfirmedAt = Changed,
            };
            system.CapabilityLinks = Enumerable.Range(0, 5)
                .Select(k => new SystemCapability { SystemId = system.Id, CapabilityId = capabilities[(i + k) % 100].Id })
                .ToList();
            return system;
        }).ToList();

        var written = CouplingCsv.Write(systems, capabilities, new HashSet<Guid>());
        var (rows, errors) = CouplingImport.Parse(Ea.Api.Common.Csv.Read(written));

        Assert.Empty(errors);
        Assert.Equal(10_000, rows.Count);
        Assert.True(rows.Count * 2 <= CouplingImport.MaxRows, "MaxRows skal give dobbelt margin over hele DTU.");
        Assert.True(written.Length < CouplingImport.MaxFileBytes,
            $"Eksporten fylder {written.Length / (1024 * 1024)} MB — mere end MaxFileBytes.");
    }
}
