using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.UseCases;

public interface IParseDocumentUseCase
{
    /// <summary>
    /// Extrai linhas estruturadas e, se o tipo ainda for desconhecido, infere pelo texto.
    /// </summary>
    ParseDocumentResult Execute(string rawText, DocumentType documentType);
}
