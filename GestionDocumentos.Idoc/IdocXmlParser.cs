using System.Globalization;
using System.Xml;

namespace GestionDocumentos.Idoc;

public static class IdocXmlParser
{
    public static IdocDocument Parse(string xmlContent)
    {
        var tibcoXmlDoc = new XmlDocument();
        tibcoXmlDoc.LoadXml(xmlContent);

        var tipoDoc = GetTagValue(tibcoXmlDoc, "ns0:documentType");
        var documento = new IdocDocument
        {
            TipoDoc = tipoDoc,
            Serie = GetTagValue(tibcoXmlDoc, "ns0:docNumber"),
            Numero = GetTagValue(tibcoXmlDoc, "ns0:sequentilaNumber"),
            Fecha = GetTagValue(tibcoXmlDoc, "ns0:invoiceDate"),
            CodSapCliente = GetTagValue(tibcoXmlDoc, "ns0:billToNumber"),
            RucCliente = GetTagValue(tibcoXmlDoc, "ns0:customerTVAT"),
            NomCliente = GetTagValue(tibcoXmlDoc, "ns0:billToName"),
            Moneda = GetTagValue(tibcoXmlDoc, "ns0:currencyCode"),
            Monto = GetTagValue(tibcoXmlDoc, "ns0:totalTaxValue"),
            DocumentoSap = GetTagValue(tibcoXmlDoc, "ns0:SAPBillingDocumentNumber"),
            FechaVencimiento = GetTagValue(tibcoXmlDoc, "ns0:paymentDueDate"),
            PaymentMethod = GetTagValue(tibcoXmlDoc, "ns0:paymentMethod"),
            Contacto = GetTagValue(tibcoXmlDoc, "ns0:soldToEmailId"),
            Pedido = GetTagValue(tibcoXmlDoc, "ns0:orderNumber"),
            PaymentIssueTime = GetTagValue(tibcoXmlDoc, "ns0:paymentIssueTime"),
            CodVen = GetTagValue(tibcoXmlDoc, "ns0:salesMan"),
            OrderReason = GetTagValue(tibcoXmlDoc, "ns0:salesOrderReason"),
            CustomerOrderNumber = GetTagValue(tibcoXmlDoc, "ns0:customerOrderNumber")
        };

        documento.MontoDetraccion = TryParseDecimal(GetTagValue(tibcoXmlDoc, "ns0:MontoDetraccion"));
        documento.PorcentajeDet = TryParseDecimal(GetTagValue(tibcoXmlDoc, "ns0:PorcentajeDet"));

        if (tipoDoc is "07" or "08")
        {
            documento.ReferenceDocNumber = GetTagValue(tibcoXmlDoc, "ns0:referenceDocNumber");
        }

        var lineas = tibcoXmlDoc.GetElementsByTagName("ns0:stocKLine");
        foreach (XmlNode line in lineas)
        {
            if (line is not XmlElement elem)
            {
                continue;
            }

            documento.Detalle.Add(new IdocLineDetail
            {
                PartNumber = GetChildText(elem, "ns0:partNumber"),
                Descripcion = GetChildText(elem, "ns0:itemDescription"),
                Cantidad = GetChildText(elem, "ns0:quantity")
            });
        }

        return documento;
    }

    private static string GetTagValue(XmlDocument doc, string exactTagName)
    {
        var nodes = doc.GetElementsByTagName(exactTagName);
        if (nodes.Count == 0)
        {
            return "";
        }

        return nodes[0]?.InnerText ?? "";
    }

    private static string GetChildText(XmlElement parent, string childName) =>
        parent[childName]?.InnerText ?? "";

    /// <summary>
    /// Parseo tolerante: los XML de origen pueden venir con coma o punto como separador decimal
    /// según el locale del productor. Normalizamos a punto e interpretamos con <see cref="CultureInfo.InvariantCulture"/>
    /// para evitar dependencias del locale del host.
    /// </summary>
    /// <summary>
    /// Parseo tolerante: normalizamos el separador decimal a punto cuando la entrada usa coma
    /// (productor con locale <c>es-*</c>) y parseamos con <see cref="CultureInfo.InvariantCulture"/>
    /// para no depender del locale del host.
    /// </summary>
    private static decimal TryParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var trimmed = value.Trim();

        // Si hay una única coma y ningún punto, es separador decimal: normalizar.
        if (trimmed.IndexOf('.') < 0 &&
            trimmed.IndexOf(',') >= 0 &&
            trimmed.IndexOf(',') == trimmed.LastIndexOf(','))
        {
            trimmed = trimmed.Replace(',', '.');
        }

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : 0m;
    }
}
