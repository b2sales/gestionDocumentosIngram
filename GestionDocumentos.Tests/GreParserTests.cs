using GestionDocumentos.Gre;

namespace GestionDocumentos.Tests;

public sealed class GreParserTests
{
    [Fact]
    public void GetAttributes_Returns_Value_After_Semicolon()
    {
        var lines = new[]
        {
            "DescripcionAdicsunat|Orden de Compra Cliente ABC",
            "SomeRow;Serie;T004"
        };
        var data = GreParser.ParseLines(lines);
        var serie = data.GetAttributes("Serie");
        Assert.Equal("T004", serie);
    }

    [Fact]
    public void GetValue_Returns_Text_After_Label()
    {
        var lines = new[]
        {
            "DescripcionAdicsunat|Orden de Compra Cliente OC-1 | Nota de Venta SAP NV-2"
        };
        var data = GreParser.ParseLines(lines);
        Assert.Equal("OC-1", data.GetValue("Orden de Compra Cliente"));
        Assert.Equal("NV-2", data.GetValue("Nota de Venta SAP"));
    }

    [Fact]
    public void GetTxtPathForPdf_Builds_Expected_FileName()
    {
        var pdf = "GR-0-T004-12345678.pdf";
        var txtDir = "/data/txt";
        var path = GrePathUtility.GetTxtPathForPdf(pdf, txtDir);
        Assert.Equal(Path.Combine(txtDir, "GR-T004-2345678.txt"), path);
    }

    [Fact]
    public void TryGetGreNameFromPdfFileName_Uses_Same_Token_As_Companion_Txt_Name()
    {
        const string pdf = "GR-0-T004-12345678.pdf";
        Assert.True(GrePathUtility.TryGetGreNameFromPdfFileName(pdf, out var greName));
        Assert.Equal("GR-0T004-2345678", greName);
        var txtPath = GrePathUtility.GetTxtPathForPdf(pdf, "/x");
        Assert.EndsWith("2345678.txt", txtPath, StringComparison.Ordinal);
    }
}
