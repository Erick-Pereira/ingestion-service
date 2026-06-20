using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Application.Services;

/// <summary>
/// Evita segundo processamento quando o mesmo ficheiro (SHA-256) é enviado outra vez pelo mesmo tenant.
/// </summary>
public interface IIngestionUploadDedupStore
{
    bool TryGet(string tenantId, FileHash fileHash, out IngestionDedupEntry entry);

    void Remember(string tenantId, FileHash fileHash, IngestionDedupEntry entry);

    void RememberDocumentIndex(string documentId, IngestionDedupEntry entry);
}
