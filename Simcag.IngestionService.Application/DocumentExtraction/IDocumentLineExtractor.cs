using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.DocumentExtraction;

public interface IDocumentLineExtractor
{
    ParseDocumentResult Extract(string rawText, DocumentType documentTypeHint);
}
