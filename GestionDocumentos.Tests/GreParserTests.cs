using GestionDocumentos.Gre;

namespace GestionDocumentos.Tests;

public sealed class GreParserTests
{
    [Fact]
    public void GetAttributes_uses_exact_key_match_not_contains()
    {
        // Un archivo con dos claves que comparten prefijo: "RUCTranspor" y "RUCTransporExtra".
        // La implementación anterior (Contains+First) devolvía el valor de la primera ocurrencia,
        // que podía ser la línea "Extra". El diccionario nuevo resuelve por clave exacta.
        var lines = new[]
        {
            "A;RUCTransporExtra;1;EXTRA-VALUE",
            "A;RUCTranspor;1;TRANSPORTISTA-001"
        };

        var data = GreParser.ParseLines(lines);

        Assert.Equal("TRANSPORTISTA-001", data.GetAttributes("RUCTranspor"));
        Assert.Equal("EXTRA-VALUE", data.GetAttributes("RUCTransporExtra"));
        Assert.Equal("", data.Errors);
    }

    [Fact]
    public void GetAttributes_returns_empty_and_records_error_on_missing_key()
    {
        var data = GreParser.ParseLines(["A;Serie;1;T001"]);
        Assert.Equal("", data.GetAttributes("Inexistente"));
        Assert.Contains("Inexistente:Attribute not found", data.Errors);
    }

    [Fact]
    public void GetValue_extracts_value_from_pipe_separated_desc()
    {
        var lines = new[]
        {
            "E;DescripcionAdicsunat;1;T001-001  |  Orden de Compra Cliente 123  |  Nota de Venta SAP 456  |  Factura Sistema 789"
        };

        var data = GreParser.ParseLines(lines);
        Assert.Equal("123", data.GetValue("Orden de Compra Cliente"));
        Assert.Equal("456", data.GetValue("Nota de Venta SAP"));
        Assert.Equal("789", data.GetValue("Factura Sistema"));
    }

    [Fact]
    public void GetValue_requires_prefix_followed_by_space_to_avoid_collisions()
    {
        // "Factura" sola NO debe matchear "Factura Sistema 789".
        var lines = new[]
        {
            "E;DescripcionAdicsunat;1;Factura Sistema 789"
        };

        var data = GreParser.ParseLines(lines);
        Assert.Equal("", data.GetValue("Factura"));
    }

    [Fact]
    public void Values_with_embedded_semicolons_are_preserved()
    {
        // El valor puede contener ';' adicionales; Split(';', 4) preserva el resto.
        var lines = new[]
        {
            "A;RznSocEmis;1;INGRAM MICRO; S.A.C."
        };

        var data = GreParser.ParseLines(lines);
        Assert.Equal("INGRAM MICRO; S.A.C.", data.GetAttributes("RznSocEmis"));
    }
}
