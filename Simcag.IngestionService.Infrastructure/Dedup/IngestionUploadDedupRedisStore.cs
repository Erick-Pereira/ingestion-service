using System.Text.Json;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.ValueObjects;
using StackExchange.Redis;

namespace Simcag.IngestionService.Infrastructure.Dedup;

/// <summary>Dedupe partilhado entre réplicas do ingestion-service (Redis). TTL 30 dias, alinhado à memória.</summary>
public sealed class IngestionUploadDedupRedisStore : IIngestionUploadDedupStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IConnectionMultiplexer _mux;
    private readonly ILogger<IngestionUploadDedupRedisStore> _log;

    public IngestionUploadDedupRedisStore(IConnectionMultiplexer mux, ILogger<IngestionUploadDedupRedisStore> log)
    {
        _mux = mux;
        _log = log;
    }

    public bool TryGet(string tenantId, FileHash fileHash, out IngestionDedupEntry entry)
    {
        entry = default!;
        var key = IngestionUploadDedupKeys.Build(tenantId, fileHash);
        var db = _mux.GetDatabase();
        var raw = db.StringGet(key);
        if (raw.IsNullOrEmpty)
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<IngestionDedupEntry>(raw.ToString(), Json);
            if (parsed is null)
                return false;
            entry = parsed;
            _log.LogInformation(
                "Upload deduplicado (Redis): tenant {TenantId}, hash {HashPrefix}… → documento {DocumentId}",
                tenantId,
                fileHash.Value.Length >= 12 ? fileHash.Value[..12] : fileHash.Value,
                entry.DocumentId);
            return true;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Entrada de dedupe Redis inválida para chave {Key}; ignorada.", key);
            return false;
        }
    }

    public void Remember(string tenantId, FileHash fileHash, IngestionDedupEntry entry)
    {
        var key = IngestionUploadDedupKeys.Build(tenantId, fileHash);
        var json = JsonSerializer.Serialize(entry, Json);
        var db = _mux.GetDatabase();
        _ = db.StringSet(key, json, Ttl);
    }
}
