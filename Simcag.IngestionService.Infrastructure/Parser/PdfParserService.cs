using System.Text;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Application.Services;
using UglyToad.PdfPig;

namespace Simcag.IngestionService.Infrastructure.Parser;

public class PdfParserService : IPdfParserService
{
    private readonly ILogger<PdfParserService> _logger;
    private readonly IOcrService _ocrService;

    public PdfParserService(ILogger<PdfParserService> logger, IOcrService ocrService)
    {
        _logger = logger;
        _ocrService = ocrService;
    }

    public async Task<string> ExtractTextAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Extraindo texto via PDF parser para documento {DocumentId}", document.Id);

        var content = document.GetContent();
        if (content.IsEmpty)
        {
            _logger.LogWarning("Sem bytes do PDF para {DocumentId}; retornando texto vazio.", document.Id);
            return string.Empty;
        }

        try
        {
            using var ms = new MemoryStream(content.ToArray());
            using var pdf = PdfDocument.Open(ms);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(page.Text);
            }

            var text = sb.ToString().Trim();
            _logger.LogInformation(
                "Texto extraído via PDF parser para documento {DocumentId}: {Length} caracteres",
                document.Id,
                text.Length);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Falha ao extrair texto via PdfPig para documento {DocumentId}; tentando OCR como fallback.",
                document.Id);

            // Tentar OCR como fallback quando o parser falha
            try
            {
                var ocrResult = await (_ocrService?.PerformOcrAsync(document, cancellationToken) 
                                        ?? Task.FromResult<string>(string.Empty));
                if (!string.IsNullOrWhiteSpace(ocrResult))
                {
                    _logger.LogInformation("OCR fallback bem-sucedido para documento {DocumentId}", document.Id);
                    return ocrResult;
                }

                _logger.LogWarning("OCR retornou texto vazio para {DocumentId}; usando fallback heurístico.", document.Id);
            }
            catch (Exception ocrEx)
            {
                _logger.LogError(ocrEx, "OCR fallback também falhou para {DocumentId}, tentando fallback heurístico.", document.Id);
            }

            // Fallback heurístico como última opção
            var heuristicText = BuildHeuristicFallback(document);
            return heuristicText;
        }
    }

    private static string BuildHeuristicFallback(RawDocument document) =>
        $"[HEURISTIC FALLBACK - Document: {document.FileName ?? document.Id}]\n" +
        "WARNING: Could not extract text from PDF. Document may need manual review.";
}
