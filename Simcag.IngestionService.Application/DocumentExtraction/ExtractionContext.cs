using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.DocumentExtraction;

/// <summary>Entrada imutável para perfis de extração de linhas.</summary>
public sealed class ExtractionContext
{
    public ExtractionContext(string sanitizedText, DocumentType hintDocumentType, DocumentType resolvedDocumentType)
    {
        SanitizedText = sanitizedText ?? throw new ArgumentNullException(nameof(sanitizedText));
        HintDocumentType = hintDocumentType;
        ResolvedDocumentType = resolvedDocumentType;
    }

    public string SanitizedText { get; }
    public DocumentType HintDocumentType { get; }
    public DocumentType ResolvedDocumentType { get; }
    public string Locale { get; init; } = "pt-BR";
}
