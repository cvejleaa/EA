using System.Text;

namespace Ea.Api.Common;

/// <summary>
/// CSV til dansk Excel: UTF-8 MED BOM (så æøå vises rigtigt ved dobbeltklik), semikolon som separator,
/// CRLF og citering efter RFC 4180.
/// Formel-neutralisering, så et regneark aldrig udfører indhold fra registret som formel — heller ikke hvis
/// filen åbnes med komma eller tabulator som skilletegn (andre sprogindstillinger):
/// et ' sættes foran = + - @ (og fuldbredde-varianterne) i starten af et felt og lige efter et komma eller en
/// tabulator. En apostrof, der allerede står dér, får også en foran, så <see cref="Read"/> kan fjerne præcis
/// det, skriveren satte: Read(Write(x)) giver altid x (docs/csv-integrationer.md).
/// </summary>
public static partial class Csv
{
    public const char Separator = ';';
    public const char FormulaGuard = '\'';

    private static readonly char[] FormulaStart = ['=', '+', '-', '@', '＝', '＋', '－', '＠', '\t', '\r'];

    /// <summary>Tegn, der får en apostrof foran i starten af et felt: formeltegnene og apostroffen selv.</summary>
    private static readonly char[] GuardedStart = [.. FormulaStart, FormulaGuard];
    private static readonly char[] NeedsQuoting = [Separator, ',', '\t', '"', '\r', '\n'];

    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var text = new StringBuilder();
        AppendRow(text, header);
        foreach (var row in rows)
        {
            if (row.Count != header.Count)
            {
                throw new ArgumentException($"Rækken har {row.Count} felter, men headeren har {header.Count}.", nameof(rows));
            }

            AppendRow(text, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (Array.IndexOf(GuardedStart, value[0]) >= 0)
        {
            value = FormulaGuard + value;
        }

        // Et andet skilletegn (komma, tabulator) kan gøre midten af et felt til starten af en celle.
        value = AfterOtherSeparator().Replace(value, "$0" + FormulaGuard);

        var quote = value.IndexOfAny(NeedsQuoting) >= 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]);
        return quote ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static void AppendRow(StringBuilder text, IReadOnlyList<string?> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                text.Append(Separator);
            }

            text.Append(Field(fields[i]));
        }

        text.Append("\r\n");
    }

    // "-" alene eller før mellemrum (fx dansk "100,- kr.") er ikke en formel og efterlades.
    // En apostrof efter komma/tabulator får også en foran (se GuardedStart).
    [System.Text.RegularExpressions.GeneratedRegex("[,\\t](?=[=+@＝＋＠']|[-－][^\\s])")]
    private static partial System.Text.RegularExpressions.Regex AfterOtherSeparator();

    // Det omvendte: apostroffen, skriveren satte efter et komma eller en tabulator.
    [System.Text.RegularExpressions.GeneratedRegex("(?<=[,\\t])'(?=[=+@＝＋＠']|[-－][^\\s])")]
    private static partial System.Text.RegularExpressions.Regex GuardAfterOtherSeparator();

    /// <summary>Fjerner præcis de apostroffer, <see cref="Field"/> satte (det omvendte af formel-neutraliseringen).</summary>
    public static string Unguard(string value)
    {
        if (value.Length >= 2 && value[0] == FormulaGuard && Array.IndexOf(GuardedStart, value[1]) >= 0)
        {
            value = value[1..];
        }

        return GuardAfterOtherSeparator().Replace(value, "");
    }

    /// <summary>ASCII-venligt filnavn ud fra et systemnavn (æøå omskrives, resten bliver bindestreger).</summary>
    public static string Slug(string name)
    {
        var lower = name.ToLowerInvariant()
            .Replace("æ", "ae", StringComparison.Ordinal)
            .Replace("ø", "oe", StringComparison.Ordinal)
            .Replace("å", "aa", StringComparison.Ordinal);
        var slug = new StringBuilder();
        foreach (var c in lower)
        {
            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        return string.Join('-', slug.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
