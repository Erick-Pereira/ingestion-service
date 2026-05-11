using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Application.Services;

/// <summary>Armazenamento em memória (por instância da API). Com TTL longo; reinício do processo limpa o índice.</summary>
public sealed class IngestionUploadDedupMemoryStore : IIngestionUploadDedupStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<IngestionUploadDedupMemoryStore> _log;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public IngestionUploadDedupMemoryStore(IMemoryCache cache, ILogger<IngestionUploadDedupMemoryStore> log)
    {
        _cache = cache;
        _log = log;
    }

    public bool TryGet(string tenantId, FileHash fileHash, out IngestionDedupEntry entry)
    {
        entry = default!;
        var key = BuildKey(tenantId, fileHash);
        if (!_cache.TryGetValue(key, out IngestionDedupEntry? cached) || cached is null)
            return false;

        entry = cached;
        _log.LogInformation(
            "Upload deduplicado: tenant {TenantId}, hash {HashPrefix}… → documento {DocumentId}",
            tenantId,
            fileHash.Value.Length >= 12 ? fileHash.Value[..12] : fileHash.Value,
            entry.DocumentId);
        return true;
    }

    public void Remember(string tenantId, FileHash fileHash, IngestionDedupEntry entry)
    {
        var key = BuildKey(tenantId, fileHash);
        _cache.Set(key, entry, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl });
    }

    private static string BuildKey(string tenantId, FileHash fileHash) =>
        $"ingestion:upload:{tenantId.Trim().ToLowerInvariant()}:{fileHash.Value}";
}
