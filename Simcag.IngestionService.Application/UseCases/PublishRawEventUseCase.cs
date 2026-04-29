using Simcag.IngestionService.Domain.Entities;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;
using Simcag.IngestionService.Application.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.UseCases;

public class PublishRawEventUseCase : IPublishRawEventUseCase
{
    private readonly IEventPublisher<RawFinancialDataEvent> _eventPublisher;
    private readonly ILogger<PublishRawEventUseCase> _logger;

    public PublishRawEventUseCase(
        IEventPublisher<RawFinancialDataEvent> eventPublisher,
        ILogger<PublishRawEventUseCase> logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task PublishAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build raw financial data event from document
        var rawEvent = new RawFinancialDataEvent
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            Origin = document.Origin,
            RawText = document.RawText,
            DocumentType = document.DocumentType.ToString(),
            Source = document.Source,
            FileHash = document.FileHash.Value,
            ExtractedFields = ExtractFields(document.ExtractedLineItems),
            OccurredAt = document.UploadedAt,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await _eventPublisher.PublishAsync(rawEvent, cancellationToken);

            _logger.LogInformation(
                "RawFinancialDataEvent publicado para documento {DocumentId} | Tipo: {DocType} | Itens: {ItemCount}",
                document.Id,
                document.DocumentType,
                document.ExtractedLineItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao publicar RawFinancialDataEvent para documento {DocumentId}",
                document.Id);
            throw;
        }
    }

    private Dictionary<string, object?> ExtractFields(List<ExtractedLineItem> lineItems)
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

        // Add first few line items for reference
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
}