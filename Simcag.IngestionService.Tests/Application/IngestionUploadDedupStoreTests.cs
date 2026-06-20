using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.IngestionDedup;

namespace Simcag.IngestionService.Tests.Application;

public sealed class IngestionUploadDedupStoreTests
{
    [Fact]
    public void MemoryStore_blocks_second_upload_by_same_hash()
    {
        var store = new IngestionUploadDedupMemoryStore(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<IngestionUploadDedupMemoryStore>.Instance);
        const string tenant = "tenant-a";
        var hash = FileHash.FromHex(new string('a', 64));
        var entry = new IngestionDedupEntry(
            DocumentId: "11111111-1111-1111-1111-111111111111",
            TenantId: tenant,
            DocumentType: "NotaFiscal",
            ExtractedItemCount: 1,
            PublishedDataIngestedEvent: true,
            FileHash: hash.Value);

        store.Remember(tenant, hash, entry);

        Assert.True(store.TryGet(tenant, hash, out var byHash));
        Assert.Equal(entry.DocumentId, byHash.DocumentId);
    }

    [Fact]
    public void MemoryStore_does_not_dedupe_different_hash_even_with_same_nfe_metadata_in_payload()
    {
        var store = new IngestionUploadDedupMemoryStore(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<IngestionUploadDedupMemoryStore>.Instance);
        const string tenant = "tenant-a";
        var hashA = FileHash.FromHex(new string('a', 64));
        var hashB = FileHash.FromHex(new string('b', 64));
        const string sharedAccessKey = "53260312345678000190550100009041002000000001";

        store.Remember(tenant, hashA, new IngestionDedupEntry(
            DocumentId: "11111111-1111-1111-1111-111111111111",
            TenantId: tenant,
            DocumentType: "NotaFiscal",
            ExtractedItemCount: 1,
            PublishedDataIngestedEvent: true,
            NfeAccessKey: sharedAccessKey,
            NfeFallbackKey: "12345678000190:000904100:1",
            FileHash: hashA.Value));

        Assert.False(store.TryGet(tenant, hashB, out _));
    }

    [Fact]
    public void Redis_keys_are_stable_per_tenant_for_file_hash()
    {
        const string tenant = "Tenant-A";
        const string hash = "DEADBEEF";

        Assert.Equal(
            "ingestion:upload:tenant-a:deadbeef",
            IngestionDedupRedisKeys.BuildFileHashKey(tenant, hash));
    }
}
