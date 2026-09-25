using System.Net;
using System.Net.Http.Json;
using Ea.Api.Integrations;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Authorization;

/// <summary>
/// Delopgave 4a: systemejere og systemforvaltere redigerer deres egne systemer og modulerne under dem. Retten kommer
/// fra rollen på systemet, bundet til brugeren via personens oid. Hver afvisning tjekker, at intet blev ændret.
/// </summary>
public sealed class StewardAccessTests
{
    /// <summary>
    /// P (med modulet M) og R er Fridas, når hun får roller på dem; Q er en andens. Enterprise arkitekten opretter alt.
    /// </summary>
    private sealed record World(
        TestApp App, HttpClient Admin, HttpClient Frida, PersonDto Person, SystemDetail P, SystemDetail M, SystemDetail Q, SystemDetail R)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<World> SetupAsync()
    {
        var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        var person = await admin.CreatePersonAsync("Frida Forvalter");
        await app.BindPersonAsync(person.Id, TestUsers.Steward);

        var p = await admin.CreateSystemAsync("Pindsvin");
        var m = await admin.CreateSystemAsync("Pindsvin Modul", p.Id);
        var q = await admin.CreateSystemAsync("Quark");
        var r = await admin.CreateSystemAsync("Ræv");
        return new World(app, admin, await app.ClientFor(TestUsers.Steward), person, p, m, q, r);
    }

    private static async Task GiveRoleAsync(World w, SystemDetail system, SystemRole role)
    {
        var current = await w.Admin.GetSystemAsync(system.Id);
        await (await w.Admin.PutSystemAsync(system.Id, current.ToWrite() with { Roles = [new RoleAssignmentInput(role, w.Person.Id)] }))
            .ExpectAsync(HttpStatusCode.OK);
    }

    /// <summary>Frida gemmer systemet med en ny beskrivelse (alt andet uændret).</summary>
    private static async Task<HttpResponseMessage> EditAsync(World w, SystemDetail system, string description = "Rettet af Frida")
    {
        var current = await w.Admin.GetSystemAsync(system.Id);
        return await w.Frida.PutSystemAsync(system.Id, current.ToWrite() with { Description = description });
    }

    private static async Task<HttpResponseMessage> MoveAsync(World w, SystemDetail system, Guid? toParent)
    {
        var current = await w.Admin.GetSystemAsync(system.Id);
        return await w.Frida.PutSystemAsync(system.Id, current.ToWrite() with { ParentSystemId = toParent });
    }

    private static async Task<List<string>> CandidatesAsync(HttpClient client, Guid forSystemId)
    {
        var response = await client.GetAsync($"/api/systems/parent-candidates?forSystemId={forSystemId}");
        await response.ExpectAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SystemRef>>(TestApp.Json))!.Select(s => s.Name).ToList();
    }

    [Fact]
    public async Task Forvalteren_redigerer_sit_system_og_dets_moduler_men_ikke_andres()
    {
        await using var w = await SetupAsync();

        // Uden rolle: ingen ret (retten kommer fra rollen — ikke fra at være bundet).
        await (await EditAsync(w, w.P)).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Null((await w.Admin.GetSystemAsync(w.P.Id)).Description);

        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);

        await (await EditAsync(w, w.P)).ExpectAsync(HttpStatusCode.OK);
        await (await EditAsync(w, w.M, "Modulet rettet")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Rettet af Frida", (await w.Admin.GetSystemAsync(w.P.Id)).Description);
        Assert.Equal("Modulet rettet", (await w.Admin.GetSystemAsync(w.M.Id)).Description);

        var before = await w.Admin.GetSystemAsync(w.Q.Id);
        await (await EditAsync(w, w.Q)).ExpectAsync(HttpStatusCode.Forbidden);
        var after = await w.Admin.GetSystemAsync(w.Q.Id);
        Assert.Null(after.Description);
        Assert.Equal(before.Version, after.Version);

        // Retten er Fridas — bundet via HENDES oid — ikke enhver indloggets.
        var leo = await w.App.ClientFor(TestUsers.Reader);
        var seenByLeo = await leo.GetSystemAsync(w.P.Id);
        Assert.False(seenByLeo.Permissions.CanEdit);
        await (await leo.PutSystemAsync(w.P.Id, seenByLeo.ToWrite() with { Description = "Leo" })).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Equal("Rettet af Frida", (await w.Admin.GetSystemAsync(w.P.Id)).Description);

        // Knapperne følger samme regel.
        Assert.True((await w.Frida.GetSystemAsync(w.P.Id)).Permissions.CanEdit);
        Assert.True((await w.Frida.GetSystemAsync(w.M.Id)).Permissions.CanEdit);
        Assert.False((await w.Frida.GetSystemAsync(w.Q.Id)).Permissions.CanEdit);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Et_tomt_oid_giver_ingen_ret_heller_ikke_til_en_person_med_tomt_oid(string oid)
    {
        await using var w = await SetupAsync();
        var nobody = await w.Admin.CreatePersonAsync("Tom Identitet");
        await w.App.BindPersonAsync(nobody.Id, oid);
        var current = await w.Admin.GetSystemAsync(w.P.Id);
        await (await w.Admin.PutSystemAsync(w.P.Id, current.ToWrite() with
        {
            Roles = [new RoleAssignmentInput(SystemRole.Systemforvalter, nobody.Id)],
        })).ExpectAsync(HttpStatusCode.OK);

        var client = w.App.ClientWithOid(oid);
        var seen = await client.GetSystemAsync(w.P.Id);
        Assert.False(seen.Permissions.CanEdit);
        await (await client.PutSystemAsync(w.P.Id, seen.ToWrite() with { Description = "Uden identitet" }))
            .ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Null((await w.Admin.GetSystemAsync(w.P.Id)).Description);
    }

    [Theory]
    [InlineData(SystemRole.Systemforvalter, true)]
    [InlineData(SystemRole.Systemejer, true)]
    [InlineData(SystemRole.Forretningsejer, false)] // Ejer processen, men redigerer ikke (beslutning 13).
    public async Task Kun_systemejer_og_systemforvaltere_kan_redigere(SystemRole role, bool canEdit)
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.P, role);

        await (await EditAsync(w, w.P)).ExpectAsync(canEdit ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        Assert.Equal(canEdit ? "Rettet af Frida" : null, (await w.Admin.GetSystemAsync(w.P.Id)).Description);
        Assert.Equal(canEdit, (await w.Frida.GetSystemAsync(w.P.Id)).Permissions.CanEdit);

        var confirm = await w.Frida.PostAsJsonAsync(
            $"/api/systems/{w.P.Id}/confirm", new ConfirmSystemRequest((await w.Admin.GetSystemAsync(w.P.Id)).Version), TestApp.Json);
        await confirm.ExpectAsync(canEdit ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task En_rolle_paa_et_modul_giver_ikke_ret_over_forælderen()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.M, SystemRole.Systemforvalter);

        await (await EditAsync(w, w.M)).ExpectAsync(HttpStatusCode.OK);
        await (await EditAsync(w, w.P)).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Null((await w.Admin.GetSystemAsync(w.P.Id)).Description);
    }

    [Fact]
    public async Task Et_modul_flyttes_kun_til_en_forælder_forvalteren_selv_kan_redigere()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);
        await GiveRoleAsync(w, w.R, SystemRole.Systemforvalter);

        Assert.Equal(["Pindsvin", "Ræv"], await CandidatesAsync(w.Frida, w.M.Id));
        Assert.Equal(["Pindsvin", "Quark", "Ræv"], await CandidatesAsync(w.Admin, w.M.Id));

        await (await MoveAsync(w, w.M, w.Q.Id)).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Equal(w.P.Id, (await w.Admin.GetSystemAsync(w.M.Id)).Parent!.Id);

        await (await MoveAsync(w, w.M, w.R.Id)).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal(w.R.Id, (await w.Admin.GetSystemAsync(w.M.Id)).Parent!.Id);

        // Forælderens forvalter må også gøre modulet selvstændigt.
        await (await MoveAsync(w, w.M, null)).ExpectAsync(HttpStatusCode.OK);
        Assert.Null((await w.Admin.GetSystemAsync(w.M.Id)).Parent);
    }

    [Fact]
    public async Task Et_selvstaendigt_system_goeres_kun_til_modul_af_en_forælder_forvalteren_kan_redigere()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.R, SystemRole.Systemforvalter);
        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);

        await (await MoveAsync(w, w.R, w.Q.Id)).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Null((await w.Admin.GetSystemAsync(w.R.Id)).Parent);

        await (await MoveAsync(w, w.R, w.P.Id)).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal(w.P.Id, (await w.Admin.GetSystemAsync(w.R.Id)).Parent!.Id);
    }

    [Fact]
    public async Task Et_modul_kan_ikke_tages_ud_af_en_forælder_forvalteren_ikke_kan_redigere()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.M, SystemRole.Systemforvalter); // Kun modulet — ikke Pindsvin.
        await GiveRoleAsync(w, w.R, SystemRole.Systemforvalter);

        await (await MoveAsync(w, w.M, null)).ExpectAsync(HttpStatusCode.Forbidden);
        await (await MoveAsync(w, w.M, w.R.Id)).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Equal(w.P.Id, (await w.Admin.GetSystemAsync(w.M.Id)).Parent!.Id);

        // Formularen låser feltet med forklaring, og listen tilbyder kun den nuværende forælder.
        Assert.Equal(
            "Modulet kan kun flyttes af den, der kan redigere Pindsvin.",
            (await w.Frida.GetSystemAsync(w.M.Id)).Permissions.ParentBlockedReason);
        Assert.Equal(["Pindsvin"], await CandidatesAsync(w.Frida, w.M.Id));

        // Arkitekten må godt — og får ingen låsning.
        Assert.Null((await w.Admin.GetSystemAsync(w.M.Id)).Permissions.ParentBlockedReason);
    }

    [Fact]
    public async Task Kun_enterprise_arkitekten_kan_slette_og_forvalteren_faar_at_vide_hvad_hun_goer_i_stedet()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.R, SystemRole.Systemforvalter);

        var seen = (await w.Frida.GetSystemAsync(w.R.Id)).Permissions;
        Assert.True(seen.CanEdit);
        Assert.False(seen.CanDelete);
        Assert.Equal(
            "Kun enterprise arkitekten kan slette systemer. Er systemet taget ud af brug, så sæt status til Nedlagt.",
            seen.DeleteBlockedReason);

        // 403 — ikke 409 "i brug": adgangen afgøres før brugen (og før låsen).
        await (await w.Frida.DeleteAsync($"/api/systems/{w.R.Id}")).ExpectAsync(HttpStatusCode.Forbidden);
        await w.Admin.GetSystemAsync(w.R.Id);

        var adminSees = (await w.Admin.GetSystemAsync(w.R.Id)).Permissions;
        Assert.True(adminSees.CanDelete);
        Assert.Null(adminSees.DeleteBlockedReason);
        await (await w.Admin.DeleteAsync($"/api/systems/{w.R.Id}")).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Forvalteren_redigerer_integrationer_hvor_hendes_system_er_en_ende_men_ikke_via_platformen()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);
        var platform = await w.Admin.CreateSystemAsync(TestApi.NewSystem("Platformen", type: SystemType.Platform));
        await GiveRoleAsync(w, platform, SystemRole.Systemforvalter);

        // Oprettelse: en ende skal være hendes (også et modul under hendes system).
        var own = await w.Frida.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.P.Id, w.Q.Id));
        await w.Frida.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.M.Id, w.Q.Id));
        await (await w.Frida.PostIntegrationAsync(TestApiIntegrations.NewIntegration(w.Q.Id, w.R.Id, via: platform.Id)))
            .ExpectAsync(HttpStatusCode.Forbidden);

        // Hendes system som modtager: hun må rette og slette.
        var incoming = await w.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.Q.Id, w.P.Id));
        Assert.True((await w.Frida.GetIntegrationAsync(incoming.Id)).Permissions.CanEdit);
        await (await w.Frida.PutIntegrationAsync(incoming.Id, incoming.ToUpdate() with { Description = "Frida" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Frida", (await w.Admin.GetIntegrationAsync(incoming.Id)).Description);

        // Kun via hendes platform: ingen ret.
        var viaHer = await w.Admin.CreateIntegrationAsync(TestApiIntegrations.NewIntegration(w.Q.Id, w.R.Id, via: platform.Id));
        Assert.False((await w.Frida.GetIntegrationAsync(viaHer.Id)).Permissions.CanEdit);
        await (await w.Frida.PutIntegrationAsync(viaHer.Id, viaHer.ToUpdate() with { Description = "Frida" }))
            .ExpectAsync(HttpStatusCode.Forbidden);
        await (await w.Frida.DeleteAsync($"/api/integrations/{viaHer.Id}")).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Null((await w.Admin.GetIntegrationAsync(viaHer.Id)).Description);

        // "Tilføj integration" vises kun på hendes egne systemer.
        Assert.True((await w.Frida.SystemIntegrationsAsync(w.P.Id)).CanAdd);
        Assert.False((await w.Frida.SystemIntegrationsAsync(w.Q.Id)).CanAdd);

        await (await w.Frida.DeleteAsync($"/api/integrations/{own.Id}")).ExpectAsync(HttpStatusCode.NoContent);
        await (await w.Frida.DeleteAsync($"/api/integrations/{incoming.Id}")).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Forvalteren_overdrager_systemet_og_skifter_forretningsejer()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);
        var anne = await w.Admin.CreatePersonAsync("Anne Afløser");
        var bo = await w.Admin.CreatePersonAsync("Bo Bogholder");

        // Beslutning P: forvalteren ændrer selv rollerne — også forretningsejeren — og kan give andre ret.
        var current = await w.Frida.GetSystemAsync(w.P.Id);
        await (await w.Frida.PutSystemAsync(w.P.Id, current.ToWrite() with
        {
            Roles =
            [
                new RoleAssignmentInput(SystemRole.Forretningsejer, bo.Id),
                new RoleAssignmentInput(SystemRole.Systemforvalter, anne.Id),
                new RoleAssignmentInput(SystemRole.Systemforvalter, w.Person.Id),
            ],
        })).ExpectAsync(HttpStatusCode.OK);

        var roles = (await w.Admin.GetSystemAsync(w.P.Id)).Roles.Select(r => (r.Role, r.Person.DisplayName)).ToList();
        Assert.Equal(
            [
                (SystemRole.Forretningsejer, "Bo Bogholder"),
                (SystemRole.Systemforvalter, "Anne Afløser"),
                (SystemRole.Systemforvalter, "Frida Forvalter"),
            ],
            roles);
    }

    [Fact]
    public async Task Fjerner_forvalteren_sig_selv_viser_svaret_at_hun_ikke_laengere_kan_redigere()
    {
        await using var w = await SetupAsync();
        await GiveRoleAsync(w, w.P, SystemRole.Systemforvalter);

        var current = await w.Frida.GetSystemAsync(w.P.Id);
        var response = await w.Frida.PutSystemAsync(w.P.Id, current.ToWrite() with { Roles = [] });
        await response.ExpectAsync(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<SystemDetail>(TestApp.Json))!;

        Assert.Empty(saved.Roles);
        Assert.False(saved.Permissions.CanEdit);
        await (await EditAsync(w, w.P)).ExpectAsync(HttpStatusCode.Forbidden);
    }
}
