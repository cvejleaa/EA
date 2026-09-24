using Ea.Api.Common;

namespace Ea.Api.Capabilities;

/// <summary>
/// En knude i kortet, i visningsrækkefølge. <c>Depth</c> 0 er øverste niveau. <c>Path</c> er navnene ovenover
/// (til vælgeren). <c>Selectable</c>: et system kan få en ny kobling hertil (et blad — samme regel som ved gem).
/// </summary>
public sealed record CapabilityNode(
    Guid Id, string Code, string Name, string? Description, Guid? ParentId, int Depth, string Path, bool Selectable);

/// <summary>Et system, vist med fuldt navn ("Forælder › Modul" for et modul).</summary>
public sealed record CoupledSystem(Guid Id, string Name);

/// <summary>En kapabilitet, der ikke længere står i kortet, men stadig har koblinger, der skal flyttes.</summary>
public sealed record RetiredCapability(
    Guid Id, string Code, string Name, string? RetiredPath, DateTimeOffset RetiredAt, IReadOnlyList<CoupledSystem> Systems);

public sealed record CapabilityTreeResponse(
    IReadOnlyList<CapabilityNode> Items, IReadOnlyList<RetiredCapability> Retired, bool CanImport);

/// <summary>Kapabiliteten, som den vises ved et system. <c>Path</c> er navnene ovenover (for en udgået: hvor den sad).</summary>
public sealed record CapabilityRef(Guid Id, string Code, string Name, string Path, bool Retired);

/// <summary>
/// En kobling på systemsiden. <c>HeldBy</c> er null for systemets egne koblinger; ellers det familiemedlem, der har
/// koblingen (et modul på forælderens side, forælderen på et moduls side). Kun egne koblinger redigeres her.
/// </summary>
public sealed record SystemCapabilityDto(CapabilityRef Capability, Ea.Api.Systems.SystemRef? HeldBy);

/// <summary>Hvad en import gør ved én kapabilitet. Danske koder, som resten af API'et (ekstern kontrakt).</summary>
public enum CapabilityChangeKind
{
    Ny,
    Aendret,
    Slettes,

    /// <summary>Står ikke i filen, men har koblinger: markeres udgået og bevarer koblingerne.</summary>
    Udgaar,

    /// <summary>En udgået kapabilitet står i filen igen.</summary>
    Genaktiveres,

    /// <summary>Et koblet blad får underkapabiliteter: koblingerne bør flyttes ned på det rigtige blad.</summary>
    FaarUnderkapabiliteter,
}

public sealed record CapabilitySnapshot(string Code, string Name, string? ParentCode, string? Description);

/// <summary>
/// Én ændring med før og efter (Before er null for Ny, After er null for Slettes og Udgaar). <c>AffectedSystems</c>
/// er de systemer, hvis koblinger bør flyttes (ved Udgaar og FaarUnderkapabiliteter — ellers tom).
/// </summary>
public sealed record CapabilityChange(
    CapabilityChangeKind Kind,
    string Code,
    CapabilitySnapshot? Before,
    CapabilitySnapshot? After,
    IReadOnlyList<CoupledSystem> AffectedSystems);

/// <summary>
/// Tallene øverst i tør-kørslen (alle i kapabiliteter, undtagen de to sidste). <c>CurrentTotal</c> er kortet nu
/// (uden udgåede). <c>LargeRemoval</c>: importen fjerner (sletter eller lader udgå) en stor del af kortet —
/// typisk en delvis fil, for filen er HELE kortet. <c>CouplingsToMove</c>/<c>SystemsToMove</c>: koblinger (og
/// antal forskellige systemer), der bør flyttes bagefter.
/// </summary>
public sealed record CapabilityImportSummary(
    int New,
    int Changed,
    int Removed,
    int Retired,
    int Reactivated,
    int Unchanged,
    int CurrentTotal,
    bool LargeRemoval,
    int CouplingsToMove,
    int SystemsToMove);

/// <summary>
/// Svaret fra en tør-kørsel eller en gennemført import. Har filen fejl, er <c>Errors</c> udfyldt, og intet andet
/// er beregnet. <c>Fingerprint</c> (kun fra en fejlfri tør-kørsel) sendes med, når importen gennemføres: så
/// gemmes præcis det, tør-kørslen viste.
/// </summary>
public sealed record CapabilityImportResult(
    bool Committed,
    IReadOnlyList<ImportRowError> Errors,
    CapabilityImportSummary Summary,
    IReadOnlyList<CapabilityChange> Changes,
    string? Fingerprint);
