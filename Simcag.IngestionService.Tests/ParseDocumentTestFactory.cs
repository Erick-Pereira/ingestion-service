using Microsoft.Extensions.Logging.Abstractions;
using Simcag.IngestionService.Application.DocumentExtraction;
using Simcag.IngestionService.Application.DocumentExtraction.Profiles;
using Simcag.IngestionService.Application.UseCases;

namespace Simcag.IngestionService.Tests;

internal static class ParseDocumentTestFactory
{
    internal static ParseDocumentUseCase CreateUseCase()
    {
        var fallback = new GenericLineScannerProfile();
        IExtractionProfile[] profiles =
        [
            new ServiceDiscriminationProfile(),
            new TabularProductTableProfile(),
            new GluedCondominiumTableProfile(),
            new CompactExpenseReportProfile(),
        ];
        var extractor = new DocumentLineExtractor(
            profiles,
            fallback,
            NullLogger<DocumentLineExtractor>.Instance);
        return new ParseDocumentUseCase(extractor);
    }
}
