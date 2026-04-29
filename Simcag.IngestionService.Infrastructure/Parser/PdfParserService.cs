using System.Text;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Application.Services;
using UglyToad.PdfPig;

namespace Simcag.IngestionService.Infrastructure.Parser;

public class PdfParserService : IPdfParserService
{
    private readonly ILogger<PdfParserService> _logger;

    public PdfParserService(ILogger<PdfParserService> logger)
    {
        _logger = logger;
    }

    public Task<string> ExtractTextAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Extraindo texto via PDF parser para documento {DocumentId}", document.Id);

        var content = document.GetContent();
        if (content.IsEmpty)
        {
            _logger.LogWarning("Sem bytes do PDF para {DocumentId}; retornando texto vazio.", document.Id);
            return Task.FromResult(string.Empty);
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

            return Task.FromResult(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Falha ao extrair texto via PdfPig para documento {DocumentId}; pipeline pode usar OCR.",
                document.Id);
            return Task.FromResult(string.Empty);
        }
    }
}
