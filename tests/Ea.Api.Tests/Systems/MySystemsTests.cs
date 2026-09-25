using System.Net;
using System.Net.Http.Json;
using Ea.Api.CurrentUser;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Systems;

/// <summary>
/// Delopgave 4b-1: "Mine systemer" (alle roller og deres moduler, ældst bekræftede først), menuens tal, "Din rolle",
/// hvem der kan redigere et system, og om man kan redigere via forælderen.
/// </summary>
public sealed class MySystemsTests
{
    private static async Task<(TestApp App, HttpClient Admin, HttpClient Frida, PersonDto Person)> SetupAsync()
    {
        var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var person = await admin.CreatePersonAsync("Frida Forvalter");
        await app.BindPersonAsync(person.Id, TestUsers.Steward);
        return (app, admin, await app.ClientFor(TestUsers.Steward), person);
    }

    private static async Task SetRolesAsync(HttpClient admin, SystemDetail system, params (SystemRole Role, Guid PersonId)[] roles)
    {
        var current = await admin.GetSystemAsync(system.Id);
        var response = await admin.PutSystemAsync(system.Id, current.ToWrite() with
        {
            Roles = roles.Select(r => new RoleAssignmentInput(r.Role, r.PersonId)).ToList(),
        });
        await response.ExpectAsync(HttpStatusCode.OK);
    }

    private static async Task<MeResponse> MeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(TestApp.Json))!;
    }

    [Fact]
    public async Task Mine_systemer_er_alle_roller_og_deres_moduler_aeldst_bekraeftede_foerst_og_nedlagte_sidst()
    {
        var (app, admin, frida, person) = await SetupAsync();
        await using var _ = app;
        var other = await admin.CreatePersonAsync("Ole Anden");

        // Oprettelse og redigering sætter "sidst bekræftet"; tiden flyttes mellem hvert system, så rækkefølgen er entydig.
        var owner = await admin.CreateSystemAsync("Ejer-system");                    // forretningsejer → med
        await SetRolesAsync(admin, owner, (SystemRole.Forretningsejer, person.Id));
        app.Time.Advance(TimeSpan.FromDays(1));
        await admin.CreateSystemAsync("Ejer-modul", owner.Id);                        // modul under hendes → med (via)
        app.Time.Advance(TimeSpan.FromDays(1));
        var retired = await admin.CreateSystemAsync(TestApi.NewSystem("Nedlagt-system", LifecycleStatus.Nedlagt));
        await SetRolesAsync(admin, retired, (SystemRole.Systemforvalter, person.Id)); // nedlagt → med, men sidst
        app.Time.Advance(TimeSpan.FromDays(1));
        var systemOwner = await admin.CreateSystemAsync("Systemejer-system");         // systemejer → med
        await SetRolesAsync(admin, systemOwner, (SystemRole.Systemejer, person.Id), (SystemRole.Forretningsejer, person.Id));
        app.Time.Advance(TimeSpan.FromDays(1));
        var foreign = await admin.CreateSystemAsync("Fremmed");                       // en andens → ikke med
        await SetRolesAsync(admin, foreign, (SystemRole.Systemforvalter, other.Id));
        var foreignModule = await admin.CreateSystemAsync("Fremmed-modul", foreign.Id); // hendes rolle på modulet → med
        await SetRolesAsync(admin, foreignModule, (SystemRole.Systemforvalter, person.Id), (SystemRole.Forretningsejer, person.Id));
        app.Time.Advance(TimeSpan.FromDays(1));

        // Ejer-systemet (det ældste) bekræftes nu og rykker dermed nederst blandt de aktive.
        var seen = await admin.GetSystemAsync(owner.Id);
        await (await admin.PostAsJsonAsync($"/api/systems/{owner.Id}/confirm", new ConfirmSystemRequest(seen.Version), TestApp.Json))
            .ExpectAsync(HttpStatusCode.OK);

        var mine = await frida.ListSystemsAsync("?mine=true");

        Assert.Equal(
            [
                ("Ejer-modul", "–"),
                ("Systemejer-system", "Forretningsejer, Systemejer"),
                ("Fremmed-modul", "Forretningsejer, Systemforvalter"),
                ("Ejer-system", "Forretningsejer"),
                ("Nedlagt-system", "Systemforvalter"),
            ],
            mine.Items.Select(i => (i.Name, i.MyRoles!.Count == 0 ? "–" : string.Join(", ", i.MyRoles))));
        Assert.DoesNotContain(mine.Items, i => i.Name == "Fremmed");

        // Menuens tal og personen følger SAMME regel som listen: 5 systemer — ikke hendes 6 roller (to systemer har to
        // roller hver, og modulet via forælderen har ingen), så et tal efter en anden regel bliver rødt.
        var me = await MeAsync(frida);
        Assert.Equal(mine.Items.Count, me.MySystemCount);
        Assert.Equal(5, me.MySystemCount);
        Assert.Equal(person.Id, me.PersonId);

        // Uden mine: alle systemer og ingen "Din rolle".
        var all = await frida.ListSystemsAsync();
        Assert.Equal(6, all.Items.Count);
        Assert.All(all.Items, i => Assert.Null(i.MyRoles));
    }

    [Fact]
    public async Task Mine_kombineres_med_de_andre_filtre()
    {
        var (app, admin, frida, person) = await SetupAsync();
        await using var _ = app;
        var active = await admin.CreateSystemAsync("Aktivt");
        var planned = await admin.CreateSystemAsync(TestApi.NewSystem("Planlagt", LifecycleStatus.Planlagt));
        await admin.CreateSystemAsync("Andres");
        await SetRolesAsync(admin, active, (SystemRole.Systemforvalter, person.Id));
        await SetRolesAsync(admin, planned, (SystemRole.Systemforvalter, person.Id));

        var list = await frida.ListSystemsAsync("?mine=true&status=IDrift");

        Assert.Equal(["Aktivt"], list.Items.Select(i => i.Name));
        Assert.Equal(["Planlagt"], (await frida.ListSystemsAsync("?mine=true&q=planl")).Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Samme_bekraeftelse_sorteres_efter_navn()
    {
        var (app, admin, frida, person) = await SetupAsync();
        await using var _ = app;

        // Samme tidspunkt (uret står stille): navnet afgør. Oprettet i omvendt orden, så rækkefølgen ikke er tilfældig.
        foreach (var name in new[] { "Cirkel", "Alfa", "Bue" })
        {
            var system = await admin.CreateSystemAsync(name);
            await SetRolesAsync(admin, system, (SystemRole.Systemforvalter, person.Id));
        }

        Assert.Equal(["Alfa", "Bue", "Cirkel"], (await frida.ListSystemsAsync("?mine=true")).Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Uden_egne_roller_eller_uden_identitet_er_der_ingen_mine_systemer()
    {
        var (app, admin, _, _) = await SetupAsync();
        await using var __ = app;
        var system = await admin.CreateSystemAsync("Noget");

        // Enterprise arkitekten må alt, men har ingen egne roller: listen er tom, ikke "alle".
        Assert.Empty((await admin.ListSystemsAsync("?mine=true")).Items);
        var me = await MeAsync(admin);
        Assert.Equal(0, me.MySystemCount);
        Assert.Null(me.PersonId);

        // Et blankt oid matcher aldrig en person, heller ikke en med blankt Oid.
        var blank = await admin.CreatePersonAsync("Tom Identitet");
        await app.BindPersonAsync(blank.Id, "");
        await SetRolesAsync(admin, system, (SystemRole.Systemforvalter, blank.Id));
        var client = app.ClientWithOid("");
        Assert.Empty((await client.ListSystemsAsync("?mine=true")).Items);
        Assert.Equal(0, (await MeAsync(client)).MySystemCount);
        Assert.Null((await MeAsync(client)).PersonId);
    }

    [Fact]
    public async Task Systemsiden_viser_hvem_der_kan_redigere_ogsaa_via_forælderen()
    {
        var (app, admin, _, _) = await SetupAsync();
        await using var _ = app;
        var anne = await admin.CreatePersonAsync("Anne Ejer");
        var bo = await admin.CreatePersonAsync("Bo Forvalter");
        var cille = await admin.CreatePersonAsync("Cille Modulforvalter");
        var dan = await admin.CreatePersonAsync("Dan Forretning");
        var parent = await admin.CreateSystemAsync("Forælder");
        var module = await admin.CreateSystemAsync("Modul", parent.Id);
        await SetRolesAsync(admin, parent,
            (SystemRole.Systemejer, anne.Id), (SystemRole.Systemforvalter, bo.Id), (SystemRole.Forretningsejer, dan.Id),
            (SystemRole.Systemforvalter, cille.Id));
        await SetRolesAsync(admin, module, (SystemRole.Systemforvalter, cille.Id), (SystemRole.Forretningsejer, dan.Id));

        var editors = (await admin.GetSystemAsync(module.Id)).Editors;

        // Egne først; forælderens med "via"; en person kun én gang; forretningsejeren redigerer ikke.
        Assert.Equal(
            [("Cille Modulforvalter", null), ("Anne Ejer", "Forælder"), ("Bo Forvalter", "Forælder")],
            editors.Select(e => (e.Person.DisplayName, e.Via?.Name)));
        Assert.Equal(
            ["Anne Ejer", "Bo Forvalter", "Cille Modulforvalter"],
            (await admin.GetSystemAsync(parent.Id)).Editors.Select(e => e.Person.DisplayName));
    }

    [Fact]
    public async Task Ret_via_forælderen_saettes_af_serveren()
    {
        var (app, admin, frida, person) = await SetupAsync();
        await using var _ = app;
        var parent = await admin.CreateSystemAsync("Forælder");
        var module = await admin.CreateSystemAsync("Modul", parent.Id);
        var other = await admin.CreateSystemAsync("Andet");
        var otherModule = await admin.CreateSystemAsync("Andet modul", other.Id);
        await SetRolesAsync(admin, parent, (SystemRole.Systemforvalter, person.Id));
        await SetRolesAsync(admin, otherModule, (SystemRole.Systemforvalter, person.Id));

        // Forvalter på forælderen: kan redigere modulet via forælderen, også uden rolle på modulet.
        Assert.True((await frida.GetSystemAsync(module.Id)).Permissions.CanEditViaParent);
        // Kun rolle på modulet: kan redigere modulet, men ikke via forælderen.
        var own = (await frida.GetSystemAsync(otherModule.Id)).Permissions;
        Assert.True(own.CanEdit);
        Assert.False(own.CanEditViaParent);
        // Et selvstændigt system har ingen forælder.
        Assert.False((await admin.GetSystemAsync(parent.Id)).Permissions.CanEditViaParent);
    }
}
