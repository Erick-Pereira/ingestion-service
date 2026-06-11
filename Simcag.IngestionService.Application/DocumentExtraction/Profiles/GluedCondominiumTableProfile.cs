using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction.Profiles;

/// <summary>Relatório condominial com linhas coladas (categoria+descrição+valor).</summary>
public sealed class GluedCondominiumTableProfile : IExtractionProfile
{
    public string ProfileId => "br.glued_condominium_table.v1";

    public int MinimumItems => 2;

    public int Score(ExtractionContext context)
    {
        if (DocumentParsers.LooksLikeGluedCondominioInvoice(context.SanitizedText))
            return 70;
        return 0;
    }

    public IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context) =>
        DocumentParsers.TryExtractGluedCondominioInvoiceLineItems(context.SanitizedText);
}
