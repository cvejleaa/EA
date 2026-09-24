namespace Ea.Api.Capabilities;

/// <summary>
/// En kapabilitet i kapabilitetskortet (HERM hos DTU): en knude i et træ. Kortet vedligeholdes KUN ved import
/// (docs/csv-kapabiliteter.md); koden er importens nøgle. Niveauet gemmes ikke — det er dybden i træet.
/// </summary>
public sealed class Capability
{
    private string _code = "";

    public Guid Id { get; set; }

    public string Code
    {
        get => _code;
        set
        {
            _code = value.Trim();
            CodeNormalized = CapabilityRules.NormalizeCode(_code);
        }
    }

    /// <summary>Koden uden hensyn til store/små bogstaver — unik, så "ek-1" og "EK-1" er samme kapabilitet.</summary>
    public string CodeNormalized { get; private set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public Guid? ParentId { get; set; }
}
