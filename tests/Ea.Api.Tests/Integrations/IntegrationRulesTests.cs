using Ea.Api.Integrations;
using Ea.Api.Systems;

namespace Ea.Api.Tests.Integrations;

/// <summary>Én test pr. gren i integrationsreglerne.</summary>
public sealed class IntegrationRulesTests
{
    private static readonly SystemInfo A = new(Guid.NewGuid(), "A", SystemType.Egenudviklet);
    private static readonly SystemInfo B = new(Guid.NewGuid(), "B", SystemType.Saas);
    private static readonly SystemInfo P = new(Guid.NewGuid(), "Platformen", SystemType.Platform);
    private static readonly SystemInfo NotPlatform = new(Guid.NewGuid(), "Kompas", SystemType.Egenudviklet);

    private static Dictionary<string, string[]> Validate(
        Guid? fromId, SystemInfo? from, Guid? toId, SystemInfo? to, Guid? viaId = null, SystemInfo? via = null,
        bool viaChanged = true, string? name = null, string? description = null, int dataObjects = 0) =>
        IntegrationRules.Validate(fromId, from, toId, to, viaId, via, viaChanged, name, description, dataObjects);

    [Fact]
    public void Gyldig_integration_giver_ingen_fejl() =>
        Assert.Empty(Validate(A.Id, A, B.Id, B, P.Id, P));

    [Fact]
    public void Fra_skal_vaelges() =>
        Assert.Equal(["Vælg det system, data sendes fra."], Validate(null, null, B.Id, B)["fromSystemId"]);

    [Fact]
    public void Fra_skal_findes() =>
        Assert.Equal(["Systemet, data sendes fra, findes ikke."], Validate(A.Id, null, B.Id, B)["fromSystemId"]);

    [Fact]
    public void Til_skal_vaelges() =>
        Assert.Equal(["Vælg det system, data sendes til."], Validate(A.Id, A, null, null)["toSystemId"]);

    [Fact]
    public void Til_skal_findes() =>
        Assert.Equal(["Systemet, data sendes til, findes ikke."], Validate(A.Id, A, B.Id, null)["toSystemId"]);

    [Fact]
    public void Et_system_kan_ikke_integrere_med_sig_selv() =>
        Assert.Equal(["En integration kan ikke gå fra et system til sig selv."], Validate(A.Id, A, A.Id, A)["toSystemId"]);

    [Fact]
    public void Platformen_kan_ikke_vaere_afsender() =>
        Assert.Equal(["Platformen kan ikke også være en af enderne."], Validate(A.Id, A, B.Id, B, A.Id, A)["viaPlatformId"]);

    [Fact]
    public void Platformen_kan_ikke_vaere_modtager() =>
        Assert.Equal(["Platformen kan ikke også være en af enderne."], Validate(A.Id, A, B.Id, B, B.Id, B)["viaPlatformId"]);

    [Fact]
    public void Platformen_skal_findes() =>
        Assert.Equal(["Den valgte platform findes ikke."], Validate(A.Id, A, B.Id, B, Guid.NewGuid(), null)["viaPlatformId"]);

    [Fact]
    public void Via_skal_vaere_af_typen_platform() =>
        Assert.Equal(["Kompas er ikke registreret som platform (systemtype Platform)."],
            Validate(A.Id, A, B.Id, B, NotPlatform.Id, NotPlatform)["viaPlatformId"]);

    [Fact]
    public void Uaendret_via_valideres_ikke_igen_selv_om_typen_er_skiftet() =>
        Assert.Empty(Validate(A.Id, A, B.Id, B, NotPlatform.Id, NotPlatform, viaChanged: false));

    [Fact]
    public void Navn_beskrivelse_og_antal_dataobjekter_har_graenser()
    {
        var errors = Validate(A.Id, A, B.Id, B, name: new string('x', 201), description: new string('x', 4001), dataObjects: 51);
        Assert.Equal(["Navnet må højst være 200 tegn."], errors["name"]);
        Assert.Equal(["Beskrivelsen må højst være 4000 tegn."], errors["description"]);
        Assert.Equal(["Højst 50 dataobjekter pr. integration."], errors["dataObjectIds"]);
        // Præcis på grænsen er tilladt.
        Assert.Empty(Validate(A.Id, A, B.Id, B, name: new string('x', 200), description: new string('x', 4000), dataObjects: 50));
    }

    [Theory]
    [InlineData("   ", "Navn skal udfyldes.")]
    [InlineData("Medarbejder | Løn", "Navnet må ikke indeholde tegnet '|'.")]
    public void Dataobjektnavne_valideres(string name, string expected) =>
        Assert.Equal(expected, IntegrationRules.ValidateDataObjectName(name));

    [Fact]
    public void Dataobjektnavn_paa_201_tegn_afvises_og_200_tilladt()
    {
        Assert.Equal("Navnet må højst være 200 tegn.", IntegrationRules.ValidateDataObjectName(new string('x', 201)));
        Assert.Null(IntegrationRules.ValidateDataObjectName(new string('x', 200)));
    }

    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Module = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Other2 = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> Family = new HashSet<Guid> { Parent, Module };

    [Fact]
    public void Relation_ud_naar_familien_sender() =>
        Assert.Equal(IntegrationRelation.Ud, IntegrationRules.RelationTo(Family, Module, Other, null));

    [Fact]
    public void Relation_ind_naar_familien_modtager() =>
        Assert.Equal(IntegrationRelation.Ind, IntegrationRules.RelationTo(Family, Other, Parent, null));

    [Fact]
    public void Relation_intern_naar_begge_ender_er_i_familien() =>
        Assert.Equal(IntegrationRelation.Intern, IntegrationRules.RelationTo(Family, Parent, Module, null));

    [Fact]
    public void Relation_via_naar_familien_kun_er_platform() =>
        Assert.Equal(IntegrationRelation.Via, IntegrationRules.RelationTo(Family, Other, Other2, Parent));

    [Fact]
    public void En_ende_vinder_over_via() =>
        Assert.Equal(IntegrationRelation.Ud, IntegrationRules.RelationTo(Family, Parent, Other, Module));

    [Fact]
    public void Fremmed_integration_hoerer_ikke_til_familien() =>
        Assert.Null(IntegrationRules.RelationTo(Family, Other, Other2, Guid.NewGuid()));

    [Fact]
    public void Dubletbeskeden_peger_paa_den_eksisterende_integration()
    {
        Assert.Equal(
            "Der findes allerede en API-integration fra A til B via Platformen og uden navn. " +
            "Tilføj dataobjekterne til den, eller giv den nye integration et navn, der skelner den.",
            IntegrationRules.DuplicateMessage("A", "B", IntegrationType.Api, "Platformen", null));
        Assert.Equal(
            "Der findes allerede en integration uden angivet type fra A til B uden platform og med navnet \"Natlig fil\". " +
            "Tilføj dataobjekterne til den, eller giv den nye integration et navn, der skelner den.",
            IntegrationRules.DuplicateMessage("A", "B", null, null, "Natlig fil"));
    }
}
