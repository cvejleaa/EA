using System.Net;
using System.Net.Http.Json;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Systems;

public sealed class SystemEndpointsTests
{
    [Fact]
    public async Task Oprettet_system_kan_hentes_med_alle_felter_og_forretningsejer()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var owner = await admin.CreatePersonAsync("Bo Bogholder", "Økonomi");
        var steward = await admin.CreatePersonAsync("Frida Forvalter", "ITFL");

        var created = await admin.CreateSystemAsync(TestApi.NewSystem(
            "  Nordlys ERP ",
            LifecycleStatus.Indfases,
            teamId: Team.Kerneapplikationer,
            type: SystemType.Saas,
            description: "Økonomi og HR",
            aliases: ["Nordlys", " ERP "],
            roles: [new(SystemRole.Forretningsejer, owner.Id), new(SystemRole.Systemforvalter, steward.Id)]));

        var s = await admin.GetSystemAsync(created.Id);
        Assert.Equal("Nordlys ERP", s.Name);
        Assert.Equal(["Nordlys", "ERP"], s.Aliases);
        Assert.Equal("Økonomi og HR", s.Description);
        Assert.Equal(SystemType.Saas, s.Type);
        Assert.Equal(LifecycleStatus.Indfases, s.LifecycleStatus);
        Assert.Equal(new TeamDto(Team.Kerneapplikationer, "Kerneapplikationer"), s.ManagingTeam);
        Assert.Null(s.Parent);
        Assert.Equal(
            [(SystemRole.Forretningsejer, "Bo Bogholder"), (SystemRole.Systemforvalter, "Frida Forvalter")],
            s.Roles.Select(r => (r.Role, r.Person.DisplayName)));
        Assert.Equal(TestApp.Start, s.CreatedAt);
        Assert.Equal(TestApp.Start, s.LastConfirmedAt);
        Assert.Equal("Eva Arkitekt", s.LastConfirmedByName);

        var item = Assert.Single((await admin.ListSystemsAsync()).Items);
        Assert.Equal("Bo Bogholder", item.BusinessOwner?.DisplayName);
        Assert.Equal("Økonomi", item.BusinessOwner?.Department);
        Assert.Equal("Kerneapplikationer", item.ManagingTeam?.Name);
    }

    [Fact]
    public async Task Navn_og_status_er_obligatoriske()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var response = await admin.PostAsJsonAsync("/api/systems", TestApi.NewSystem("   ") with { LifecycleStatus = null }, TestApp.Json);

        var errors = await response.ValidationErrorsAsync();
        Assert.Equal(["Navn skal udfyldes."], errors["name"]);
        Assert.Equal(["Vælg en livscyklus-status."], errors["lifecycleStatus"]);
        Assert.Equal(0, (await admin.ListSystemsAsync()).Total);
    }

    [Fact]
    public async Task Ukendt_team_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var response = await admin.PostAsJsonAsync("/api/systems", TestApi.NewSystem("X", teamId: Guid.NewGuid()), TestApp.Json);

        Assert.Equal(["Det valgte team findes ikke."], (await response.ValidationErrorsAsync())["managingTeamId"]);
    }

    [Fact]
    public async Task Navn_er_unikt_paa_topniveau_uanset_store_og_smaa_bogstaver()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.CreateSystemAsync("Økonomisuite");

        var response = await admin.PostAsJsonAsync("/api/systems", TestApi.NewSystem("ØKONOMISUITE "), TestApp.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Der findes allerede et system med navnet \"ØKONOMISUITE\".", await response.ProblemDetailAsync());
    }

    [Fact]
    public async Task Modulnavne_er_unikke_inden_for_forælderen_men_ikke_paa_tvaers()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var kompas = await admin.CreateSystemAsync("Kompas");

        await admin.CreateSystemAsync("Rapportering", nordlys.Id);
        await admin.CreateSystemAsync("Rapportering", kompas.Id); // Samme navn under en anden forælder: OK.
        await admin.CreateSystemAsync("Rapportering");            // …og på topniveau: OK.

        var duplicate = await admin.PostAsJsonAsync("/api/systems", TestApi.NewSystem("rapportering", parentId: nordlys.Id), TestApp.Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Nordlys har allerede et modul med navnet \"rapportering\".", await duplicate.ProblemDetailAsync());
        Assert.Equal(5, (await admin.ListSystemsAsync()).Total);
    }

    [Fact]
    public async Task Modulreglerne_haandhaeves_ved_gem()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", nordlys.Id);
        var kompas = await admin.CreateSystemAsync("Kompas");

        async Task<string> ParentError(Guid id, SystemWriteRequest request) =>
            (await (await admin.PutSystemAsync(id, request)).ValidationErrorsAsync())["parentSystemId"].Single();

        var nordlysNow = await admin.GetSystemAsync(nordlys.Id);
        var kompasNow = await admin.GetSystemAsync(kompas.Id);

        Assert.Equal("Et system kan ikke være modul af sig selv.",
            await ParentError(kompas.Id, kompasNow.ToWrite() with { ParentSystemId = kompas.Id }));
        Assert.Equal("HR er selv et modul. Moduler kan kun ligge ét niveau under et system.",
            await ParentError(kompas.Id, kompasNow.ToWrite() with { ParentSystemId = hr.Id }));
        Assert.Equal("Systemet har selv moduler og kan derfor ikke gøres til modul.",
            await ParentError(nordlys.Id, nordlysNow.ToWrite() with { ParentSystemId = kompas.Id }));
        Assert.Equal("Det valgte forældersystem findes ikke.",
            await ParentError(kompas.Id, kompasNow.ToWrite() with { ParentSystemId = Guid.NewGuid() }));

        Assert.Null((await admin.GetSystemAsync(kompas.Id)).Parent);
    }

    [Fact]
    public async Task Forælder_kandidater_foelger_samme_regel_som_gem()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        await admin.CreateSystemAsync("HR", nordlys.Id);
        var kompas = await admin.CreateSystemAsync("Kompas");

        async Task<List<string>> Candidates(string query) =>
            (await admin.GetFromJsonAsync<List<SystemRef>>("/api/systems/parent-candidates" + query, TestApp.Json))!
            .Select(c => c.Name).ToList();

        Assert.Equal(["Kompas", "Nordlys"], await Candidates(""));                     // Nyt system: alle topniveau.
        Assert.Equal(["Nordlys"], await Candidates($"?forSystemId={kompas.Id}"));      // Ikke sig selv, ikke moduler.
        Assert.Empty(await Candidates($"?forSystemId={nordlys.Id}"));                  // Har selv moduler.
    }

    [Fact]
    public async Task System_med_moduler_kan_ikke_slettes_foer_modulerne_er_vaek()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        var hr = await admin.CreateSystemAsync("HR", nordlys.Id);

        var detail = await admin.GetSystemAsync(nordlys.Id);
        Assert.False(detail.Permissions.CanDelete);
        Assert.Equal("Nordlys har moduler (1) — flyt eller slet dem først, eller sæt status til Nedlagt.", detail.Permissions.DeleteBlockedReason);
        Assert.Equal("Systemet har selv moduler og kan derfor ikke gøres til modul.", detail.Permissions.ParentBlockedReason);

        var blocked = await admin.DeleteAsync($"/api/systems/{nordlys.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("Nordlys har moduler (1) — flyt eller slet dem først, eller sæt status til Nedlagt.", await blocked.ProblemDetailAsync());

        await (await admin.DeleteAsync($"/api/systems/{hr.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        var after = await admin.GetSystemAsync(nordlys.Id);
        Assert.True(after.Permissions.CanDelete);
        Assert.Null(after.Permissions.ParentBlockedReason);

        await (await admin.DeleteAsync($"/api/systems/{nordlys.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        Assert.Equal(0, (await admin.ListSystemsAsync()).Total);
    }

    [Fact]
    public async Task Sletning_fjerner_systemets_roller_men_ikke_personen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var bo = await admin.CreatePersonAsync("Bo");
        var system = await admin.CreateSystemAsync(TestApi.NewSystem("X", roles: [new(SystemRole.Forretningsejer, bo.Id)]));

        await (await admin.DeleteAsync($"/api/systems/{system.Id}")).ExpectAsync(HttpStatusCode.NoContent);

        var persons = await admin.GetFromJsonAsync<List<Ea.Api.Persons.PersonDto>>("/api/persons", TestApp.Json);
        Assert.Equal(["Bo"], persons!.Select(p => p.DisplayName));
    }

    [Fact]
    public async Task Redigering_med_foraeldet_version_afvises_og_aendrer_intet()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var original = await admin.CreateSystemAsync("Kompas");

        await (await admin.PutSystemAsync(original.Id, original.ToWrite() with { Description = "Første" })).ExpectAsync(HttpStatusCode.OK);

        // En anden bruger, der stadig har den oprindelige version åben:
        var stale = await admin.PutSystemAsync(original.Id, original.ToWrite() with { Description = "Anden" });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.StartsWith("Systemet er ændret af en anden", await stale.ProblemDetailAsync(), StringComparison.Ordinal);
        Assert.Equal("Første", (await admin.GetSystemAsync(original.Id)).Description);
    }

    [Fact]
    public async Task En_foraeldet_version_afvises_ogsaa_naar_kun_rollerne_aendres()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var bo = await admin.CreatePersonAsync("Bo");
        var seen = await admin.CreateSystemAsync("Kompas");
        await (await admin.PutSystemAsync(seen.Id, seen.ToWrite() with { Description = "Ændret af en anden" }))
            .ExpectAsync(HttpStatusCode.OK);

        // Samme felter som nu og samme klokkeslæt (uret står stille) — kun rollerne (en anden tabel) er nye.
        var stale = await admin.PutSystemAsync(seen.Id, seen.ToWrite() with
        {
            Description = "Ændret af en anden",
            Roles = [new(SystemRole.Forretningsejer, bo.Id)],
        });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty((await admin.GetSystemAsync(seen.Id)).Roles);
    }

    [Fact]
    public async Task Bekraeftelse_af_en_foraeldet_version_afvises_ogsaa_i_samme_oejeblik()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var seen = await admin.CreateSystemAsync("Kompas");
        await (await admin.PostAsJsonAsync($"/api/systems/{seen.Id}/confirm", new ConfirmSystemRequest(seen.Version), TestApp.Json))
            .ExpectAsync(HttpStatusCode.OK);

        // Uret står stille: den forældede bekræftelse ville skrive præcis de samme værdier.
        var stale = await admin.PostAsJsonAsync($"/api/systems/{seen.Id}/confirm", new ConfirmSystemRequest(seen.Version), TestApp.Json);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Redigering_uden_version_afvises()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var system = await admin.CreateSystemAsync("Kompas");

        var response = await admin.PutSystemAsync(system.Id, system.ToWrite() with { Version = null });

        Assert.Equal(["Version mangler."], (await response.ValidationErrorsAsync())["version"]);
    }

    [Fact]
    public async Task Redigering_opdaterer_data_og_taeller_som_bekraeftelse()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var system = await admin.CreateSystemAsync("Kompas");

        app.Time.Advance(TimeSpan.FromDays(3));
        var response = await admin.PutSystemAsync(system.Id, system.ToWrite() with
        {
            Name = "Kompas Sag",
            LifecycleStatus = LifecycleStatus.Udfases,
            ManagingTeamId = Team.Stab,
        });
        await response.ExpectAsync(HttpStatusCode.OK);

        var updated = await admin.GetSystemAsync(system.Id);
        Assert.Equal("Kompas Sag", updated.Name);
        Assert.Equal(LifecycleStatus.Udfases, updated.LifecycleStatus);
        Assert.Equal("Stab", updated.ManagingTeam?.Name);
        Assert.Equal(TestApp.Start, updated.CreatedAt);
        Assert.Equal(TestApp.Start.AddDays(3), updated.UpdatedAt);
        Assert.Equal(TestApp.Start.AddDays(3), updated.LastConfirmedAt);
        Assert.NotEqual(system.Version, updated.Version);
    }

    [Fact]
    public async Task Bekraeft_uaendret_saetter_kun_bekraeftelsen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var system = await admin.CreateSystemAsync(TestApi.NewSystem("Kompas", description: "Uændret"));

        app.Time.Advance(TimeSpan.FromDays(40));
        var response = await admin.PostAsJsonAsync($"/api/systems/{system.Id}/confirm", new ConfirmSystemRequest(system.Version), TestApp.Json);
        await response.ExpectAsync(HttpStatusCode.OK);

        var confirmed = await admin.GetSystemAsync(system.Id);
        Assert.Equal(TestApp.Start.AddDays(40), confirmed.LastConfirmedAt);
        Assert.Equal("Eva Arkitekt", confirmed.LastConfirmedByName);
        Assert.Equal(TestApp.Start, confirmed.UpdatedAt); // Data er ikke ændret.
        Assert.Equal("Uændret", confirmed.Description);

        var item = Assert.Single((await admin.ListSystemsAsync()).Items);
        Assert.Equal(TestApp.Start.AddDays(40), item.LastConfirmedAt);
    }

    [Fact]
    public async Task Man_kan_ikke_bekraefte_en_version_man_ikke_har_set()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var seen = await admin.CreateSystemAsync("Kompas");
        await (await admin.PutSystemAsync(seen.Id, seen.ToWrite() with { Description = "Ændret af en anden" })).ExpectAsync(HttpStatusCode.OK);

        app.Time.Advance(TimeSpan.FromDays(1));
        var stale = await admin.PostAsJsonAsync($"/api/systems/{seen.Id}/confirm", new ConfirmSystemRequest(seen.Version), TestApp.Json);
        var missing = await admin.PostAsJsonAsync($"/api/systems/{seen.Id}/confirm", new ConfirmSystemRequest(null), TestApp.Json);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(["Version mangler."], (await missing.ValidationErrorsAsync())["version"]);
        Assert.Equal(TestApp.Start, (await admin.GetSystemAsync(seen.Id)).LastConfirmedAt);
    }

    [Fact]
    public async Task Roller_valideres_og_erstattes_ved_redigering()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var bo = await admin.CreatePersonAsync("Bo");
        var hanne = await admin.CreatePersonAsync("Hanne");
        var frida = await admin.CreatePersonAsync("Frida");
        var system = await admin.CreateSystemAsync(TestApi.NewSystem("Kompas", roles:
        [
            new(SystemRole.Forretningsejer, bo.Id),
            new(SystemRole.Systemforvalter, frida.Id),
        ]));

        var twoOwners = await admin.PutSystemAsync(system.Id, system.ToWrite() with
        {
            Roles = [new(SystemRole.Forretningsejer, bo.Id), new(SystemRole.Forretningsejer, hanne.Id)],
        });
        Assert.Equal(["Et system kan kun have én forretningsejer."], (await twoOwners.ValidationErrorsAsync())["roles"]);

        var unknown = await admin.PutSystemAsync(system.Id, system.ToWrite() with
        {
            Roles = [new(SystemRole.Systemejer, Guid.NewGuid())],
        });
        Assert.Equal(["En eller flere af de valgte personer findes ikke."], (await unknown.ValidationErrorsAsync())["roles"]);

        // Skift forretningsejer, behold Frida, tilføj Bo som ekstra forvalter.
        await (await admin.PutSystemAsync(system.Id, system.ToWrite() with
        {
            Roles =
            [
                new(SystemRole.Forretningsejer, hanne.Id),
                new(SystemRole.Systemforvalter, frida.Id),
                new(SystemRole.Systemforvalter, bo.Id),
            ],
        })).ExpectAsync(HttpStatusCode.OK);

        var updated = await admin.GetSystemAsync(system.Id);
        Assert.Equal(
            [(SystemRole.Forretningsejer, "Hanne"), (SystemRole.Systemforvalter, "Bo"), (SystemRole.Systemforvalter, "Frida")],
            updated.Roles.Select(r => (r.Role, r.Person.DisplayName)));
    }

    [Fact]
    public async Task Soegning_rammer_navn_alias_beskrivelse_og_forælder()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync(TestApi.NewSystem("Nordlys ERP", aliases: ["Fusion"]));
        await admin.CreateSystemAsync("Indkøb", nordlys.Id);
        await admin.CreateSystemAsync(TestApi.NewSystem("Servicedesk", aliases: ["Serviceportalen"]));
        await admin.CreateSystemAsync(TestApi.NewSystem("Kompas", description: "Håndterer løn og ansættelse"));
        await admin.CreateSystemAsync("Andet");

        async Task<List<string>> Search(string q) =>
            (await admin.ListSystemsAsync("?q=" + Uri.EscapeDataString(q))).Items.Select(i => i.Name).ToList();

        Assert.Equal(["Nordlys ERP", "Indkøb"], await Search("nordlys"));   // Modul via forælderens navn, lige efter forælderen.
        Assert.Equal(["Nordlys ERP", "Indkøb"], await Search("FUSION"));    // Forælderens alias rammer også modulet.
        Assert.Equal(["Kompas"], await Search("løn"));                      // Beskrivelse.
        Assert.Empty(await Search("%"));                                     // Brugerens % er bogstaveligt, ikke et jokertegn.

        var alias = Assert.Single((await admin.ListSystemsAsync("?q=portal")).Items);
        Assert.Equal("Serviceportalen", alias.MatchedAlias);
        var byName = Assert.Single((await admin.ListSystemsAsync("?q=kompas")).Items);
        Assert.Null(byName.MatchedAlias);
    }

    [Fact]
    public async Task Filtre_og_total()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var bo = await admin.CreatePersonAsync("Bo");
        await admin.CreateSystemAsync(TestApi.NewSystem("A", LifecycleStatus.IDrift, teamId: Team.Stab, type: SystemType.Saas,
            roles: [new(SystemRole.Forretningsejer, bo.Id)]));
        await admin.CreateSystemAsync(TestApi.NewSystem("B", LifecycleStatus.Planlagt, type: SystemType.LokalLoesning,
            roles: [new(SystemRole.Systemforvalter, bo.Id)]));
        await admin.CreateSystemAsync(TestApi.NewSystem("C", LifecycleStatus.IDrift, teamId: Team.DigitalArbejdsplads));

        async Task<List<string>> Names(string query)
        {
            var result = await admin.ListSystemsAsync(query);
            Assert.Equal(3, result.Total); // Total er altid hele registret.
            return result.Items.Select(i => i.Name).ToList();
        }

        Assert.Equal(["A", "C"], await Names("?status=IDrift"));
        Assert.Equal(["B"], await Names("?type=LokalLoesning"));
        Assert.Equal(["B"], await Names("?teamId=none"));
        Assert.Equal(["C"], await Names($"?teamId={Team.DigitalArbejdsplads}"));
        Assert.Equal(["B", "C"], await Names("?businessOwnerId=none")); // B har kun en forvalter — ikke en forretningsejer.
        Assert.Equal(["A"], await Names($"?businessOwnerId={bo.Id}"));
        Assert.Equal(["C"], await Names("?status=IDrift&businessOwnerId=none"));

        var invalid = await admin.GetAsync("/api/systems?teamId=stab");
        Assert.Equal(["teamId skal være et id eller 'none'."], (await invalid.ValidationErrorsAsync())["teamId"]);
    }

    [Fact]
    public async Task Moduler_staar_lige_under_deres_forælder_i_listen()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var nordlys = await admin.CreateSystemAsync("Nordlys");
        await admin.CreateSystemAsync("Zeta", nordlys.Id);
        await admin.CreateSystemAsync("Alfa", nordlys.Id);
        await admin.CreateSystemAsync("Kompas");
        await admin.CreateSystemAsync("Østerled");

        var items = (await admin.ListSystemsAsync()).Items;

        Assert.Equal(["Kompas", "Nordlys", "Alfa", "Zeta", "Østerled"], items.Select(i => i.Name));
        Assert.Equal([null, null, "Nordlys", "Nordlys", null], items.Select(i => i.Parent?.Name));
        Assert.Equal([0, 2, 0, 0, 0], items.Select(i => i.ModuleCount));
    }

    [Fact]
    public async Task Ukendt_system_giver_404()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/systems/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutSystemAsync(id, TestApi.NewSystem("X", version: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/systems/{id}")).StatusCode);
    }
}
