using System.Diagnostics.CodeAnalysis;

namespace GestionDocumentos.Gre;

public static class GrePathUtility
{
    /// <summary>
    /// Resolves the GRE .txt path from a PDF file name (same rules as legacy PdfWatcherWorker).
    /// </summary>
    public static string GetTxtPathForPdf(string pdfFileName, string greTxtDirectory)
    {
        var greCode = pdfFileName.Split('-');
        if (greCode.Length < 4 || greCode[3].Length < 8)
        {
            throw new InvalidOperationException($"Nombre de PDF invalido: {pdfFileName}");
        }

        var txtName = $"{greCode[0]}-{greCode[2]}-{greCode[3].AsSpan(1, 7)}.txt";
        return Path.Combine(greTxtDirectory, txtName);
    }

    public static bool TryGetPdfSearchPatternFromTxtFileName(
        string txtFileName,
        [NotNullWhen(true)] out string? pdfPattern)
    {
        pdfPattern = null;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(txtFileName);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return false;
        }

        var txtCode = fileNameWithoutExtension.Split('-');
        if (txtCode.Length != 3 || txtCode[2].Length != 7)
        {
            return false;
        }

        pdfPattern = $"{txtCode[0]}-*-{txtCode[1]}-?{txtCode[2]}*.pdf";
        return true;
    }

    /// <summary>
    /// Obtiene <c>greName</c> esperado en BD a partir del nombre del PDF.
    /// Usa el mismo token de 7 caracteres que el nombre del TXT compañero en <see cref="GetTxtPathForPdf"/>
    /// (<c>greCode[3].AsSpan(1, 7)</c>), alineado con el campo <c>Correlativo</c> típico del TXT.
    /// </summary>
    public static bool TryGetGreNameFromPdfFileName(string pdfFileName, [NotNullWhen(true)] out string? greName)
    {
        greName = null;
        var fileName = Path.GetFileName(pdfFileName);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var greCode = fileName.Split('-');
        if (greCode.Length < 4 || greCode[3].Length < 8)
        {
            return false;
        }

        var correlativo = greCode[3].AsSpan(1, 7).ToString();
        greName = $"GR-0{greCode[2]}-{correlativo}";
        return true;
    }
}
