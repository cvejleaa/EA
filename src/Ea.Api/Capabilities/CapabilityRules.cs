namespace Ea.Api.Capabilities;

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
