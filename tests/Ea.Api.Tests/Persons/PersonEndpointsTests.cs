using System.Net.Http.Json;
using Ea.Api.Persons;
using Ea.Api.Tests.Infrastructure;

namespace Ea.Api.Tests.Persons;

public sealed class PersonEndpointsTests
{
    [Fact]
    public async Task Person_oprettes_med_afdeling_og_kan_soeges_paa_navn_og_afdeling()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);
        await admin.CreatePersonAsync("Bo Bogholder", "Økonomi");
        await admin.CreatePersonAsync("Hanne Holm", "HR");

        async Task<List<string>> Search(string q) =>
            (await admin.GetFromJsonAsync<List<PersonDto>>("/api/persons?q=" + Uri.EscapeDataString(q), TestApp.Json))!
            .Select(p => p.DisplayName).ToList();

        Assert.Equal(["Bo Bogholder", "Hanne Holm"], await Search(""));
        Assert.Equal(["Bo Bogholder"], await Search("økonomi"));
        Assert.Equal(["Hanne Holm"], await Search("holm"));
    }

    [Fact]
    public async Task Person_valideres()
    {
        await using var app = await TestApp.StartAsync();
        var admin = await app.ClientFor(TestUsers.Admin);

        var response = await admin.PostAsJsonAsync("/api/persons", new CreatePersonRequest(" ", "ikke-en-mail", null), TestApp.Json);

        var errors = await response.ValidationErrorsAsync();
        Assert.Equal(["Navn skal udfyldes."], errors["displayName"]);
        Assert.Equal(["E-mail er ikke gyldig."], errors["email"]);
    }
}
