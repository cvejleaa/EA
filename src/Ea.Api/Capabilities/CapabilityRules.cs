namespace Ea.Api.Capabilities;

/// <summary>Grænser og ordning for kapabilitetskortet — ét sted, brugt af import, eksport og visning.</summary>
public static class CapabilityRules
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;

    /// <summary>Familie → gruppe → kapabilitet, plus ét niveau til DTU's egne underopdelinger.</summary>
    public const int MaxDepth = 4;

    public const int MaxRows = 5000;
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>Fjerner en import mere end denne andel af kortet, advarer tør-kørslen tydeligt (en delvis fil?).</summary>
    public const double LargeRemovalShare = 0.2;

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

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

    /// <summary>Træet i visningsrækkefølge: forælder før børn, søskende efter kode. Dybde 0 = øverste niveau.</summary>
    public static List<(Capability Capability, int Depth)> Ordered(IReadOnlyCollection<Capability> capabilities)
    {
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
