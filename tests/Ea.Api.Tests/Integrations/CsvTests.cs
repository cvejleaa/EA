using System.Text;
using Ea.Api.Common;
using Microsoft.VisualBasic.FileIO;

namespace Ea.Api.Tests.Integrations;

/// <summary>
/// CSV-skrivningen. Tilbagelæsning sker med en UAFHÆNGIG parser (TextFieldParser), så testen ikke bare
/// bekræfter skriverens egen forståelse af formatet.
/// </summary>
public sealed class CsvTests
{
    private static readonly string[] Header = ["Id", "Tekst"];

    private static List<string[]> ReadBack(byte[] bytes)
    {
        using var parser = new TextFieldParser(new MemoryStream(bytes), Encoding.UTF8, detectEncoding: true);
        parser.SetDelimiters(";");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields()!);
        }

        return rows;
    }

    [Fact]
    public void Filen_starter_med_utf8_bom_og_bruger_semikolon_og_crlf()
    {
        var bytes = Csv.Write(Header, [["1", "Æble"]]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Id;Tekst\r\n1;Æble\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("Løn; månedlig")]
    [InlineData("Regnearket \"Løn 2026\"")]
    [InlineData("Første linje\r\nAnden linje")]
    [InlineData(" foranstillet mellemrum")]
    [InlineData("Almindelig tekst")]
    public void Felter_kommer_uaendrede_tilbage_gennem_en_uafhaengig_parser(string value)
    {
        var rows = ReadBack(Csv.Write(Header, [["1", value]]));

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Id", "Tekst"], rows[0]);
        Assert.Equal(["1", value], rows[1]);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]
    [InlineData("+1")]
    [InlineData("-2")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tTAB")]
    public void Formler_neutraliseres_med_apostrof(string value)
    {
        var field = ReadBack(Csv.Write(Header, [["1", value]]))[1][1];

        Assert.Equal("'" + value, field);
    }

    [Fact]
    public void Tomme_og_manglende_felter_skrives_tomme() =>
        Assert.Equal("Id;Tekst\r\n;\r\n", Encoding.UTF8.GetString(Csv.Write(Header, [[null, ""]])[3..]));

    [Fact]
    public void En_raekke_med_forkert_antal_felter_afvises() =>
        Assert.Throws<ArgumentException>(() => Csv.Write(Header, [["kun ét felt"]]));

    [Theory]
    [InlineData("Identitetskilde (fiktiv)", "identitetskilde-fiktiv")]
    [InlineData("Lønudtræk-regneark", "loenudtraek-regneark")]
    [InlineData("Åben Ærø", "aaben-aeroe")]
    public void Filnavne_bliver_ascii(string name, string expected) => Assert.Equal(expected, Csv.Slug(name));
}
