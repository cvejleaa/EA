using System.Text;

namespace Ea.Api.Common;

/// <summary>
/// CSV til dansk Excel: UTF-8 MED BOM (så æøå vises rigtigt ved dobbeltklik), semikolon som separator,
/// CRLF og citering efter RFC 4180. Felter, der starter med = + - @ TAB eller CR, får et foranstillet '
/// (formel-neutralisering), så et regneark aldrig udfører indhold fra registret som formel. Importen fjerner det igen.
/// </summary>
public static class Csv
{
    public const char Separator = ';';
    public const char FormulaGuard = '\'';

    private static readonly char[] FormulaStart = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuoting = [Separator, '"', '\r', '\n'];

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

        if (Array.IndexOf(FormulaStart, value[0]) >= 0)
        {
            value = FormulaGuard + value;
        }

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
