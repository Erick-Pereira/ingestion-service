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

    public bool TryGet(string tenantId, FileHash fileHash, out IngestionDedupEntry entry) =>
        TryGetKey(IngestionUploadDedupKeys.Build(tenantId, fileHash), out entry);

    public void Remember(string tenantId, FileHash fileHash, IngestionDedupEntry entry) =>
        SetKey(IngestionUploadDedupKeys.Build(tenantId, fileHash), entry);

    public void RememberDocumentIndex(string documentId, IngestionDedupEntry entry) =>
        SetKey(IngestionUploadDedupKeys.BuildDocumentIndex(documentId), entry);

    private bool TryGetKey(string key, out IngestionDedupEntry entry)
    {
        entry = default!;
        if (!_cache.TryGetValue(key, out IngestionDedupEntry? cached) || cached is null)
            return false;

        entry = cached;
        _log.LogInformation(
            "Upload deduplicado (memória): documento {DocumentId}",
            entry.DocumentId);
        return true;
    }

    private void SetKey(string key, IngestionDedupEntry entry)
    {
        _cache.Set(key, entry, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl });
    }
}
