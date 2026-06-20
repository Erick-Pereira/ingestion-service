using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.ValueObjects;
using StackExchange.Redis;

namespace Simcag.IngestionService.Infrastructure.Dedup;

/// <summary>Dedupe partilhado entre réplicas do ingestion-service (Redis). TTL 30 dias.</summary>
public sealed class IngestionUploadDedupRedisStore : IIngestionUploadDedupStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<IngestionUploadDedupRedisStore> _log;

    public IngestionUploadDedupRedisStore(IConnectionMultiplexer mux, ILogger<IngestionUploadDedupRedisStore> log)
    {
        _mux = mux;
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
        var db = _mux.GetDatabase();
        var raw = db.StringGet(key);
        if (raw.IsNullOrEmpty)
            return false;

        if (!IngestionDedupEntryMapper.TryDeserialize(raw.ToString(), out var parsed))
        {
            _log.LogWarning("Entrada de dedupe Redis inválida para chave {Key}; ignorada.", key);
            return false;
        }

        entry = parsed;
        _log.LogInformation(
            "Upload deduplicado (Redis): documento {DocumentId}",
            entry.DocumentId);
        return true;
    }

    private void SetKey(string key, IngestionDedupEntry entry)
    {
        var json = IngestionDedupEntryMapper.Serialize(entry);
        var db = _mux.GetDatabase();
        _ = db.StringSet(key, json, Ttl);
    }
}
