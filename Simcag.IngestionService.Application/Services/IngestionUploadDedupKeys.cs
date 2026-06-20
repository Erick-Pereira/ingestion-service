using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.IngestionDedup;

namespace Simcag.IngestionService.Application.Services;

/// <summary>Delega formato de chave ao contrato partilhado Redis.</summary>
public static class IngestionUploadDedupKeys
{
    public static string Build(string tenantId, FileHash fileHash) =>
        IngestionDedupRedisKeys.BuildFileHashKey(tenantId, fileHash.Value);

    public static string BuildDocumentIndex(string documentId) =>
        IngestionDedupRedisKeys.BuildDocumentIndexKey(documentId);
}
