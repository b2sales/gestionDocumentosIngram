namespace GestionDocumentos.Idoc;

public sealed class IdocDocument
{
    public string ArchivoTibco { get; set; } = "";
    public string TipoDoc { get; set; } = "";
    public string Serie { get; set; } = "";
    public string Numero { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string CodSapCliente { get; set; } = "";
    public string RucCliente { get; set; } = "";
    public string NomCliente { get; set; } = "";
    public string Moneda { get; set; } = "";
    public string Monto { get; set; } = "";
    public string DocumentoSap { get; set; } = "";
    public string FechaVencimiento { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public string Contacto { get; set; } = "";
    public string Pedido { get; set; } = "";
    public string PaymentIssueTime { get; set; } = "";
    public string CodVen { get; set; } = "";
    public string OrderReason { get; set; } = "";
    public string CustomerOrderNumber { get; set; } = "";
    public decimal MontoDetraccion { get; set; }
    public decimal PorcentajeDet { get; set; }
    public string ReferenceDocNumber { get; set; } = "";

    public List<IdocLineDetail> Detalle { get; } = new();
}

public sealed class IdocLineDetail
{
    public string PartNumber { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Cantidad { get; set; } = "";
}
