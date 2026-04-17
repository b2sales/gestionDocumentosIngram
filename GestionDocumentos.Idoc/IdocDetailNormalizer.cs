using System.Globalization;

namespace GestionDocumentos.Idoc;

public static class IdocDetailNormalizer
{
    public static string NormalizePartNumber(string partNumber) =>
        int.Parse(partNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    public static string NormalizeCantidad(string cantidad) =>
        decimal.Parse(cantidad, NumberStyles.Any, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
}
