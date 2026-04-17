using System.ComponentModel.DataAnnotations;

namespace GestionDocumentos.Gre;

public sealed class GreInfo
{
    [Key]
    public int id { get; set; }

    public string greName { get; set; } = "";
    public string ordenCompra { get; set; } = "";
    public string notaVenta { get; set; } = "";
    public string delivery { get; set; } = "";
    public string? facturaSAP { get; set; }
    public string? facturaSUNAT { get; set; }
    public string rucTranspor { get; set; } = "";
    public string razoTrans { get; set; } = "";
    public MotivoTraslado motivoTraslado { get; set; }
    public DateTime fechaInicioTraslado { get; set; }
    public DateTime Auditoria_CreatedAt { get; set; }
    public DateTime Auditoria_UpdatedAt { get; set; }
    public DateTime? Auditoria_DeletedAt { get; set; }
    public bool Auditoria_Deleted { get; set; }

    public string? statusBee { get; set; }
    public string? substatusBee { get; set; }
    public bool? delivered_in_clientBee { get; set; }

    public DateTime? statusDate { get; set; }
    public DateTime? estimatedDeliveryDate { get; set; }
    public DateTime? actualDeliveryDate { get; set; }
    public string? shipmentStatus { get; set; }
    public Departamentos city { get; set; }
    public string? stateCode { get; set; }
    public string destinationPostCode { get; set; } = "";

    public string? StatusUrbano { get; set; }
    public string? SubstatusUrbano { get; set; }
}
