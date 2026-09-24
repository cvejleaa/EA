using System.Text;
using Microsoft.VisualBasic.FileIO;

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
    /// citering efter RFC 4180. Apostroffer fra formel-neutraliseringen fjernes igen (<see cref="Unguard"/>).
    /// Tomme linjer og linjer med kun tomme felter springes over (Excel efterlader dem gerne).
    /// </summary>
    public static CsvReadResult Read(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var content = bytes.AsSpan(start);

        var invalidAt = FirstInvalidUtf8(content);
        if (invalidAt >= 0)
        {
            var line = content[..invalidAt].Count((byte)'\n') + 1;
            return Failed(new ImportRowError(line, null, $"Filen er ikke gemt som UTF-8. {SaveAsUtf8Advice}"));
        }

        var text = Encoding.UTF8.GetString(content);
        var lineCount = text.Length == 0 ? 0 : text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1);

        var records = new List<CsvRow>();
        using var parser = new TextFieldParser(new StringReader(text))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(Separator.ToString());

        while (!parser.EndOfData)
        {
            string[] fields;
            try
            {
                fields = parser.ReadFields() ?? [];
            }
            catch (MalformedLineException ex)
            {
                return Failed(new ImportRowError((int)ex.LineNumber, null,
                    "Linjen kan ikke læses: et citationstegn (\") er ikke lukket."));
            }

            // Parseren springer tomme linjer over, så startlinjen regnes baglæns fra, hvor rækken slutter.
            var endLine = parser.LineNumber < 0 ? lineCount : (int)parser.LineNumber - 1;
            var startLine = endLine - fields.Sum(f => f.Count(c => c == '\n'));

            if (fields.All(f => f.Length == 0))
            {
                continue;
            }

            records.Add(new CsvRow(startLine, fields.Select(Unguard).ToList()));
        }

        return records.Count == 0
            ? new CsvReadResult([], [], null)
            : new CsvReadResult(records[0].Fields, records.GetRange(1, records.Count - 1), null);
    }

    private static CsvReadResult Failed(ImportRowError error) => new([], [], error);

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
