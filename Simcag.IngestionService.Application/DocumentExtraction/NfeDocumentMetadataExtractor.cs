using System.Text.RegularExpressions;
using Simcag.IngestionService.Application.DocumentExtraction.Parsing;

namespace Simcag.IngestionService.Application.DocumentExtraction;

/// <summary>Metadados fiscais extraídos do texto bruto (NF-e).</summary>
public sealed record NfeDocumentMetadata(
    string? AccessKey,
    string? FallbackCompositeKey,
    string? NfeNumber,
    string? NfeSeries,
    string? IssuerTaxId);

public static class NfeDocumentMetadataExtractor
{
    public static NfeDocumentMetadata Extract(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new NfeDocumentMetadata(null, null, null, null, null);

        var accessKey = DocumentParsers.TryExtractNfeAccessKey(rawText);
        var number = DocumentParsers.TryExtractDanfeNfeNumber(rawText);
        var series = DocumentParsers.TryExtractDanfeNfeSeries(rawText);
        var issuer = DocumentParsers.TryExtractDanfeIssuerTaxId(rawText);
        var fallback = DocumentParsers.TryBuildNfeFallbackCompositeKey(issuer, number, series);

        return new NfeDocumentMetadata(accessKey, fallback, number, series, issuer);
    }
}
