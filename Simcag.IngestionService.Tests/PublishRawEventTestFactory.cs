using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Simcag.IngestionService.Application.Configuration;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging.Contracts;

namespace Simcag.IngestionService.Tests;

internal static class PublishRawEventTestFactory
{
    internal static DataIngestedEvent? LastDataIngestedEvent { get; private set; }

    internal static PublishRawEventUseCase CreateUseCase()
    {
        LastDataIngestedEvent = null;
        return new PublishRawEventUseCase(
            new CapturingPublisher<RawFinancialDataEvent>(),
            new CapturingPublisher<DataIngestedEvent>(evt => LastDataIngestedEvent = evt),
            NullLogger<PublishRawEventUseCase>.Instance,
            Options.Create(new IngestionEventPublishingOptions { PublishLegacyRawFinancialEvent = false }));
    }

    internal static RawDocument BuildDocument(IReadOnlyList<ExtractedLineItem> lines, string? rawText = null)
    {
        var doc = new RawDocument(
            id: Guid.NewGuid().ToString(),
            fileName: "nfe.pdf",
            fileExtension: ".pdf",
            mimeType: "application/pdf",
            fileSize: 1024,
            fileHash: FileHash.FromHex(new string('a', 64)),
            source: "upload",
            origin: "manual",
            uploadedAt: DateTime.UtcNow);

        doc.SetTenantId(Guid.NewGuid().ToString());
        doc.SetUploadedBy(Guid.NewGuid());
        doc.SetRawText(rawText ?? "DANFE NF-e");
        doc.SetDocumentType(DocumentType.NotaFiscal);
        doc.SetExtractedLineItems(lines.ToList());
        doc.SetContent(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        return doc;
    }

    private sealed class CapturingPublisher<TEvent>(Action<TEvent>? onPublish = null) : IEventPublisher<TEvent>
        where TEvent : IEvent
    {
        public Task PublishAsync(TEvent @event, CancellationToken cancellationToken = default)
        {
            onPublish?.Invoke(@event);
            return Task.CompletedTask;
        }
    }
}
