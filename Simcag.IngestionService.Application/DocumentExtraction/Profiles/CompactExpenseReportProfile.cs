using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction.Profiles;

/// <summary>PDF condominial compacto (tabela Valor R$ numa linha longa).</summary>
public sealed class CompactExpenseReportProfile : IExtractionProfile
{
    public string ProfileId => "br.compact_expense_report.v1";

    public int MinimumItems => 3;

    public int Score(ExtractionContext context)
    {
        if (DocumentParsers.ShouldUseCompactCondominioExtraction(
                context.SanitizedText,
                context.ResolvedDocumentType))
            return 60;
        return 0;
    }

    public IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context) =>
        DocumentParsers.ExtractCompactCondominioExpenseRows(context.SanitizedText);
}
