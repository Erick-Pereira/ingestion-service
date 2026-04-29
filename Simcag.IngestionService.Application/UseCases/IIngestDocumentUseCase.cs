using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.UseCases;

public interface IIngestDocumentUseCase
{
    Task<RawDocument> ExecuteAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        long fileSize,
        string source,
        string origin,
        string? tenantId,
        CancellationToken cancellationToken = default);
}
