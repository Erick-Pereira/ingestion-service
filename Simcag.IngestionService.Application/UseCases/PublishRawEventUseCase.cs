using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simcag.IngestionService.Application.Configuration;
using Simcag.IngestionService.Application.DocumentExtraction;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.Shared.Events;
using Simcag.Shared.Finance;
using Simcag.Shared.Messaging.Contracts;

namespace Simcag.IngestionService.Application.UseCases;

/// <summary>
/// Publica eventos no RabbitMQ após ingestão:
/// <list type="bullet">
/// <item><description><see cref="RawFinancialDataEvent"/> — legado; consumido pelo AI Service enquanto <c>Ingestion:PublishLegacyRawFinancialEvent</c> for <c>true</c> (ver <c>.env.example</c>).</description></item>
/// <item><description><see cref="DataIngestedEvent"/> — canónico v1 para o Processing Service, quando há <c>TenantId</c> e <c>DocumentId</c> válidos como GUID.</description></item>
/// </list>
/// Enriquecimento por IA entre serviços deve usar apenas este fluxo assíncrono (evita HTTP síncrono duplicado).
/// </summary>
public class PublishRawEventUseCase : IPublishRawEventUseCase
{
    private readonly IEventPublisher<RawFinancialDataEvent> _legacyRawPublisher;
    private readonly IEventPublisher<DataIngestedEvent> _dataIngestedPublisher;
    private readonly ILogger<PublishRawEventUseCase> _logger;
    private readonly IngestionEventPublishingOptions _publishOptions;

    public PublishRawEventUseCase(
        IEventPublisher<RawFinancialDataEvent> legacyRawPublisher,
        IEventPublisher<DataIngestedEvent> dataIngestedPublisher,
        ILogger<PublishRawEventUseCase> logger,
        IOptions<IngestionEventPublishingOptions> publishOptions)
    {
        _legacyRawPublisher = legacyRawPublisher;
        _dataIngestedPublisher = dataIngestedPublisher;
        _logger = logger;
        _publishOptions = publishOptions.Value;
    }

    public async Task<RawEventPublishOutcome> PublishAsync(
        RawDocument document,
        NfeDocumentMetadata? nfeMetadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawEvent = new RawFinancialDataEvent
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            UploadedBy = document.UploadedBy ?? Guid.Empty,
            Origin = document.Origin,
            RawText = document.RawText,
            DocumentType = document.DocumentType.ToString(),
            Source = document.Source,
            FileHash = document.FileHash.Value,
            ExtractedFields = ExtractLegacyFields(document.ExtractedLineItems),
            ExtractedItems = MapToFinancialItems(document.ExtractedLineItems),
            OccurredAt = document.UploadedAt,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            if (_publishOptions.PublishLegacyRawFinancialEvent)
            {
                await _legacyRawPublisher.PublishAsync(rawEvent, cancellationToken);

                _logger.LogInformation(
                    "RawFinancialDataEvent publicado para documento {DocumentId} | Tipo: {DocType} | Itens: {ItemCount}",
                    document.Id,
                    document.DocumentType,
                    document.ExtractedLineItems.Count);
            }
            else
            {
                _logger.LogInformation(
                    "RawFinancialDataEvent omitido (Ingestion:PublishLegacyRawFinancialEvent=false) para documento {DocumentId}",
                    document.Id);
            }

            if (TryBuildDataIngestedEvent(document, nfeMetadata, out var dataIngested))
            {
                await _dataIngestedPublisher.PublishAsync(dataIngested, cancellationToken);
                _logger.LogInformation(
                    "DataIngestedEvent publicado para documento {DocumentId} | Tenant: {TenantId}",
                    document.Id,
                    dataIngested.TenantId);
                return new RawEventPublishOutcome(DataIngestedEventPublished: true);
            }

            _logger.LogWarning(
                "DataIngestedEvent omitido para documento {DocumentId}: exige TenantId e DocumentId como GUID não vazios (processing não consome sem tenant).",
                document.Id);
            return new RawEventPublishOutcome(DataIngestedEventPublished: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao publicar eventos RabbitMQ para documento {DocumentId}",
                document.Id);
            throw;
        }
    }

    private static bool TryBuildDataIngestedEvent(
        RawDocument document,
        NfeDocumentMetadata? nfeMetadata,
        out DataIngestedEvent evt)
    {
        evt = default!;

        if (!Guid.TryParse(document.Id, out var documentGuid) || documentGuid == Guid.Empty)
            return false;

        if (!Guid.TryParse(document.TenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
            return false;

        var lines = document.ExtractedLineItems;
        var withAmount = lines.Where(li => li.Amount?.IsValid() == true).ToList();

        decimal? amount = withAmount.Count switch
        {
            0 => null,
            1 => withAmount[0].Amount!.Amount,
            _ => withAmount.Sum(li => li.Amount!.Amount)
        };

        var date = withAmount.FirstOrDefault(li => li.Date.HasValue)?.Date
                   ?? lines.FirstOrDefault(li => li.Date.HasValue)?.Date;

        var lineDtos = withAmount
            .Select(li => new IngestedExpenseLine
            {
                Description = string.IsNullOrWhiteSpace(li.Description)
                    ? $"Linha {li.LineNumber}"
                    : li.Description.Trim(),
                Amount = li.Amount!.Amount,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                ItemCode = li.ItemCode,
            })
            .ToList();

        string? headerDescription = withAmount.Count switch
        {
            0 => lines.FirstOrDefault(li => !string.IsNullOrWhiteSpace(li.Description))?.Description.Trim(),
            1 => string.IsNullOrWhiteSpace(withAmount[0].Description)
                ? null
                : withAmount[0].Description.Trim(),
            _ => $"{MapCanonicalDocumentType(document.DocumentType)} — {withAmount.Count} itens"
        };

        var extra = new Dictionary<string, object?> { ["lineItemCount"] = lines.Count };
        if (nfeMetadata?.AccessKey is { Length: 44 } accessKey)
            extra["nfeAccessKey"] = accessKey;
        if (!string.IsNullOrWhiteSpace(nfeMetadata?.NfeNumber))
            extra["nfeNumber"] = nfeMetadata.NfeNumber;
        if (!string.IsNullOrWhiteSpace(nfeMetadata?.NfeSeries))
            extra["nfeSeries"] = nfeMetadata.NfeSeries;
        if (!string.IsNullOrWhiteSpace(nfeMetadata?.IssuerTaxId))
            extra["nfeIssuerTaxId"] = nfeMetadata.IssuerTaxId;
        // Redundância: alguns pipelines deserializam ExtractedFields sem popular Lines; o processing reidrata disto.
        if (lineDtos.Count > 0)
            extra["ingestedLinesJson"] = JsonSerializer.Serialize(lineDtos);

        var supplierHint = BrazilianDocumentSupplierExtractor.TryExtract(document.RawText);

        evt = new DataIngestedEvent
        {
            DocumentId = documentGuid,
            TenantId = tenantGuid,
            FileHash = document.FileHash.Value,
            Source = MapCanonicalSource(document),
            DocumentType = MapCanonicalDocumentType(document.DocumentType),
            RawText = document.RawText ?? string.Empty,
            ExtractedFields = new ExtractedFields
            {
                Amount = amount,
                Date = date,
                Description = headerDescription,
                SupplierName = supplierHint.Name,
                SupplierTaxId = supplierHint.TaxId,
                Lines = lineDtos.Count > 0 ? lineDtos : null,
                Extra = extra
            },
            UploadedBy = document.UploadedBy ?? Guid.Empty,
            UploadedAt = document.UploadedAt
        };

        return true;
    }

    private static string MapCanonicalSource(RawDocument document)
    {
        var ext = document.FileExtension.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "PDF",
            ".xlsx" or ".xls" => "EXCEL",
            ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" => "IMAGE_OCR",
            _ => "OTHER"
        };
    }

    private static string MapCanonicalDocumentType(DocumentType t) =>
        t switch
        {
            DocumentType.NotaFiscal => "INVOICE",
            DocumentType.Balancete => "BALANCE_SHEET",
            DocumentType.Recibo => "RECEIPT",
            DocumentType.Contrato => "CONTRACT",
            DocumentType.Boleto => "OTHER",
            DocumentType.Desconhecido => "OTHER",
            _ => "OTHER"
        };

    private static Dictionary<string, object?> ExtractLegacyFields(List<ExtractedLineItem> lineItems)
    {
        var fields = new Dictionary<string, object?>
        {
            ["lineItemCount"] = lineItems.Count,
            ["totalAmount"] = lineItems
                .Where(li => li.Amount?.IsValid() == true)
                .Sum(li => (decimal?)li.Amount?.Amount),
            ["itemsWithDate"] = lineItems.Count(li => li.Date.HasValue),
            ["itemsWithDescription"] = lineItems.Count(li => !string.IsNullOrWhiteSpace(li.Description)),
            ["averageConfidence"] = lineItems.Any()
                ? lineItems.Average(li => li.ConfidenceScore)
                : 0
        };

        var sampleItems = lineItems
            .Take(3)
            .Select(li => new
            {
                li.LineNumber,
                Amount = li.Amount?.Amount,
                li.Date,
                li.Description,
                li.ConfidenceScore
            })
            .ToList();

        fields["sampleItems"] = sampleItems;

        return fields;
    }

    /// <summary>Propaga linhas para o AI Service (<see cref="RawFinancialDataEvent.ExtractedItems"/>), evitando um único item genérico só com RawText.</summary>
    private static List<object>? MapToFinancialItems(List<ExtractedLineItem> lineItems)
    {
        if (lineItems.Count == 0)
            return null;

        var list = new List<object>();
        foreach (var li in lineItems)
        {
            var amt = li.Amount?.IsValid() == true ? li.Amount!.Amount : 0m;
            var desc = string.IsNullOrWhiteSpace(li.Description)
                ? $"Item linha {li.LineNumber}"
                : li.Description.Trim();

            if (amt <= 0 && string.IsNullOrWhiteSpace(li.Description))
                continue;

            list.Add(FinancialLineItemSemanticNormalizer.NormalizeFinancialItem(
                new FinancialItem
                {
                    Description = desc,
                    Amount = amt,
                    Quantity = li.Quantity is > 0m
                        ? (int)Math.Round(li.Quantity.Value, MidpointRounding.AwayFromZero)
                        : null,
                    UnitPrice = li.UnitPrice,
                    ItemCode = li.ItemCode,
                }));
        }

        return list.Count > 0 ? list : null;
    }
}
