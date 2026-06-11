using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction.Profiles;

/// <summary>Fallback linha-a-linha quando nenhum perfil tabular se aplica.</summary>
public sealed class GenericLineScannerProfile : IExtractionProfile
{
    public string ProfileId => "generic.line_scanner.v1";

    public int MinimumItems => 0;

    public int Score(ExtractionContext _) => 1;

    public IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context) =>
        DocumentParsers.ParseLineItems(context.SanitizedText, context.ResolvedDocumentType);
}
