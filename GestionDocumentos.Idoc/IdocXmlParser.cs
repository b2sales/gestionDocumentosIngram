using System.Xml;

namespace GestionDocumentos.Idoc;

public static class IdocXmlParser
{
    public static IdocDocument Parse(string xmlContent)
    {
        var tibcoXmlDoc = new XmlDocument();
        tibcoXmlDoc.LoadXml(xmlContent);

        static string GetHeaderValue(XmlDocument doc, string exactTagName) =>
            doc.OuterXml.Contains($"<{exactTagName}>", StringComparison.Ordinal)
                ? doc.GetElementsByTagName(exactTagName)[0]?.InnerText ?? ""
                : "";

        var tipoDoc = GetHeaderValue(tibcoXmlDoc, "ns0:documentType");
        var documento = new IdocDocument
        {
            TipoDoc = tipoDoc,
            Serie = GetHeaderValue(tibcoXmlDoc, "ns0:docNumber"),
            Numero = GetHeaderValue(tibcoXmlDoc, "ns0:sequentilaNumber"),
            Fecha = GetHeaderValue(tibcoXmlDoc, "ns0:invoiceDate"),
            CodSapCliente = GetHeaderValue(tibcoXmlDoc, "ns0:billToNumber"),
            RucCliente = GetHeaderValue(tibcoXmlDoc, "ns0:customerTVAT"),
            NomCliente = GetHeaderValue(tibcoXmlDoc, "ns0:billToName"),
            Moneda = GetHeaderValue(tibcoXmlDoc, "ns0:currencyCode"),
            Monto = GetHeaderValue(tibcoXmlDoc, "ns0:totalTaxValue"),
            DocumentoSap = GetHeaderValue(tibcoXmlDoc, "ns0:SAPBillingDocumentNumber"),
            FechaVencimiento = GetHeaderValue(tibcoXmlDoc, "ns0:paymentDueDate"),
            PaymentMethod = GetHeaderValue(tibcoXmlDoc, "ns0:paymentMethod"),
            Contacto = GetHeaderValue(tibcoXmlDoc, "ns0:soldToEmailId"),
            Pedido = GetHeaderValue(tibcoXmlDoc, "ns0:orderNumber"),
            PaymentIssueTime = GetHeaderValue(tibcoXmlDoc, "ns0:paymentIssueTime"),
            CodVen = GetHeaderValue(tibcoXmlDoc, "ns0:salesMan"),
            OrderReason = GetHeaderValue(tibcoXmlDoc, "ns0:salesOrderReason"),
            CustomerOrderNumber = GetHeaderValue(tibcoXmlDoc, "ns0:customerOrderNumber")
        };

        documento.MontoDetraccion = decimal.Parse(GetHeaderValue(tibcoXmlDoc, "ns0:MontoDetraccion") is { Length: > 0 } md ? md : "0.00");
        documento.PorcentajeDet = decimal.Parse(GetHeaderValue(tibcoXmlDoc, "ns0:PorcentajeDet") is { Length: > 0 } pd ? pd : "0.00");

        if (tipoDoc is "07" or "08")
        {
            documento.ReferenceDocNumber = GetHeaderValue(tibcoXmlDoc, "ns0:referenceDocNumber");
        }

        var lineas = tibcoXmlDoc.GetElementsByTagName("ns0:stocKLine");
        foreach (XmlNode line in lineas)
        {
            var det = new IdocLineDetail
            {
                PartNumber = line["ns0:partNumber"]!.InnerText,
                Descripcion = line["ns0:itemDescription"]!.InnerText,
                Cantidad = line["ns0:quantity"]!.InnerText
            };
            documento.Detalle.Add(det);
        }

        return documento;
    }
}
