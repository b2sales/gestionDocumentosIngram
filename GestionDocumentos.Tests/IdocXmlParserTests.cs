using GestionDocumentos.Idoc;

namespace GestionDocumentos.Tests;

public sealed class IdocXmlParserTests
{
    [Fact]
    public void Parse_Extracts_Header_And_Lines()
    {
        var xml = """
                  <ns0:root xmlns:ns0="http://example.com/ns">
                    <ns0:documentType>01</ns0:documentType>
                    <ns0:docNumber>F001</ns0:docNumber>
                    <ns0:sequentilaNumber>123</ns0:sequentilaNumber>
                    <ns0:invoiceDate>2024-01-15</ns0:invoiceDate>
                    <ns0:billToNumber>000123</ns0:billToNumber>
                    <ns0:customerTVAT>20123456789</ns0:customerTVAT>
                    <ns0:billToName>ACME</ns0:billToName>
                    <ns0:currencyCode>PEN</ns0:currencyCode>
                    <ns0:totalTaxValue>100.00</ns0:totalTaxValue>
                    <ns0:SAPBillingDocumentNumber>90000001</ns0:SAPBillingDocumentNumber>
                    <ns0:paymentDueDate>2024-02-15</ns0:paymentDueDate>
                    <ns0:paymentMethod>TRANSFER</ns0:paymentMethod>
                    <ns0:soldToEmailId>a@b.com</ns0:soldToEmailId>
                    <ns0:orderNumber>PO1</ns0:orderNumber>
                    <ns0:paymentIssueTime>10:00</ns0:paymentIssueTime>
                    <ns0:salesMan>V1</ns0:salesMan>
                    <ns0:salesOrderReason>R1</ns0:salesOrderReason>
                    <ns0:customerOrderNumber>CUST-1</ns0:customerOrderNumber>
                    <ns0:MontoDetraccion>1,5</ns0:MontoDetraccion>
                    <ns0:PorcentajeDet>2,0</ns0:PorcentajeDet>
                    <ns0:stocKLine>
                      <ns0:partNumber>00042</ns0:partNumber>
                      <ns0:itemDescription>Widget</ns0:itemDescription>
                      <ns0:quantity>3</ns0:quantity>
                    </ns0:stocKLine>
                  </ns0:root>
                  """;

        var doc = IdocXmlParser.Parse(xml);

        Assert.Equal("01", doc.TipoDoc);
        Assert.Equal("F001", doc.Serie);
        Assert.Equal("123", doc.Numero);
        Assert.Equal("2024-01-15", doc.Fecha);
        Assert.Equal("000123", doc.CodSapCliente);
        Assert.Equal("20123456789", doc.RucCliente);
        Assert.Equal("ACME", doc.NomCliente);
        Assert.Equal("PEN", doc.Moneda);
        Assert.Equal("100.00", doc.Monto);
        Assert.Equal("90000001", doc.DocumentoSap);
        Assert.Equal(1.5m, doc.MontoDetraccion);
        Assert.Equal(2.0m, doc.PorcentajeDet);
        Assert.Equal("CUST-1", doc.CustomerOrderNumber);

        Assert.Single(doc.Detalle);
        Assert.Equal("00042", doc.Detalle[0].PartNumber);
        Assert.Equal("Widget", doc.Detalle[0].Descripcion);
        Assert.Equal("3", doc.Detalle[0].Cantidad);
    }

    [Fact]
    public void Parse_ReferenceDoc_For_Tipo_07()
    {
        var xml = """
                  <ns0:root xmlns:ns0="http://x">
                    <ns0:documentType>07</ns0:documentType>
                    <ns0:docNumber>F001</ns0:docNumber>
                    <ns0:sequentilaNumber>1</ns0:sequentilaNumber>
                    <ns0:referenceDocNumber>REF-9</ns0:referenceDocNumber>
                  </ns0:root>
                  """;
        var doc = IdocXmlParser.Parse(xml);
        Assert.Equal("REF-9", doc.ReferenceDocNumber);
    }
}
