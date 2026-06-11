using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction.Profiles;

/// <summary>NFSe com secção DISCRIMINAÇÃO colada (Prefeitura).</summary>
public sealed class ServiceDiscriminationProfile : IExtractionProfile
{
    public string ProfileId => "br.service_discrimination.v1";

    public int MinimumItems => 1;

    public int Score(ExtractionContext context)
    {
        if (DocumentParsers.LooksLikeGluedNfsePrefeitura(context.SanitizedText))
            return 80;
        return 0;
    }

    public IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context) =>
        DocumentParsers.TryExtractGluedNfseDiscriminacaoLineItems(context.SanitizedText);
}
