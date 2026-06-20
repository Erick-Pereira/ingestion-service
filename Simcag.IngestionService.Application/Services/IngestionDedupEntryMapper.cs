using System.Text.Json;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.IngestionDedup;

namespace Simcag.IngestionService.Application.Services;

internal static class IngestionDedupEntryMapper
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IngestionDedupEntryPayload ToPayload(IngestionDedupEntry entry) =>
        new(
            entry.DocumentId,
            entry.TenantId,
            entry.DocumentType,
            entry.ExtractedItemCount,
            entry.PublishedDataIngestedEvent,
            entry.NfeAccessKey,
            entry.NfeFallbackKey,
            entry.ExpenseId,
            entry.FileHash,
            entry.DuplicateReason);

    public static IngestionDedupEntry FromPayload(IngestionDedupEntryPayload p) =>
        new(
            p.DocumentId,
            p.TenantId,
            p.DocumentType,
            p.ExtractedItemCount,
            p.PublishedDataIngestedEvent,
            p.NfeAccessKey,
            p.NfeFallbackKey,
            p.ExpenseId,
            p.FileHash,
            p.DuplicateReason);

    public static string Serialize(IngestionDedupEntry entry) =>
        JsonSerializer.Serialize(ToPayload(entry), Json);

    public static bool TryDeserialize(string raw, out IngestionDedupEntry entry)
    {
        entry = default!;
        try
        {
            var parsed = JsonSerializer.Deserialize<IngestionDedupEntryPayload>(raw, Json);
            if (parsed is null)
                return false;
            entry = FromPayload(parsed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
