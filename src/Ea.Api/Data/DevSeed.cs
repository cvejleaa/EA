using Ea.Api.Capabilities;
using Ea.Api.Integrations;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Data;

/// <summary>
/// FIKTIVE eksempeldata til lokal udvikling. Ingen rigtige DTU-systemer, -personer eller -integrationer:
/// fiktive oplysninger om et rigtigt system bliver forvekslet med fakta.
/// </summary>
public static partial class DevSeed
{
    private static readonly string[] SeedDataObjects = ["Medarbejder", "Studerende", "Organisationsenhed", "Brugerkonto", "Løn"];

    /// <summary>Samme oid som dev-brugeren "frida" i appsettings.Development.json (forberedt til delopgave 4).</summary>
    public const string FridaOid = "00000000-0000-0000-0000-00000000f001";

    public static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EaDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevSeed));

        await db.Database.MigrateAsync();
        var now = time.GetUtcNow();

        // Hver del har sin egen vagt, så en eksisterende udviklingsdatabase også får de nyere eksempeldata.
        if (!await db.Systems.AnyAsync())
        {
            await SeedSystemsAsync(db, now);
        }

        if (!await db.Integrations.AnyAsync())
        {
            await SeedIntegrationsAsync(db, now, logger);
        }

        if (!await db.Capabilities.AnyAsync())
        {
            await SeedCapabilitiesAsync(db, now, logger);
        }
    }

    /// <summary>
    /// Et lille FIKTIVT kapabilitetskort — ikke HERM (licens) og ikke DTU's. Lægges ind via selve importen, så
    /// eksempeldata og import følger samme regler.
    /// </summary>
    private static async Task SeedCapabilitiesAsync(EaDbContext db, DateTimeOffset now, ILogger logger)
    {
        (string Code, string Name, string? Parent)[] model =
        [
            ("K1", "Uddannelse (fiktiv)", null),
            ("K1.1", "Studieadministration", "K1"),
            ("K1.1.1", "Optagelse", "K1.1"),
            ("K1.1.2", "Eksamensadministration", "K1.1"),
            ("K1.2", "Undervisningsplatforme", "K1"),
            ("K1.2.1", "Kursusindhold", "K1.2"),
            ("K2", "Forskning (fiktiv)", null),
            ("K2.1", "Laboratoriedrift", "K2"),
            ("K2.1.1", "Prøvehåndtering", "K2.1"),
            ("K3", "Understøttende funktioner (fiktiv)", null),
            ("K3.1", "Økonomi", "K3"),
            ("K3.1.1", "Bogføring", "K3.1"),
            ("K3.1.2", "Projektøkonomi", "K3.1"),
            ("K3.2", "Personale", "K3"),
            ("K3.2.1", "Løn", "K3.2"),
            ("K3.2.2", "Rekruttering", "K3.2"),
            ("K3.3", "IT-drift", "K3"),
            ("K3.3.1", "Identitet og adgang", "K3.3"),
            ("K3.3.2", "Servicedesk", "K3.3"),
        ];

        var rows = model.Select((m, i) => new CapabilityImportRow(i + 2, m.Code, m.Name, m.Parent, null)).ToList();
        var plan = CapabilityImport.Plan(rows, []);
        CapabilityImport.Apply(plan, [], db, now);
        await db.SaveChangesAsync();
        LogCapabilitiesSeeded(logger, rows.Count);
    }

    private static async Task SeedSystemsAsync(EaDbContext db, DateTimeOffset now)
    {
        var persons = new Dictionary<string, Person>
        {
            ["bo"] = NewPerson("Bo Bogholder", "Økonomi (fiktiv)"),
            ["hanne"] = NewPerson("Hanne Holm", "HR (fiktiv)"),
            ["sara"] = NewPerson("Sara Studie", "Studieadministration (fiktiv)"),
            ["lars"] = NewPerson("Lars Lab", "Laboratoriedrift (fiktiv)"),
            ["frida"] = NewPerson("Frida Forvalter (fiktiv)", "IT Forretningsløsninger (fiktiv)", FridaOid),
            ["ivan"] = NewPerson("Ivan Integration", "IT Forretningsløsninger (fiktiv)"),
        };
        db.Persons.AddRange(persons.Values);

        SystemEntity NewSystem(string name, SystemType? type, LifecycleStatus status, Guid? team, string? description,
            SystemEntity? parent = null, string[]? aliases = null, int confirmedDaysAgo = 10) =>
            new()
            {
                Id = Guid.CreateVersion7(now),
                Name = name,
                Type = type,
                LifecycleStatus = status,
                ManagingTeamId = team,
                Description = description,
                ParentSystemId = parent?.Id,
                Aliases = aliases?.ToList() ?? [],
                CreatedAt = now.AddDays(-400),
                UpdatedAt = now.AddDays(-confirmedDaysAgo),
                LastConfirmedAt = now.AddDays(-confirmedDaysAgo),
                LastConfirmedByOid = "seed",
                LastConfirmedByName = "Eksempeldata",
            };

        var erp = NewSystem("Nordlys ERP", SystemType.Saas, LifecycleStatus.IDrift, Team.Kerneapplikationer,
            "Fiktivt administrativt kernesystem til økonomi, HR og projekter.", aliases: ["Nordlys", "ERP"], confirmedDaysAgo: 30);
        var systems = new List<SystemEntity>
        {
            erp,
            NewSystem("Nordlys Økonomi", SystemType.Saas, LifecycleStatus.IDrift, Team.Kerneapplikationer,
                "Finans, kreditorer og debitorer.", erp, confirmedDaysAgo: 30),
            NewSystem("Nordlys HR", SystemType.Saas, LifecycleStatus.IDrift, Team.Kerneapplikationer,
                "Medarbejderdata og løn.", erp, confirmedDaysAgo: 200),
            NewSystem("Nordlys Projekter", SystemType.Saas, LifecycleStatus.Indfases, Team.Kerneapplikationer,
                "Projektøkonomi.", erp, confirmedDaysAgo: 5),
            NewSystem("Kompas Sag", SystemType.Egenudviklet, LifecycleStatus.IDrift, Team.SpecialiseredeLoesninger,
                "Fiktivt sagsbehandlings- og arkiveringssystem.", aliases: ["Kompas"], confirmedDaysAgo: 400),
            NewSystem("Laborant", SystemType.Egenudviklet, LifecycleStatus.IDrift, Team.SpecialiseredeLoesninger,
                "Fiktiv database over laboratoriekemikalier.", confirmedDaysAgo: 90),
            NewSystem("Servicedesk Plus (fiktiv)", SystemType.Saas, LifecycleStatus.IDrift, Team.SpecialiseredeLoesninger,
                "Fiktivt ITSM-værktøj til sager og serviceanmodninger.", aliases: ["Serviceportal"]),
            NewSystem("Blanketmotor", SystemType.LowCode, LifecycleStatus.IDrift, Team.SpecialiseredeLoesninger,
                "Fiktive blanketter bygget på low-code."),
            NewSystem("Identitetskilde (fiktiv)", SystemType.Egenudviklet, LifecycleStatus.IDrift, Team.DigitalArbejdsplads,
                "Fiktivt kildesystem for identiteter og organisationsdata.", confirmedDaysAgo: 700),
            NewSystem("Integrationsplatform (fiktiv)", SystemType.Platform, LifecycleStatus.IDrift, Team.DataOgIntegrationer,
                "Fiktiv platform, som integrationer kører via."),
            NewSystem("Rapportbanken", SystemType.Standardsystem, LifecycleStatus.IDrift, Team.DataOgIntegrationer,
                "Fiktiv BI-løsning til ledelsesrapportering.", aliases: ["BI"]),
            NewSystem("Studium", SystemType.Saas, LifecycleStatus.Planlagt, Team.DataOgIntegrationer,
                "Fiktivt kommende studieadministrativt system."),
            NewSystem("Arkiv 2000", SystemType.Standardsystem, LifecycleStatus.Udfases, Team.SpecialiseredeLoesninger,
                "Fiktivt gammelt arkivsystem, der erstattes af Kompas Sag.", confirmedDaysAgo: 1000),
            NewSystem("Lønudtræk-regneark", SystemType.LokalLoesning, LifecycleStatus.IDrift, team: null,
                "Fiktivt månedligt udtræk til et regneark — typisk skygge-IT.", confirmedDaysAgo: 365),
            NewSystem("Ressourcebooking", type: null, LifecycleStatus.IDrift, team: null,
                "Fiktivt bookingsystem uden kendt ejer.", confirmedDaysAgo: 500),
        };
        db.Systems.AddRange(systems);

        void Assign(string systemName, SystemRole role, string personKey) =>
            systems.Single(s => s.Name == systemName).Roles.Add(new SystemRoleAssignment
            {
                SystemId = systems.Single(s => s.Name == systemName).Id,
                Role = role,
                PersonId = persons[personKey].Id,
            });

        Assign("Nordlys ERP", SystemRole.Forretningsejer, "bo");
        Assign("Nordlys ERP", SystemRole.Systemforvalter, "frida");
        Assign("Nordlys Økonomi", SystemRole.Forretningsejer, "bo");
        Assign("Nordlys HR", SystemRole.Forretningsejer, "hanne");
        Assign("Laborant", SystemRole.Forretningsejer, "lars");
        Assign("Laborant", SystemRole.Systemforvalter, "frida");
        Assign("Studium", SystemRole.Forretningsejer, "sara");
        Assign("Integrationsplatform (fiktiv)", SystemRole.Systemforvalter, "ivan");
        Assign("Lønudtræk-regneark", SystemRole.Forretningsejer, "hanne");

        await db.SaveChangesAsync();
        return;

        Person NewPerson(string name, string department, string? oid = null) => new()
        {
            Id = Guid.CreateVersion7(now),
            DisplayName = name,
            Department = department,
            Email = $"{name.Split(' ')[0].ToLowerInvariant()}@eksempel.invalid",
            EntraObjectId = oid,
        };
    }

    /// <summary>
    /// Fiktive integrationer samlet om "Identitetskilde (fiktiv)", så "hvad rammes" kan demonstreres — inkl. et
    /// DirekteDb-brud, et udtræk til en lokal løsning og en integration via platformen.
    /// </summary>
    private static async Task SeedIntegrationsAsync(EaDbContext db, DateTimeOffset now, ILogger logger)
    {
        // Navne er kun unikke inden for en forælder — tag det første, hvis en udvikler har lavet dubletter.
        var systems = (await db.Systems.ToListAsync()).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First());
        var dataObjects = SeedDataObjects
            .ToDictionary(n => n, n => new DataObject { Id = Guid.CreateVersion7(now), Name = n });
        db.DataObjects.AddRange(dataObjects.Values);

        const string idSource = "Identitetskilde (fiktiv)";
        const string platform = "Integrationsplatform (fiktiv)";
        var added = 0;

        void Add(string from, string to, IntegrationType? type, string? via, string[] data, string? description = null, string? name = null)
        {
            if (!systems.TryGetValue(from, out var source) || !systems.TryGetValue(to, out var target)
                || (via is not null && !systems.ContainsKey(via)))
            {
                LogSkipped(logger, from, to);
                return;
            }

            var integration = new Integration
            {
                Id = Guid.CreateVersion7(now),
                SourceSystemId = source.Id,
                TargetSystemId = target.Id,
                ViaPlatformId = via is null ? null : systems[via].Id,
                Type = type,
                Name = name,
                Description = description,
                CreatedAt = now.AddDays(-60),
                UpdatedAt = now.AddDays(-20),
            };
            integration.DataObjects = data
                .Select(d => new IntegrationDataObject { IntegrationId = integration.Id, DataObjectId = dataObjects[d].Id })
                .ToList();
            db.Integrations.Add(integration);
            added++;
        }

        Add("Nordlys HR", idSource, IntegrationType.Api, platform, ["Medarbejder", "Organisationsenhed"],
            "Nye og ændrede medarbejdere sendes til identitetskilden.");
        Add(idSource, "Kompas Sag", IntegrationType.Event, platform, ["Brugerkonto"]);
        Add(idSource, "Laborant", IntegrationType.DirekteDb, null, ["Brugerkonto"],
            "Laborant læser direkte i identitetskildens database — principbrud, bør erstattes af API.");
        Add(idSource, "Studium", IntegrationType.Api, null, ["Studerende"], "Planlagt sammen med Studium.");
        Add(idSource, "Servicedesk Plus (fiktiv)", IntegrationType.Fil, platform, ["Brugerkonto"], name: "Natlig brugerfil");
        Add("Nordlys HR", "Lønudtræk-regneark", IntegrationType.Udtraek, null, ["Løn", "Medarbejder"],
            "Månedligt udtræk til et regneark hos lønkontoret.");
        Add("Nordlys HR", "Nordlys Økonomi", null, null, ["Medarbejder"]);

        await db.SaveChangesAsync();
        LogSeeded(logger, added);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DevSeed: integrationen {From} → {To} springes over — et system mangler.")]
    private static partial void LogSkipped(ILogger logger, string from, string to);

    [LoggerMessage(Level = LogLevel.Information, Message = "DevSeed: {Count} fiktive integrationer oprettet.")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "DevSeed: {Count} fiktive kapabiliteter oprettet.")]
    private static partial void LogCapabilitiesSeeded(ILogger logger, int count);
}
