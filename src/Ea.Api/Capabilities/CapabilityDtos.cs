using Ea.Api.Common;

namespace Ea.Api.Capabilities;

/// <summary>
/// En knude i kortet, i visningsrækkefølge. <c>Depth</c> 0 er øverste niveau. <c>Path</c> er navnene ovenover
/// (til vælgeren). <c>Selectable</c>: et system kan få en ny kobling hertil (et blad — samme regel som ved gem).
/// <c>Overlap</c>: serverens vurdering — null for en kapabilitet, der ikke kan vælges (den vurderes ikke).
/// </summary>
public sealed record CapabilityNode(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int Depth,
    string Path,
    bool Selectable,
    CapabilityOverlap? Overlap);

/// <summary>Et system eller modul i en overlap-vurdering. <c>Exclusion</c> er null, når det tæller med.</summary>
public sealed record OverlapMember(Guid Id, string Name, OverlapExclusion? Exclusion);

/// <summary>
/// Overlap på én kapabilitet, som serveren vurderer det (docs/plan.md, beslutning I og J) — klienten regner aldrig
/// selv. <c>Counted</c>: systemer, der tæller (et system med moduler er ét). <c>Planned</c>: systemer, der kun er med
/// som planlagte. <c>Members</c>: alle, der er koblet, i navneorden.
/// </summary>
public sealed record CapabilityOverlap(
    int Counted, int Planned, bool IsOverlap, bool PlannedOnTopOfActive, IReadOnlyList<OverlapMember> Members);

/// <summary>
/// Dækningen (beslutning K): <c>Covered</c> af <c>Total</c> systemer og moduler, nedlagte undtaget. De manglende er
/// præcis systemlistens <c>capabilityId=none</c>.
/// </summary>
public sealed record CouplingCoverage(int Covered, int Total);

/// <summary>Et system, vist med fuldt navn ("Forælder › Modul" for et modul).</summary>
public sealed record CoupledSystem(Guid Id, string Name);

/// <summary>Hvorfor koblinger til en kapabilitet bør flyttes. Danske koder (ekstern kontrakt).</summary>
public enum MoveReason
{
    /// <summary>Kapabiliteten står ikke længere i kortet (se docs/csv-kapabiliteter.md).</summary>
    Udgaaet,

    /// <summary>Kapabiliteten har fået underkapabiliteter; kun blade kan vælges.</summary>
    HarUnderkapabiliteter,
}

/// <summary>
/// Hvorfor et system ikke tæller med i overlap på en kapabilitet. Danske koder (ekstern kontrakt: står i
/// koblings-CSV'en). Gælder flere, vinder status over type, og type over <see cref="Planlagt"/>.
/// </summary>
public enum OverlapExclusion
{
    /// <summary>Endnu ikke i brug. Tæller ikke, men et planlagt system oven på et aktivt skal fanges.</summary>
    Planlagt,

    /// <summary>Systemet eller forælderen er nedlagt.</summary>
    Nedlagt,

    /// <summary>Systemet eller forælderen udfases — en truffet beslutning er ikke en kandidat.</summary>
    Udfases,

    /// <summary>Lokal løsning/udtræk — ikke et system, der løser opgaven for DTU.</summary>
    LokalLoesning,
}

/// <summary>
/// Arbejdslisten efter en ny udgave af kortet: kapabiliteter med koblinger, der bør flyttes — med de systemer, det
/// gælder. <c>Path</c> er, hvor kapabiliteten sidder (for en udgået: hvor den sad).
/// </summary>
public sealed record CapabilityToMove(
    Guid Id, string Code, string Name, string Path, MoveReason Reason, IReadOnlyList<CoupledSystem> Systems);

public sealed record CapabilityTreeResponse(
    IReadOnlyList<CapabilityNode> Items, IReadOnlyList<CapabilityToMove> ToMove, bool CanImport, CouplingCoverage Coverage);

/// <summary>
/// Kapabiliteten, som den vises ved et system. <c>Path</c> er navnene ovenover (for en udgået: hvor den sad).
/// <c>MoveReason</c> er sat, når koblingen bør flyttes (samme regel som kortets arbejdsliste).
/// </summary>
public sealed record CapabilityRef(Guid Id, string Code, string Name, string Path, MoveReason? MoveReason);

/// <summary>
/// En kobling på systemsiden. <c>HeldBy</c> er null for systemets egne koblinger; ellers det familiemedlem, der har
/// koblingen (et modul på forælderens side, forælderen på et moduls side). Kun egne koblinger redigeres her.
/// <c>Overlap</c> er serverens vurdering af kapabiliteten (null, når den ikke vurderes). <c>OwnExclusion</c>: hvorfor
/// koblingens holder ikke tæller med. <c>SharedWith</c>: de ANDRE systemer på kapabiliteten — systemets egne moduler
/// og forælder er ét system med det og står der ikke.
/// </summary>
public sealed record SystemCapabilityDto(
    CapabilityRef Capability,
    Ea.Api.Systems.SystemRef? HeldBy,
    CapabilityOverlap? Overlap,
    OverlapExclusion? OwnExclusion,
    IReadOnlyList<OverlapMember> SharedWith);

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
/// Tallene øverst i tør-kørslen (alle i kapabiliteter, undtagen de to sidste). <c>Removed</c> tæller alt, der
/// slettes — også oprydning af allerede udgåede. <c>CurrentTotal</c> er kortet nu (uden udgåede), og
/// <c>RemovedFromMap</c> er, hvor mange af DEM der forsvinder (slettes eller udgår) — tallet bag "fjerner N af M" og
/// <c>LargeRemoval</c> (en stor del af kortet: typisk en delvis fil, for filen er HELE kortet).
/// <c>CouplingsToMove</c>/<c>SystemsToMove</c>: koblinger (og antal forskellige systemer), der bør flyttes bagefter.
/// </summary>
public sealed record CapabilityImportSummary(
    int New,
    int Changed,
    int Removed,
    int Retired,
    int Reactivated,
    int Unchanged,
    int CurrentTotal,
    int RemovedFromMap,
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

/// <summary>Hvad koblingsimporten gør med en kobling. Danske koder (ekstern kontrakt).</summary>
public enum CouplingChangeKind
{
    Fjernes,
    Tilfoejes,
}

/// <summary>
/// Én kobling, importen fjerner eller tilføjer. <c>ChangedSinceExport</c>: systemet er ændret i registret, efter
/// filen blev hentet — så kan det, der fjernes, være tilføjet siden (docs/csv-koblinger.md).
/// </summary>
public sealed record CouplingChange(
    CouplingChangeKind Kind, CoupledSystem System, string Code, string Name, string Path, bool ChangedSinceExport);

/// <summary>
/// Tallene for en koblingsimport. <c>SystemsNotInFile</c>: systemer med koblinger, der ikke står i filen og derfor
/// ikke røres. <c>LargeRemoval</c>: over en femtedel af koblingerne på systemerne i filen fjernes (en delvis fil?).
/// <c>IgnoredEdits</c>: rækker, hvor en kolonne, der ikke indlæses, er rettet.
/// </summary>
public sealed record CouplingImportSummary(
    int SystemsInFile,
    int SystemsChanged,
    int Added,
    int Removed,
    int Unchanged,
    int SystemsCleared,
    bool LargeRemoval,
    int SystemsChangedSinceExport,
    int SystemsNotInFile,
    int IgnoredEdits);

/// <summary>
/// Svaret på en tør-kørsel eller gennemført koblingsimport. <c>Warnings</c> stopper ikke importen (fx en rettet
/// kolonne, der ikke indlæses). <c>NotInFile</c> er systemerne bag <c>SystemsNotInFile</c>.
/// </summary>
public sealed record CouplingImportResult(
    bool Committed,
    IReadOnlyList<ImportRowError> Errors,
    IReadOnlyList<ImportRowError> Warnings,
    CouplingImportSummary Summary,
    IReadOnlyList<CouplingChange> Changes,
    IReadOnlyList<CoupledSystem> NotInFile,
    string? Fingerprint);
