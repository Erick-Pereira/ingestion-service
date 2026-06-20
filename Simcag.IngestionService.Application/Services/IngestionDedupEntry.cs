namespace Simcag.IngestionService.Application.Services;

/// <summary>Motivo do bloqueio em upload duplicado.</summary>
public static class IngestionDuplicateReasons
{
    public const string FileHash = "file_hash";
}

/// <summary>Metadados do primeiro upload bem-sucedido, reutilizados em uploads idênticos (mesmo tenant + hash ou NF-e).</summary>
public sealed record IngestionDedupEntry(
    string DocumentId,
    string? TenantId,
    string DocumentType,
    int ExtractedItemCount,
    bool PublishedDataIngestedEvent,
    string? NfeAccessKey = null,
    string? NfeFallbackKey = null,
    string? ExpenseId = null,
    string? FileHash = null,
    string? DuplicateReason = null);
