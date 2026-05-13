using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Application.Services;

/// <summary>Chave estável Redis/memória para dedupe de upload (tenant + SHA-256).</summary>
public static class IngestionUploadDedupKeys
{
    public static string Build(string tenantId, FileHash fileHash)
    {
        var t = string.IsNullOrWhiteSpace(tenantId)
            ? "__no_tenant__"
            : tenantId.Trim().ToLowerInvariant();
        return $"ingestion:upload:{t}:{fileHash.Value}";
    }
}
