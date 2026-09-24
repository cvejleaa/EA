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

    /// <summary>
    /// Sat, når kapabiliteten ikke længere står i kortet, men stadig har koblinger (docs/csv-kapabiliteter.md).
    /// En udgået kapabilitet er løsrevet fra træet og kan ikke få nye koblinger.
    /// </summary>
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>Hvor kapabiliteten sad, da den udgik (fx "Uddannelse › Studieadministration") — så koblingerne kan flyttes.</summary>
    public string? RetiredPath { get; set; }
}

/// <summary>
/// Et system understøtter en kapabilitet. Ingen felter på koblingen (ingen visning bruger dem). Et modul kan kobles
/// selv; en forælder kan også kobles direkte ("hele systemet gør X").
/// </summary>
public sealed class SystemCapability
{
    public Guid SystemId { get; set; }

    public Guid CapabilityId { get; set; }

    public Capability Capability { get; set; } = null!;
}
