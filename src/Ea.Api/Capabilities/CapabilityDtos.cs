using Ea.Api.Common;

namespace Ea.Api.Capabilities;

/// <summary>En knude i kortet, i visningsrækkefølge. <c>Depth</c> 0 er øverste niveau.</summary>
public sealed record CapabilityNode(Guid Id, string Code, string Name, string? Description, Guid? ParentId, int Depth);

public sealed record CapabilityTreeResponse(IReadOnlyList<CapabilityNode> Items, bool CanImport);

/// <summary>Hvad en import gør ved én kapabilitet. Danske koder, som resten af API'et (ekstern kontrakt).</summary>
public enum CapabilityChangeKind
{
    Ny,
    Aendret,
    Slettes,
}

public sealed record CapabilitySnapshot(string Code, string Name, string? ParentCode, string? Description);

/// <summary>Én ændring med før og efter (Before er null for Ny, After er null for Slettes).</summary>
public sealed record CapabilityChange(CapabilityChangeKind Kind, string Code, CapabilitySnapshot? Before, CapabilitySnapshot? After);

/// <summary>
/// Tallene øverst i tør-kørslen. <c>LargeRemoval</c>: importen fjerner en stor del af kortet — typisk en delvis
/// fil, for filen er HELE kortet.
/// </summary>
public sealed record CapabilityImportSummary(int New, int Changed, int Removed, int Unchanged, int CurrentTotal, bool LargeRemoval);

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
