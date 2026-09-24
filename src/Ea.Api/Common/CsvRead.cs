using System.Text;

namespace Ea.Api.Common;

/// <summary>Én række fra en indlæst CSV-fil. <c>Line</c> er linjen i filen, hvor rækken starter (1 = overskrifterne).</summary>
public sealed record CsvRow(int Line, IReadOnlyList<string> Fields);

/// <summary>En fejl i en indlæst fil, som brugeren kan finde og rette: linje, evt. kolonne og en dansk besked.</summary>
public sealed record ImportRowError(int Line, string? Column, string Message);

/// <summary>Resultatet af at læse en fil: overskrifterne og rækkerne — eller én fejl, der gør filen ulæselig.</summary>
public sealed record CsvReadResult(IReadOnlyList<string> Header, IReadOnlyList<CsvRow> Rows, ImportRowError? Error);

public static partial class Csv
{
    /// <summary>Vejledningen, når filen ikke er UTF-8 — samme råd som i docs/csv-integrationer.md.</summary>
    public const string SaveAsUtf8Advice =
        "Gem filen i Excel som \"CSV UTF-8 (kommasepareret) (*.csv)\" — ikke \"CSV (semikolonsepareret)\", " +
        "som bruger en ældre tegnkodning, hvor æøå går tabt.";

    /// <summary>
    /// Læser en fil i registrets CSV-format (<see cref="Write"/>): strikt UTF-8 (BOM valgfri), semikolon,
    /// citering efter RFC 4180, linjeskift som CRLF, LF eller CR. Apostroffer fra formel-neutraliseringen fjernes
    /// igen (<see cref="Unguard"/>), så Read(Write(x)) giver x — også tomme linjer inde i et felt.
    /// Rækker med kun tomme felter springes over (Excel efterlader dem gerne).
    /// </summary>
    public static CsvReadResult Read(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var content = bytes.AsSpan(start);

        var invalidAt = FirstInvalidUtf8(content);
        if (invalidAt >= 0)
        {
            var badLine = content[..invalidAt].Count((byte)'\n') + 1;
            return Failed(new ImportRowError(badLine, null, $"Filen er ikke gemt som UTF-8. {SaveAsUtf8Advice}"));
        }

        var text = Encoding.UTF8.GetString(content);
        var nul = text.IndexOf('\0', StringComparison.Ordinal);
        if (nul >= 0)
        {
            return Failed(new ImportRowError(LineAt(text, nul), null, "Filen indeholder et ugyldigt tegn (NUL)."));
        }

        var records = new List<CsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var i = 0;

        while (i <= text.Length)
        {
            // Starten af et felt.
            if (i < text.Length && text[i] == '"')
            {
                var quoteLine = line;
                i++;
                while (true)
                {
                    if (i >= text.Length)
                    {
                        return Failed(new ImportRowError(quoteLine, null,
                            "Linjen kan ikke læses: et citationstegn (\") er ikke lukket."));
                    }

                    var c = text[i];
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    if (c == '\n' || (c == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n')))
                    {
                        line++;
                    }

                    field.Append(c);
                    i++;
                }

                if (i < text.Length && text[i] != Separator && text[i] != '\r' && text[i] != '\n')
                {
                    return Failed(new ImportRowError(line, null,
                        "Linjen kan ikke læses: der står tekst efter et afsluttende citationstegn (\")."));
                }
            }
            else
            {
                while (i < text.Length && text[i] != Separator && text[i] != '\r' && text[i] != '\n')
                {
                    field.Append(text[i]);
                    i++;
                }
            }

            fields.Add(field.ToString());
            field.Clear();

            if (i < text.Length && text[i] == Separator)
            {
                i++;
                continue;
            }

            // Slutningen af en række: linjeskift eller filens slutning.
            if (fields.Any(f => f.Length > 0))
            {
                records.Add(new CsvRow(recordLine, fields.Select(Unguard).ToList()));
            }

            fields = [];
            if (i >= text.Length)
            {
                break;
            }

            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            line++;
            recordLine = line;
        }

        return records.Count == 0
            ? new CsvReadResult([], [], null)
            : new CsvReadResult(records[0].Fields, records.GetRange(1, records.Count - 1), null);
    }

    private static CsvReadResult Failed(ImportRowError error) => new([], [], error);

    private static int LineAt(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    /// <summary>Byte-positionen for den første ugyldige UTF-8-sekvens, eller -1.</summary>
    private static int FirstInvalidUtf8(ReadOnlySpan<byte> bytes)
    {
        var position = 0;
        while (position < bytes.Length)
        {
            var status = System.Text.Rune.DecodeFromUtf8(bytes[position..], out _, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                return position;
            }

            position += consumed;
        }

        return -1;
    }
}
