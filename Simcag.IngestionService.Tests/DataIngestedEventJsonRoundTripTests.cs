using System.Text.Json;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Xunit;

namespace Simcag.IngestionService.Tests;

public sealed class DataIngestedEventJsonRoundTripTests
{
    private static readonly JsonSerializerOptions RabbitMqLikeJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
    };

    [Fact]
    public void MessageEnvelope_DataIngestedEvent_preserves_ExtractedFields_Lines()
    {
        var evt = new DataIngestedEvent
        {
            DocumentId = Guid.Parse("cdd77e13-7c61-4d4b-b30d-adadb51002f2"),
            TenantId = Guid.Parse("9a3f8805-4885-4742-8aff-0d0ac89eda96"),
            FileHash = "ab",
            Source = "PDF",
            DocumentType = "BALANCE_SHEET",
            RawText = "x",
            ExtractedFields = new ExtractedFields
            {
                Amount = 20350m,
                Description = "BALANCE_SHEET — 9 itens",
                Lines =
                [
                    new IngestedExpenseLine { Description = "Manutenção — Elevador", Amount = 100m },
                    new IngestedExpenseLine { Description = "Serviços — Limpeza", Amount = 200m }
                ],
                Extra = new Dictionary<string, object?> { ["lineItemCount"] = 9 }
            },
            UploadedBy = Guid.Empty,
            UploadedAt = DateTime.UtcNow
        };

        var envelope = MessageEnvelope<DataIngestedEvent>.Create(evt);
        var json = JsonSerializer.Serialize(envelope, RabbitMqLikeJson);
        var back = JsonSerializer.Deserialize<MessageEnvelope<DataIngestedEvent>>(json, RabbitMqLikeJson);

        Assert.NotNull(back?.Data?.ExtractedFields.Lines);
        Assert.Equal(2, back.Data.ExtractedFields.Lines!.Count);
        Assert.Equal("Manutenção — Elevador", back.Data.ExtractedFields.Lines[0].Description);
        Assert.Equal(100m, back.Data.ExtractedFields.Lines[0].Amount);
    }

    [Fact]
    public void ExtractedFields_camelCase_property_names_require_case_insensitive_deserializer()
    {
        var json = """{"amount":20350,"description":"BALANCE_SHEET — 9 itens","lines":[{"description":"Item A","amount":10}],"extra":{"lineItemCount":9}}""";

        var strict = JsonSerializer.Deserialize<ExtractedFields>(json);
        // Raiz: "amount" ≠ "Amount" sem PropertyNameCaseInsensitive
        Assert.Null(strict?.Amount);
        // Linhas: <see cref="ExtractedFields.Lines"/> tem [JsonPropertyName("lines")] — faz bind com JSON camelCase.
        Assert.NotNull(strict?.Lines);
        Assert.Single(strict.Lines);
        Assert.Equal("Item A", strict.Lines[0].Description);
        Assert.Equal(10m, strict.Lines[0].Amount);

        var relaxed = JsonSerializer.Deserialize<ExtractedFields>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(relaxed?.Lines);
        Assert.Single(relaxed.Lines);
        Assert.Equal("Item A", relaxed.Lines[0].Description);
        Assert.Equal(10m, relaxed.Lines[0].Amount);
        Assert.Equal(20350m, relaxed.Amount);
    }
}
