using Ea.Api.Systems;

namespace Ea.Api.Capabilities;

/// <summary>
/// Et system eller modul, der har en kobling til kapabiliteten — det, overlap vurderes på. <c>Name</c> er det
/// fulde navn, <c>GroupKey</c> er <see cref="SystemRules.OverlapGroupKey"/>.
/// </summary>
public sealed record OverlapHolder(
    Guid SystemId, Guid GroupKey, string Name, LifecycleStatus Status, SystemType? Type, LifecycleStatus? ParentStatus);

/// <summary>En holder med årsagen til, at den ikke tæller med (null = tæller med).</summary>
public sealed record OverlapMemberAssessment(OverlapHolder Holder, OverlapExclusion? Exclusion);

/// <summary>
/// Vurderingen af én kapabilitet. <c>Counted</c>: systemer (et system med moduler er ét), der tæller med.
/// <c>Planned</c>: systemer, der kun er med som planlagte. Se docs/plan.md, beslutning I og J.
/// </summary>
public sealed record OverlapAssessment(
    int Counted, int Planned, bool IsOverlap, bool PlannedOnTopOfActive, IReadOnlyList<OverlapMemberAssessment> Members);

/// <summary>Grænser og ordning for kapabilitetskortet — ét sted, brugt af import, eksport og visning.</summary>
public static class CapabilityRules
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    /// <summary>Familie → gruppe → kapabilitet, plus ét niveau til DTU's egne underopdelinger.</summary>
    public const int MaxDepth = 4;

    public const int RetiredPathMaxLength = 1000;

    /// <summary>Et system kobles typisk til en håndfuld kapabiliteter; grænsen er et værn, ikke en norm.</summary>
    public const int MaxCouplingsPerSystem = 100;

    public const int MaxRows = 5000;
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Fjerner en import mere end denne andel af kortet, advarer tør-kørslen tydeligt (en delvis fil?).</summary>
    public const double LargeRemovalShare = 0.2;

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary>
    /// Om et system må få en NY kobling til kapabiliteten: kun blade (overlap giver kun mening på samme niveau) og
    /// ikke udgåede. En eksisterende kobling bevares, selv om kapabiliteten senere udgår eller får børn.
    /// Samme regel bruges ved gem og til <c>Selectable</c> i vælgeren.
    /// </summary>
    public static string? CoupleBlockedReason(Capability capability, bool hasChildren) =>
        capability.RetiredAt is not null
            ? $"\"{capability.Code} {capability.Name}\" er udgået af kortet og kan ikke vælges."
            : hasChildren
                ? $"\"{capability.Code} {capability.Name}\" har underkapabiliteter — vælg den, der passer bedst."
                : null;

    /// <summary>
    /// Hvorfor koblinger til kapabiliteten bør flyttes — eller null. Samme regel som for nye koblinger
    /// (<see cref="CoupleBlockedReason"/>), så arbejdslisten og vælgeren aldrig er uenige.
    /// </summary>
    public static MoveReason? MoveReasonOf(Capability capability, bool hasChildren) =>
        capability.RetiredAt is not null ? MoveReason.Udgaaet
        : hasChildren ? MoveReason.HarUnderkapabiliteter
        : null;

    /// <summary>
    /// Hvorfor et system ikke tæller med i overlap — eller null. Status vinder over type, og type over Planlagt;
    /// et modul arver forælderens Udfases og Nedlagt ("er systemet på vej ud, er dets moduler det også").
    /// </summary>
    public static OverlapExclusion? OverlapExclusionOf(LifecycleStatus status, SystemType? type, LifecycleStatus? parentStatus) =>
        status == LifecycleStatus.Nedlagt || parentStatus == LifecycleStatus.Nedlagt ? OverlapExclusion.Nedlagt
        : status == LifecycleStatus.Udfases || parentStatus == LifecycleStatus.Udfases ? OverlapExclusion.Udfases
        : type == SystemType.LokalLoesning ? OverlapExclusion.LokalLoesning
        : status == LifecycleStatus.Planlagt ? OverlapExclusion.Planlagt
        : null;

    /// <summary>
    /// Overlap på én kapabilitet — den ENESTE funktion, der afgør det (kort, systemside og CSV bruger den). Kun
    /// kapabiliteter, der kan vælges, vurderes (null ellers): koblinger, der bør flyttes, indgår ikke, før de er
    /// flyttet. Medlemmerne er i navneorden.
    /// </summary>
    public static OverlapAssessment? OverlapOf(Capability capability, bool hasChildren, IEnumerable<OverlapHolder> holders)
    {
        if (MoveReasonOf(capability, hasChildren) is not null)
        {
            return null;
        }

        var members = holders
            .Select(h => new OverlapMemberAssessment(h, OverlapExclusionOf(h.Status, h.Type, h.ParentStatus)))
            .OrderBy(m => m.Holder.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Holder.SystemId)
            .ToList();
        var groups = members.GroupBy(m => m.Holder.GroupKey).ToList();
        var counted = groups.Count(g => g.Any(m => m.Exclusion is null));
        var planned = groups.Count(g => g.All(m => m.Exclusion is not null) && g.Any(m => m.Exclusion == OverlapExclusion.Planlagt));
        return new OverlapAssessment(counted, planned, counted >= 2, counted >= 1 && planned >= 1, members);
    }

    /// <summary>Kapabiliteter, der har underkapabiliteter i kortet (udgåede børn tæller ikke) — dvs. ikke er blade.</summary>
    public static HashSet<Guid> WithChildren(IEnumerable<Capability> capabilities) =>
        capabilities.Where(c => c.RetiredAt is null && c.ParentId is not null).Select(c => c.ParentId!.Value).ToHashSet();

    /// <summary>Stien til visning: hvor kapabiliteten sidder — eller, for en udgået, hvor den sad.</summary>
    public static string DisplayPath(Capability capability, IReadOnlyDictionary<Guid, Capability> byId) =>
        capability.RetiredAt is null ? PathOf(capability, byId) : capability.RetiredPath ?? "";

    /// <summary>Navnene fra øverste niveau ned til (men uden) kapabiliteten selv, fx "Uddannelse › Studieadministration".</summary>
    public static string PathOf(Capability capability, IReadOnlyDictionary<Guid, Capability> byId)
    {
        var names = new List<string>();
        var seen = new HashSet<Guid> { capability.Id };
        for (var parent = capability.ParentId; parent is { } id && byId.TryGetValue(id, out var node) && seen.Add(id); parent = node.ParentId)
        {
            names.Add(node.Name);
        }

        names.Reverse();
        return string.Join(" › ", names);
    }

    /// <summary>
    /// Kodernes rækkefølge blandt søskende: tal i koden sammenlignes som tal, så "K1.2" kommer før "K1.10".
    /// </summary>
    public static readonly IComparer<string> CodeOrder = Comparer<string>.Create(CompareCodes);

    private static int CompareCodes(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numberA = a[startA..i].TrimStart('0');
                var numberB = b[startB..j].TrimStart('0');
                var byNumber = numberA.Length != numberB.Length
                    ? numberA.Length.CompareTo(numberB.Length)
                    : string.CompareOrdinal(numberA, numberB);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byLength = (a.Length - i).CompareTo(b.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(a, b);
    }

    /// <summary>
    /// Træet i visningsrækkefølge: forælder før børn, søskende efter kode. Dybde 0 = øverste niveau. Udgåede
    /// kapabiliteter er ikke en del af træet.
    /// </summary>
    public static List<(Capability Capability, int Depth)> Ordered(IReadOnlyCollection<Capability> capabilities)
    {
        capabilities = capabilities.Where(c => c.RetiredAt is null).ToList();
        var ids = capabilities.Select(c => c.Id).ToHashSet();
        var children = capabilities
            .ToLookup(c => c.ParentId is { } parent && ids.Contains(parent) ? parent : (Guid?)null);
        var ordered = new List<(Capability, int)>(capabilities.Count);

        void Visit(Guid? parent, int depth)
        {
            foreach (var child in children[parent].OrderBy(c => c.Code, CodeOrder))
            {
                ordered.Add((child, depth));
                Visit(child.Id, depth + 1);
            }
        }

        Visit(null, 0);
        return ordered;
    }
}
