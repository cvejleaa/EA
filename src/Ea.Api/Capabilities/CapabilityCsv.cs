using Ea.Api.Common;

namespace Ea.Api.Capabilities;

/// <summary>
/// CSV-formatet for kapabilitetskortet — både eksport og import. EKSTERN kontrakt: kolonner og rækkefølge ændres
/// ikke. Spejles af docs/csv-kapabiliteter.md og docs/csv/kapabiliteter-eksempel.csv.
/// Én række pr. knude i træet; <c>ForælderKode</c> er tom for det øverste niveau. Filen er HELE kortet.
/// </summary>
public static class CapabilityCsv
{
    public static readonly IReadOnlyList<string> Header = ["Kode", "Navn", "ForælderKode", "Beskrivelse"];

    /// <summary>Forælder før børn, så filen kan læses som en indholdsfortegnelse — og indlæses igen uændret.</summary>
    public static byte[] Write(IReadOnlyCollection<Capability> capabilities)
    {
        var codes = capabilities.ToDictionary(c => c.Id, c => c.Code);
        return Csv.Write(Header, CapabilityRules.Ordered(capabilities).Select(n => (IReadOnlyList<string?>)
        [
            n.Capability.Code,
            n.Capability.Name,
            n.Capability.ParentId is { } parent ? codes.GetValueOrDefault(parent) : null,
            n.Capability.Description,
        ]));
    }
}
