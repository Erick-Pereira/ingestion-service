using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.UseCases;

/// <summary>Fachada de parsing — delega extração estrutural ao orquestrador de perfis.</summary>
public class ParseDocumentUseCase : IParseDocumentUseCase
{
    private readonly DocumentExtraction.IDocumentLineExtractor _extractor;

    public ParseDocumentUseCase(DocumentExtraction.IDocumentLineExtractor extractor)
    {
        _extractor = extractor;
    }

    public ParseDocumentResult Execute(string rawText, DocumentType documentType) =>
        _extractor.Extract(rawText, documentType);
}
