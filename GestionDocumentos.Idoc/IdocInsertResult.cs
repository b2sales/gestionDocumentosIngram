namespace GestionDocumentos.Idoc;

/// <summary>
/// Resultado de <see cref="IdocRepository.TryInsertDocumentAsync"/>.
/// <see cref="WasInserted"/> solo es true si cabecera y detalle se confirmaron en la misma transacción.
/// </summary>
public readonly record struct IdocInsertResult(bool WasInserted, int RowsAffected);
