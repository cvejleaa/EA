using Ea.Api.Capabilities;

namespace Ea.Api.Tests.Capabilities;

public sealed class CapabilityRulesTests
{
    [Fact]
    public void Koder_ordnes_med_tal_som_tal()
    {
        string[] codes = ["K1.10", "K2", "K1.2", "k1.3", "K1", "LT040", "LT005", "K1.2.1"];

        Assert.Equal(["K1", "K1.2", "K1.2.1", "k1.3", "K1.10", "K2", "LT005", "LT040"], codes.Order(CapabilityRules.CodeOrder));
    }

    [Fact]
    public void Traeet_ordnes_forælder_foer_boern_og_en_ukendt_forælder_goer_knuden_til_rod()
    {
        var root = new Capability { Id = Guid.NewGuid(), Code = "B", Name = "B" };
        var child = new Capability { Id = Guid.NewGuid(), Code = "B.1", Name = "B.1", ParentId = root.Id };
        var other = new Capability { Id = Guid.NewGuid(), Code = "A", Name = "A" };
        var orphan = new Capability { Id = Guid.NewGuid(), Code = "C", Name = "C", ParentId = Guid.NewGuid() };

        var ordered = CapabilityRules.Ordered([child, orphan, root, other]);

        Assert.Equal([("A", 0), ("B", 0), ("B.1", 1), ("C", 0)], ordered.Select(o => (o.Capability.Code, o.Depth)));
    }

    [Fact]
    public void Et_udgaaet_barn_goer_ikke_forælderen_til_andet_end_et_blad()
    {
        var parent = new Capability { Id = Guid.NewGuid(), Code = "K1", Name = "K1" };
        var retired = new Capability
        {
            Id = Guid.NewGuid(),
            Code = "K1.1",
            Name = "K1.1",
            ParentId = parent.Id,
            RetiredAt = DateTimeOffset.UnixEpoch,
        };
        var other = new Capability { Id = Guid.NewGuid(), Code = "K2", Name = "K2" };
        var child = new Capability { Id = Guid.NewGuid(), Code = "K2.1", Name = "K2.1", ParentId = other.Id };

        // Kun K2 har et barn i kortet. K1's eneste barn er udgået — også selv om det stadig peger på K1.
        Assert.Equal([other.Id], CapabilityRules.WithChildren([parent, retired, other, child]));
    }
}
