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
    public void En_tom_fil_giver_ingen_overskrifter_og_ingen_raekker()
    {
        var result = Csv.Read([]);

        Assert.Null(result.Error);
        Assert.Empty(result.Header);
        Assert.Empty(result.Rows);
    }
}
