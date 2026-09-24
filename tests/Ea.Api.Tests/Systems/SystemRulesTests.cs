using Ea.Api.Systems;

namespace Ea.Api.Tests.Systems;

/// <summary>Én test pr. gren i reglerne, så hver gren kan dræbes af en mutation for sig.</summary>
public sealed class SystemRulesTests
{
    private static readonly Guid Self = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void Ingen_forælder_er_altid_gyldig() =>
        Assert.Null(SystemRules.ValidateParent(Self, moduleCount: 3, proposedParentId: null, proposedParent: null));

    [Fact]
    public void Et_system_kan_ikke_vaere_sin_egen_forælder() =>
        Assert.Equal("Et system kan ikke være modul af sig selv.",
            SystemRules.ValidateParent(Self, 0, Self, new ParentInfo(Self, "X", null)));

    [Fact]
    public void Forælderen_skal_findes() =>
        Assert.Equal("Det valgte forældersystem findes ikke.",
            SystemRules.ValidateParent(Self, 0, Other, proposedParent: null));

    [Fact]
    public void Forælderen_maa_ikke_selv_vaere_et_modul() =>
        Assert.Equal("Nordlys HR er selv et modul. Moduler kan kun ligge ét niveau under et system.",
            SystemRules.ValidateParent(Self, 0, Other, new ParentInfo(Other, "Nordlys HR", Guid.NewGuid())));

    [Fact]
    public void Et_system_med_moduler_kan_ikke_blive_modul() =>
        Assert.Equal("Systemet har selv moduler og kan derfor ikke gøres til modul.",
            SystemRules.ValidateParent(Self, 1, Other, new ParentInfo(Other, "Nordlys", null)));

    [Fact]
    public void Topniveau_system_uden_moduler_kan_blive_modul() =>
        Assert.Null(SystemRules.ValidateParent(Self, 0, Other, new ParentInfo(Other, "Nordlys", null)));

    [Fact]
    public void Sletning_blokeres_kun_af_moduler()
    {
        Assert.Null(SystemRules.DeleteBlockedReason("Nordlys", 0));
        Assert.Equal("Nordlys har moduler (1) — flyt eller slet dem først.", SystemRules.DeleteBlockedReason("Nordlys", 1));
    }

    [Fact]
    public void Samme_person_i_samme_rolle_to_gange_afvises()
    {
        var p = Guid.NewGuid();
        Assert.Equal("Samme person har samme rolle flere gange.", SystemRules.ValidateRoles(
        [
            new(SystemRole.Systemforvalter, p),
            new(SystemRole.Systemforvalter, p),
        ]));
    }

    [Theory]
    [InlineData(SystemRole.Forretningsejer, "Et system kan kun have én forretningsejer.")]
    [InlineData(SystemRole.Systemejer, "Et system kan kun have én systemejer.")]
    public void Ejerroller_har_hoejst_een_indehaver(SystemRole role, string expected) =>
        Assert.Equal(expected, SystemRules.ValidateRoles(
        [
            new(role, Guid.NewGuid()),
            new(role, Guid.NewGuid()),
        ]));

    [Fact]
    public void Flere_systemforvaltere_og_samme_person_i_flere_roller_er_tilladt()
    {
        var p = Guid.NewGuid();
        Assert.Null(SystemRules.ValidateRoles(
        [
            new(SystemRole.Systemforvalter, p),
            new(SystemRole.Systemforvalter, Guid.NewGuid()),
            new(SystemRole.Forretningsejer, p),
            new(SystemRole.Systemejer, p),
        ]));
    }

    [Fact]
    public void Aliaser_trimmes_og_tomme_dubletter_og_navnet_fjernes() =>
        Assert.Equal(["Nordlys", "ERP"], SystemRules.NormalizeAliases(
            ["  Nordlys ", "", "   ", "nordlys", "ERP", "Nordlys ERP", "nordlys erp "], " Nordlys ERP "));
}
