using GestionDocumentos.Gre;
using GestionDocumentos.Idoc;

namespace GestionDocumentos.Tests;

/// <summary>
/// Pruebas con archivos reales en <c>examples/</c> (copiados al output del test).
/// </summary>
public sealed class RealExampleFilesTests
{
    private static string ExamplePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "examples", fileName);

    [Fact]
    public void Gre_RealPdf_QuickPath_GreName_Matches_Txt_Derived_Guia()
    {
        const string pdf = "20267163228-09-T001-00118915.pdf";
        Assert.True(GrePathUtility.TryGetGreNameFromPdfFileName(pdf, out var fromPdf));
        Assert.Equal("GR-0T001-0118915", fromPdf);

        var txtPath = ExamplePath("20267163228-T001-0118915.txt");
        Assert.True(File.Exists(txtPath), $"Falta ejemplo: {txtPath}");
        var data = GreParser.ParseLines(File.ReadAllLines(txtPath));
        var guia = $"GR-0{data.GetAttributes("Serie")}-{data.GetAttributes("Correlativo")}";
        Assert.Equal(fromPdf, guia);
    }

    [Fact]
    public void Gre_RealTxt_Parses_Routing_And_Adicsunat_Values()
    {
        var txtPath = ExamplePath("20267163228-T001-0118915.txt");
        Assert.True(File.Exists(txtPath));
        var data = GreParser.ParseLines(File.ReadAllLines(txtPath));

        Assert.Equal("T001", data.GetAttributes("Serie"));
        Assert.Equal("0118915", data.GetAttributes("Correlativo"));
        Assert.Equal("03", data.GetAttributes("MotivoTraslado"));
        Assert.Equal("150101", data.GetAttributes("DirLlegUbiGeo"));
        Assert.Equal("20556821438", data.GetAttributes("RUCTranspor"));

        Assert.Equal("3230855205", data.GetValue("Orden de Compra Cliente"));
        Assert.Equal("7110565698", data.GetValue("Nota de Venta SAP"));
        Assert.Equal("8109370325", data.GetValue("Documento Despacho"));
        Assert.Equal("9929388116", data.GetValue("Factura Sistema"));
        Assert.Equal("F007-0655433", data.GetValue("Documento Ref"));
    }

    [Fact]
    public void Gre_RealSample_GetTxtPathForPdf_Builds_Companion_Name()
    {
        const string pdf = "20267163228-09-T001-00118915.pdf";
        var dir = Path.GetTempPath();
        var expected = Path.Combine(dir, "20267163228-T001-0118915.txt");
        var actual = GrePathUtility.GetTxtPathForPdf(pdf, dir);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Idoc_RealXml_Parses_Header_And_StockLine()
    {
        var xmlPath = ExamplePath("01_NPG_F007_0655433.xml");
        Assert.True(File.Exists(xmlPath), $"Falta ejemplo: {xmlPath}");
        var xml = await File.ReadAllTextAsync(xmlPath);

        var doc = IdocXmlParser.Parse(xml);

        Assert.Equal("01", doc.TipoDoc);
        Assert.Equal("F007", doc.Serie);
        Assert.Equal("0655433", doc.Numero);
        Assert.Equal("2026-04-07", doc.Fecha);
        Assert.Equal("387108", doc.CodSapCliente);
        Assert.Equal("20604798591", doc.RucCliente);
        Assert.Equal("OPORTUTEK PERU S.A.C.", doc.NomCliente);
        Assert.Equal("PEN", doc.Moneda);
        Assert.Equal("330.08", doc.Monto);
        Assert.Equal("9929388116", doc.DocumentoSap);
        Assert.Equal("2026-05-07", doc.FechaVencimiento);
        Assert.Equal("30 días", doc.PaymentMethod);
        Assert.Equal("7110565698", doc.Pedido);
        Assert.Equal("11:21:58.0000", doc.PaymentIssueTime);
        Assert.Equal("203108", doc.CodVen);
        Assert.Equal("3230855205", doc.CustomerOrderNumber);

        Assert.Single(doc.Detalle);
        Assert.Equal("000000000006684111", doc.Detalle[0].PartNumber);
        Assert.Equal("MOTO BUDS LOOP VERDE", doc.Detalle[0].Descripcion);
        Assert.Equal("1.000", doc.Detalle[0].Cantidad);
    }

    [Fact]
    public void Idoc_DetailNormalizer_Normalizes_Legacy_Compatible_Values()
    {
        Assert.Equal("6684111", IdocDetailNormalizer.NormalizePartNumber("000000000006684111"));
        Assert.Equal("1.000", IdocDetailNormalizer.NormalizeCantidad("1.000"));
    }

    [Theory]
    [InlineData("ABC123", "1.000")]
    [InlineData("000000000006684111", "cantidad-invalida")]
    public void Idoc_DetailNormalizer_Rejects_Invalid_Legacy_Values(string partNumber, string cantidad)
    {
        if (partNumber == "ABC123")
        {
            Assert.Throws<FormatException>(() => IdocDetailNormalizer.NormalizePartNumber(partNumber));
        }

        if (cantidad == "cantidad-invalida")
        {
            Assert.Throws<FormatException>(() => IdocDetailNormalizer.NormalizeCantidad(cantidad));
        }
    }

    [Fact]
    public void Idoc_BackOfficePaths_ToArchivoTibcoRelative_Preserves_Leading_Separator()
    {
        var paths = new IdocBackOfficePaths();
        paths.Apply(@"C:\tibco\in", @"C:\tibco\in", resolvedFromDatabase: true);

        var relative = paths.ToArchivoTibcoRelative(@"C:\tibco\in\01_NPG_F007_0655433.xml");

        Assert.Equal(@"\01_NPG_F007_0655433.xml", relative);
    }

    [Fact]
    public void Idoc_XmlParser_Does_Not_Use_SequentialNumber_Fallback_From_New_Format()
    {
        const string xml =
            """
            <ns0:root xmlns:ns0="urn:test">
              <ns0:documentType>01</ns0:documentType>
              <ns0:docNumber>F007</ns0:docNumber>
              <ns0:sequentialNumber>0655433</ns0:sequentialNumber>
              <ns0:MontoDetraccion>0.00</ns0:MontoDetraccion>
              <ns0:PorcentajeDet>0.00</ns0:PorcentajeDet>
            </ns0:root>
            """;

        var doc = IdocXmlParser.Parse(xml);

        Assert.Equal("", doc.Numero);
    }

    [Fact]
    public void Idoc_XmlParser_Invalid_MontoDetraccion_Returns_Zero()
    {
        // Fase 2 (parseo defensivo): valores numéricos inválidos ya no lanzan FormatException
        // (comportamiento legacy); se normalizan a 0m para que el archivo no corrompa el pipeline.
        const string xml =
            """
            <ns0:root xmlns:ns0="urn:test">
              <ns0:documentType>01</ns0:documentType>
              <ns0:docNumber>F007</ns0:docNumber>
              <ns0:sequentilaNumber>0655433</ns0:sequentilaNumber>
              <ns0:MontoDetraccion>monto-invalido</ns0:MontoDetraccion>
              <ns0:PorcentajeDet>0.00</ns0:PorcentajeDet>
            </ns0:root>
            """;

        var doc = IdocXmlParser.Parse(xml);
        Assert.Equal(0m, doc.MontoDetraccion);
        Assert.Equal(0m, doc.PorcentajeDet);
    }
}
