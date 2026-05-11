namespace Simcag.IngestionService.Application.Services;

/// <summary>Metadados do primeiro upload bem-sucedido, reutilizados em uploads idênticos (mesmo tenant + hash).</summary>
public sealed record IngestionDedupEntry(
    string DocumentId,
    string? TenantId,
    string DocumentType,
    int ExtractedItemCount,
    bool PublishedDataIngestedEvent);
