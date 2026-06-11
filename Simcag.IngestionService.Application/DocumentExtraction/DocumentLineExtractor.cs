using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Application.DocumentExtraction.Profiles;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.DocumentExtraction;

public sealed class DocumentLineExtractor : IDocumentLineExtractor
{
    private readonly IReadOnlyList<IExtractionProfile> _profiles;
    private readonly IExtractionProfile _fallback;
    private readonly ILogger<DocumentLineExtractor> _logger;

    public DocumentLineExtractor(
        IEnumerable<IExtractionProfile> profiles,
        IExtractionProfile fallback,
        ILogger<DocumentLineExtractor> logger)
    {
        _profiles = profiles.Where(p => p is not GenericLineScannerProfile).ToList();
        _fallback = fallback;
        _logger = logger;
    }

    public ParseDocumentResult Extract(string rawText, DocumentType documentTypeHint)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        var sanitized = DocumentParsers.MaskBrazilianTaxIds(rawText);
        var resolvedType = DocumentParsers.ResolveDocumentType(documentTypeHint, sanitized);
        var context = new ExtractionContext(sanitized, documentTypeHint, resolvedType);

        foreach (var profile in _profiles.OrderByDescending(p => p.Score(context)))
        {
            var score = profile.Score(context);
            if (score <= 0)
                continue;

            var items = profile.Extract(context);
            if (items.Count >= profile.MinimumItems)
            {
                _logger.LogDebug(
                    "Perfil {ProfileId} (score {Score}): {LineCount} itens | tipo: {DocType}",
                    profile.ProfileId,
                    score,
                    items.Count,
                    resolvedType);
                return new ParseDocumentResult(items.ToList(), resolvedType);
            }
        }

        var fallbackItems = _fallback.Extract(context);
        _logger.LogDebug(
            "Fallback {ProfileId}: {LineCount} itens | tipo: {DocType}",
            _fallback.ProfileId,
            fallbackItems.Count,
            resolvedType);
        return new ParseDocumentResult(fallbackItems.ToList(), resolvedType);
    }
}
