using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction.Profiles;

/// <summary>Tabela de produtos BR (NF-e/DANFE): código, NCM, UN/UNID., qty, valores.</summary>
public sealed class TabularProductTableProfile : IExtractionProfile
{
    public string ProfileId => "br.tabular_product_table.v1";

    public int MinimumItems => 1;

    public int Score(ExtractionContext context)
    {
        if (DocumentParsers.LooksLikeDanfeNfe(context.SanitizedText))
            return 75;
        return 0;
    }

    public IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context) =>
        DocumentParsers.TryExtractDanfeNfeProductLineItems(context.SanitizedText);
}
