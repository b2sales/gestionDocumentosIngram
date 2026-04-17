using System.Text.RegularExpressions;
using GestionDocumentos.Gre;

namespace GestionDocumentos.Tests;

/// <summary>
/// Ejemplos reales de <c>greName</c> en BD (serie T001, correlativo 7 dígitos).
/// </summary>
public sealed class GreGuiaNumberFormatTests
{
    private static readonly Regex GreNameT001Pattern = new(
        @"^GR-0T001-\d{7}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static TheoryData<string> ProductionGuiaExamples =>
    [
        "GR-0T001-0116405",
        "GR-0T001-0116268",
        "GR-0T001-0116267",
        "GR-0T001-0116266",
        "GR-0T001-0116265",
        "GR-0T001-0116388",
        "GR-0T001-0116390",
        "GR-0T001-0116391",
        "GR-0T001-0116393",
        "GR-0T001-0116394",
        "GR-0T001-0116395",
        "GR-0T001-0116396",
        "GR-0T001-0116398",
        "GR-0T001-0116450",
        "GR-0T001-0116451",
        "GR-0T001-0116463",
        "GR-0T001-0116464",
        "GR-0T001-0116465",
        "GR-0T001-0116466"
    ];

    [Theory]
    [MemberData(nameof(ProductionGuiaExamples))]
    public void Production_GreName_Examples_Match_T001_SevenDigit_Format(string greName)
    {
        Assert.True(GreNameT001Pattern.IsMatch(greName), greName);
    }

    /// <summary>
    /// Coherencia con <see cref="GrePathUtility.TryGetGreNameFromPdfFileName"/>: correlativo de 7 dígitos
    /// como en el nombre del TXT compañero.
    /// </summary>
    [Theory]
    [InlineData("20267163228-09-T001-00116405.pdf", "GR-0T001-0116405")]
    [InlineData("20267163228-09-T001-00116466.pdf", "GR-0T001-0116466")]
    public void Pdf_FileName_Derives_GreName_For_IngramStyle_Samples(string pdfFileName, string expectedGreName)
    {
        Assert.True(GrePathUtility.TryGetGreNameFromPdfFileName(pdfFileName, out var greName));
        Assert.Equal(expectedGreName, greName);
    }
}
