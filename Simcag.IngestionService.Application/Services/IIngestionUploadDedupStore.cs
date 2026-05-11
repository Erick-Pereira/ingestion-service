using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Application.Services;

/// <summary>
/// Evita segundo processamento completo quando o mesmo ficheiro (mesmo SHA-256) é enviado outra vez pelo mesmo tenant.
/// </summary>
public interface IIngestionUploadDedupStore
{
    bool TryGet(string tenantId, FileHash fileHash, out IngestionDedupEntry entry);

    void Remember(string tenantId, FileHash fileHash, IngestionDedupEntry entry);
}
