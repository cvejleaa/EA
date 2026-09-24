using System.Text;
using Ea.Api.Common;

namespace Ea.Api.Tests.Capabilities;

/// <summary>
/// CSV-læseren: det omvendte af skriveren. Read(Write(x)) skal give x for ALT, også felter, skriveren har sat
/// apostroffer i — ellers ændrer en uændret eksport → import data.
/// </summary>
public sealed class CsvReadTests
{
    private static readonly string[] Header = ["Id", "Tekst"];

    public static TheoryData<string> Tricky() =>
    [
        "=HYPERLINK(\"x\")", "+1", "-2", "@SUM(A1)", "\tTAB", "\rCR", "＝1", "－1",
        "'", "''", "'=x", "'almindelig", "tekst,'=1", "tekst,=1+1", "tekst\t=1", "a,'x",
        "100,- kr.", "pris 100,-", "- punkt", "Løn; månedlig", "Første linje\r\nAnden linje", "Regnearket \"Løn 2026\"",
        " foranstillet", "efterstillet ", "Almindelig tekst", "",
        // Tomme linjer inde i et felt (TextFieldParser slugte dem — derfor egen parser).
        "a\n\nb", "\r\n\r\n", " \n ", "x\r\n\r\ny", "\n", "\r", "slut\r\n",
        // Formler efter linjeskift og citationstegn (Security Reviewer, delopgave 3a).
        "tekst\n=1+1,", "x,\"=1+1,", "x,\"\"=1", "a\r\n@SUM(1)", " \n-1+1", "a,\" '=x", "a\n'b",
    ];

    [Theory]
    [MemberData(nameof(Tricky))]
    public void Det_skriveren_skrev_laeses_uaendret_tilbage(string value)
    {
        var result = Csv.Read(Csv.Write(Header, [["1", value]]));

        Assert.Null(result.Error);
        Assert.Equal(Header, result.Header);
        Assert.Equal(["1", value], Assert.Single(result.Rows).Fields);
    }

    private static readonly char[] Dangerous = ['=', '+', '-', '@', '＝', '＋', '－', '＠'];

    /// <summary>
    /// Læser tekst, som et regneark eller Pythons csv-modul gør: citationstegn kun i starten af en celle, og tegn
    /// efter et afsluttende citationstegn hænges på cellen ("x,""=1" med komma som skilletegn giver cellen =1).
    /// </summary>
    private static List<string> CellsAsSpreadsheet(string text, char delimiter)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var atStart = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && atStart)
            {
                quoted = true;
                atStart = false;
            }
            else if (c == delimiter || c == '\r' || c == '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                atStart = true;
            }
            else
            {
                cell.Append(c);
                atStart = false;
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }

    private static bool IsFormula(string cell) =>
        cell.Length > 0 && Array.IndexOf(Dangerous, cell[0]) >= 0 &&
        !(cell[0] is '-' or '－' && (cell.Length == 1 || char.IsWhiteSpace(cell[1])));

    /// <summary>Tilfældige felter af de tegn, der betyder noget: formeltegn, skilletegn, linjeskift, citationstegn.</summary>
    private static List<string> Fuzz(int count)
    {
        const string alphabet = "=+-@＝－,\t\r\n\"' a1;";
        var random = new Random(3);
        return Enumerable.Range(0, count)
            .Select(_ => new string(Enumerable.Range(0, random.Next(0, 9)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray()))
            .ToList();
    }

    [Fact]
    public void Ingen_celle_bliver_en_formel_uanset_skilletegn_ogsaa_efter_linjeskift_og_citationstegn()
    {
        var values = Fuzz(20000);
        values.AddRange(["tekst\n=1+1,", "x,\"=1+1,", "x,\"\"=1", "a\r\n@SUM(1)", " \n-1+1", "x\t\" \"+1"]);
        var text = Encoding.UTF8.GetString(Csv.Write(Header, values.Select(v => (IReadOnlyList<string?>)[v, "x" + v])));

        foreach (var delimiter in new[] { ';', ',', '\t' })
        {
            var formulas = CellsAsSpreadsheet(text, delimiter).Where(IsFormula).ToList();
            Assert.True(formulas.Count == 0,
                $"Med '{delimiter}' som skilletegn blev {formulas.Count} celler formler, fx {string.Join(" | ", formulas.Take(3))}");
        }
    }

    [Fact]
    public void Alt_det_skriveren_skrev_laeses_uaendret_tilbage_ogsaa_tilfaeldige_felter()
    {
        var values = Fuzz(20000);

        var result = Csv.Read(Csv.Write(Header, values.Select((v, i) => (IReadOnlyList<string?>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture), v])));

        Assert.Null(result.Error);
        // Rækker med kun tomme felter springes over — det kan ikke ske her, fordi Id-feltet altid er udfyldt.
        Assert.Equal(values, result.Rows.Select(r => r.Fields[1]));
    }

    [Fact]
    public void Skriveren_saetter_apostrof_foran_en_apostrof_saa_den_kan_skelnes()
    {
        // Uden den ekstra apostrof ville "'=x" (brugerens egen tekst) blive læst som "=x".
        var text = Encoding.UTF8.GetString(Csv.Write(Header, [["1", "'=x"]])[3..]);

        Assert.Equal("Id;Tekst\r\n1;''=x\r\n", text);
    }

    [Fact]
    public void Linjenumre_passer_ogsaa_efter_tomme_linjer_og_felter_over_flere_linjer()
    {
        var text = "Id;Tekst\r\n1;a\r\n\r\n2;\"to\r\nlinjer\"\r\n;\r\n3;c\r\n";

        var result = Csv.Read(Encoding.UTF8.GetBytes(text));

        Assert.Null(result.Error);
        Assert.Equal([(2, "1"), (4, "2"), (7, "3")], result.Rows.Select(r => (r.Line, r.Fields[0])));
        Assert.Equal("to\r\nlinjer", result.Rows[1].Fields[1]);
    }

    [Fact]
    public void Bom_er_valgfri()
    {
        var withBom = Csv.Read([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("Id;Tekst\r\n1;Æble\r\n")]);
        var without = Csv.Read(Encoding.UTF8.GetBytes("Id;Tekst\r\n1;Æble\r\n"));

        Assert.Equal(["Id", "Tekst"], withBom.Header);
        Assert.Equal(["Id", "Tekst"], without.Header);
        Assert.Equal("Æble", Assert.Single(withBom.Rows).Fields[1]);
        Assert.Equal("Æble", Assert.Single(without.Rows).Fields[1]);
    }

    [Fact]
    public void En_fil_i_en_aeldre_tegnkodning_afvises_med_linjenummer_og_vejledning()
    {
        // "CSV (semikolonsepareret)" fra dansk Excel: Windows-1252, hvor æ er én byte (0xE6).
        var bytes = Encoding.Latin1.GetBytes("Id;Tekst\r\n1;a\r\n2;Æble\r\n");

        var result = Csv.Read(bytes);

        Assert.NotNull(result.Error);
        Assert.Equal(3, result.Error.Line);
        Assert.Contains("CSV UTF-8 (kommasepareret)", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Et_citationstegn_der_ikke_lukkes_giver_en_fejl_i_stedet_for_at_sluge_resten()
    {
        var result = Csv.Read(Encoding.UTF8.GetBytes("Id;Tekst\r\n1;\"åben\r\n2;b\r\n"));

        Assert.NotNull(result.Error);
        Assert.Contains("citationstegn", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tekst_efter_et_afsluttende_citationstegn_er_en_fejl_med_linjenummer()
    {
        var result = Csv.Read(Encoding.UTF8.GetBytes("Id;Tekst\r\n1;a\r\n2;\"b\"c\r\n"));

        Assert.Equal(3, result.Error?.Line);
        Assert.Contains("efter et afsluttende citationstegn", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Et_nul_tegn_afvises_med_linjenummer()
    {
        var result = Csv.Read(Encoding.UTF8.GetBytes("Id;Tekst\r\n1;a\r\n2;b\0c\r\n"));

        Assert.Equal(3, result.Error?.Line);
        Assert.Contains("NUL", result.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Alle_slags_linjeskift_giver_samme_raekker_og_linjenumre(string newline)
    {
        var result = Csv.Read(Encoding.UTF8.GetBytes(string.Join(newline, "Id;Tekst", "1;a", "", "2;b") + newline));

        Assert.Equal([(2, "a"), (4, "b")], result.Rows.Select(r => (r.Line, r.Fields[1])));
    }

    [Fact]
    public void En_tom_fil_giver_ingen_overskrifter_og_ingen_raekker()
    {
        var result = Csv.Read([]);

        Assert.Null(result.Error);
        Assert.Empty(result.Header);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Laesningen_stopper_ved_maxRecords_saa_en_kaempefil_ikke_parses_helt()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 10).Select(i => $"a{i};b\n")));

        var read = Csv.Read(bytes, maxRecords: 3);

        Assert.Null(read.Error);
        Assert.Equal(["a0", "b"], read.Header);
        Assert.Equal([2, 3], read.Rows.Select(r => r.Line));
    }

    [Fact]
    public void En_linje_med_over_1000_felter_afvises()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("a;b\n" + new string(';', Csv.MaxFieldsPerRecord) + "\n");

        var read = Csv.Read(bytes);

        Assert.Equal(new ImportRowError(2, null, "Linjen har over 1000 felter. Er det den rigtige fil?"), read.Error);
    }
}
