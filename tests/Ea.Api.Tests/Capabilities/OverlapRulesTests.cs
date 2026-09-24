using Ea.Api.Capabilities;
using Ea.Api.Systems;
using static Ea.Api.Systems.LifecycleStatus;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// Overlap-reglen (docs/plan.md, beslutning I og J): hver undtagelse alene, forrangen, når flere gælder, og
/// kombinationer med tre eller flere systemer. Hver case er valgt, så fjernelse af én betingelse vender resultatet.
/// </summary>
public sealed class OverlapRulesTests
{
    private static readonly Capability Leaf = new() { Id = Guid.NewGuid(), Code = "K1.1", Name = "Optagelse" };

    private static readonly Guid Parent = Guid.NewGuid();

    /// <summary>Et selvstændigt system.</summary>
    private static OverlapHolder S(string name, LifecycleStatus status, SystemType? type = null)
    {
        var id = Guid.NewGuid();
        return new OverlapHolder(id, SystemRules.OverlapGroupKey(id, null), name, status, type, null);
    }

    /// <summary>Et modul af <see cref="Parent"/> (forælderens status som givet).</summary>
    private static OverlapHolder M(string name, LifecycleStatus status, LifecycleStatus parentStatus = IDrift)
    {
        var id = Guid.NewGuid();
        return new OverlapHolder(id, SystemRules.OverlapGroupKey(id, Parent), name, status, null, parentStatus);
    }

    private static (int Counted, int Planned, bool IsOverlap, bool PlannedOnTopOfActive) Assess(params OverlapHolder[] holders)
    {
        var result = CapabilityRules.OverlapOf(Leaf, hasChildren: false, holders)!;
        return (result.Counted, result.Planned, result.IsOverlap, result.PlannedOnTopOfActive);
    }

    public static TheoryData<LifecycleStatus, SystemType?, LifecycleStatus?, OverlapExclusion?> Exclusions => new()
    {
        // Hver årsag alene.
        { IDrift, null, null, null },
        { Indfases, SystemType.Saas, null, null },
        { Planlagt, null, null, OverlapExclusion.Planlagt },
        { Udfases, null, null, OverlapExclusion.Udfases },
        { Nedlagt, null, null, OverlapExclusion.Nedlagt },
        { IDrift, SystemType.LokalLoesning, null, OverlapExclusion.LokalLoesning },

        // Et modul arver forælderens Udfases og Nedlagt — men ikke Planlagt.
        { IDrift, null, Udfases, OverlapExclusion.Udfases },
        { IDrift, null, Nedlagt, OverlapExclusion.Nedlagt },
        { IDrift, null, Planlagt, null },

        // Forrang: status over type, type over Planlagt.
        { Udfases, SystemType.LokalLoesning, null, OverlapExclusion.Udfases },
        { Nedlagt, SystemType.LokalLoesning, null, OverlapExclusion.Nedlagt },
        { Planlagt, SystemType.LokalLoesning, null, OverlapExclusion.LokalLoesning },
        { Planlagt, null, Udfases, OverlapExclusion.Udfases },
        { IDrift, SystemType.LokalLoesning, Nedlagt, OverlapExclusion.Nedlagt },
    };

    [Theory]
    [MemberData(nameof(Exclusions))]
    public void Aarsagen_til_at_et_system_ikke_taeller(
        LifecycleStatus status, SystemType? type, LifecycleStatus? parentStatus, OverlapExclusion? expected) =>
        Assert.Equal(expected, CapabilityRules.OverlapExclusionOf(status, type, parentStatus));

    [Fact]
    public void To_systemer_i_drift_eller_indfasning_er_overlap_et_alene_er_ikke()
    {
        Assert.Equal((2, 0, true, false), Assess(S("A", IDrift), S("B", Indfases)));
        Assert.Equal((1, 0, false, false), Assess(S("A", IDrift)));
        Assert.Equal((3, 0, true, false), Assess(S("A", IDrift), S("B", IDrift), S("C", Indfases)));
    }

    [Fact]
    public void En_planlagt_udskiftning_er_ikke_overlap()
    {
        // Indfases + Udfases: beslutningen er truffet.
        Assert.Equal((1, 0, false, false), Assess(S("Ny", Indfases), S("Gammel", Udfases)));
        Assert.Equal((1, 0, false, false), Assess(S("A", IDrift), S("B", Nedlagt), S("C", IDrift, SystemType.LokalLoesning)));
    }

    [Fact]
    public void En_lokal_loesning_ved_siden_af_to_i_drift_aendrer_ikke_tallet()
    {
        Assert.Equal((2, 0, true, false), Assess(S("A", IDrift), S("B", Indfases), S("Udtræk", IDrift, SystemType.LokalLoesning)));
    }

    [Fact]
    public void Et_planlagt_system_oven_paa_et_aktivt_fanges()
    {
        Assert.Equal((1, 1, false, true), Assess(S("A", IDrift), S("B", Udfases), S("C", Planlagt)));
    }

    [Fact]
    public void To_planlagte_uden_et_aktivt_taelles_men_markeres_ikke()
    {
        // Båndet: Planned er det rå antal (2), men mærket kræver et aktivt system (false).
        Assert.Equal((0, 2, false, false), Assess(S("A", Planlagt), S("B", Planlagt)));
    }

    [Fact]
    public void Et_system_og_dets_moduler_er_et_system()
    {
        var parent = new OverlapHolder(Parent, Parent, "Nordlys", IDrift, null, null);

        Assert.Equal((1, 0, false, false), Assess(parent, M("Nordlys > HR", IDrift)));
        Assert.Equal((1, 0, false, false), Assess(M("Nordlys > HR", IDrift), M("Nordlys > Løn", IDrift)));
        Assert.Equal((2, 0, true, false), Assess(M("Nordlys > HR", IDrift), S("Kompas", IDrift)));
    }

    [Fact]
    public void Et_planlagt_modul_i_et_aktivt_system_er_ikke_planlagt_men_er_det_ved_siden_af_et_andet_system()
    {
        Assert.Equal((1, 0, false, false), Assess(M("Nordlys > Ny", Planlagt), M("Nordlys > HR", IDrift)));
        Assert.Equal((1, 1, false, true), Assess(M("Nordlys > Ny", Planlagt), S("Kompas", IDrift)));
    }

    [Fact]
    public void Et_modul_i_et_system_der_udfases_taeller_ikke()
    {
        Assert.Equal((1, 0, false, false), Assess(M("Nordlys > HR", IDrift, parentStatus: Udfases), S("Kompas", IDrift)));
    }

    [Fact]
    public void Kun_kapabiliteter_der_kan_vaelges_vurderes()
    {
        var retired = new Capability { Id = Guid.NewGuid(), Code = "K9", Name = "Gammel", RetiredAt = DateTimeOffset.UnixEpoch };
        OverlapHolder[] holders = [S("A", IDrift), S("B", IDrift)];

        Assert.Null(CapabilityRules.OverlapOf(retired, hasChildren: false, holders));
        Assert.Null(CapabilityRules.OverlapOf(Leaf, hasChildren: true, holders));
        Assert.True(CapabilityRules.OverlapOf(Leaf, hasChildren: false, holders)!.IsOverlap);
    }

    [Fact]
    public void Medlemmerne_staar_i_navneorden_med_aarsag()
    {
        var result = CapabilityRules.OverlapOf(Leaf, false, [S("Ugle", Udfases), S("Kompas", IDrift), S("Rune", Planlagt)])!;

        Assert.Equal(
            [("Kompas", (OverlapExclusion?)null), ("Rune", OverlapExclusion.Planlagt), ("Ugle", OverlapExclusion.Udfases)],
            result.Members.Select(m => (m.Holder.Name, m.Exclusion)));
    }
}
