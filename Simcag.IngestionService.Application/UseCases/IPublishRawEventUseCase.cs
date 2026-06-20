using Simcag.IngestionService.Application.DocumentExtraction;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.UseCases;

public interface IPublishRawEventUseCase
{
    Task<RawEventPublishOutcome> PublishAsync(
        RawDocument document,
        NfeDocumentMetadata? nfeMetadata = null,
        CancellationToken cancellationToken = default);
}