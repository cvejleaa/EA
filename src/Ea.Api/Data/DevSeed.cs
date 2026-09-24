using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Data;

/// <summary>
/// FIKTIVE eksempeldata til lokal udvikling. Ingen rigtige DTU-systemer, -personer eller -integrationer:
/// fiktive oplysninger om et rigtigt system bliver forvekslet med fakta.
/// </summary>
public static class DevSeed
{
    /// <summary>Samme oid som dev-brugeren "frida" i appsettings.Development.json (forberedt til delopgave 4).</summary>
    public const string FridaOid = "00000000-0000-0000-0000-00000000f001";

    public static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EaDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await db.Database.MigrateAsync();
        if (await db.Systems.AnyAsync())
        {
            return;
        }

        var now = time.GetUtcNow();
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
}
