using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Application.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.UseCases;

public class ExtractTextUseCase : IExtractTextUseCase
{
    private readonly IOcrService _ocrService;
    private readonly IPdfParserService _pdfParser;
    private readonly IExcelParserService _excelParser;
    private readonly ILogger<ExtractTextUseCase> _logger;

    public ExtractTextUseCase(
        IOcrService ocrService,
        IPdfParserService pdfParser,
        IExcelParserService excelParser,
        ILogger<ExtractTextUseCase> logger)
    {
        _ocrService = ocrService;
        _pdfParser = pdfParser;
        _excelParser = excelParser;
        _logger = logger;
    }

    public async Task<string> ExtractAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string extractedText = document.MimeType.ToLower() switch
            {
                // Extract text from PDF
                string pdfType when pdfType.Contains("pdf") =>
                    await ExtractFromPdfAsync(document, cancellationToken),

                // Extract data from Excel
                string excelType when excelType.Contains("excel") || excelType.Contains("sheet") =>
                    await ExtractFromExcelAsync(document, cancellationToken),

                // Extract text from images using OCR
                string imgType when imgType.Contains("image") =>
                    await ExtractFromImageAsync(document, cancellationToken),

                // Text/CSV files
                string textType when textType.Contains("text") || textType.Contains("csv") =>
                    await ExtractFromTextAsync(document, cancellationToken),

                _ => throw new NotSupportedException($"Tipo MIME não suportado: {document.MimeType}")
            };

            _logger.LogInformation(
                "Texto extraído do documento {DocumentId}: {Length} caracteres",
                document.Id,
                extractedText.Length);

            return extractedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao extrair texto do documento {DocumentId}",
                document.Id);
            throw;
        }
    }

    private async Task<string> ExtractFromPdfAsync(RawDocument document, CancellationToken cancellationToken)
    {
        try
        {
            // Try to extract text directly from PDF first
            var text = await _pdfParser.ExtractTextAsync(document, cancellationToken);

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Falha ao extrair texto via PDF parser, tentando OCR para {DocumentId}",
                document.Id);
        }

        // Fall back to OCR for scanned PDFs
        _logger.LogInformation("Usando OCR para PDF escaneado {DocumentId}", document.Id);
        return await _ocrService.PerformOcrAsync(document, cancellationToken);
    }

    private async Task<string> ExtractFromExcelAsync(RawDocument document, CancellationToken cancellationToken)
    {
        var data = await _excelParser.ExtractDataAsync(document, cancellationToken);

        // Convert structured data to text format for further processing
        var textBuilder = new System.Text.StringBuilder();

        foreach (var row in data)
            textBuilder.AppendLine(string.Join(" | ", row));

        if (textBuilder.Length == 0)
            textBuilder.AppendLine($"(planilha sem linhas legíveis: {document.FileName})");

        return textBuilder.ToString();
    }

    private async Task<string> ExtractFromImageAsync(RawDocument document, CancellationToken cancellationToken)
    {
        return await _ocrService.PerformOcrAsync(document, cancellationToken);
    }

    private Task<string> ExtractFromTextAsync(RawDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = document.GetContent();
        if (content.IsEmpty)
        {
            _logger.LogWarning("Conteúdo vazio para arquivo de texto {DocumentId}", document.Id);
            return Task.FromResult(string.Empty);
        }

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(content.Span);
            if (string.IsNullOrWhiteSpace(text) && content.Length > 0)
            {
                text = System.Text.Encoding.Latin1.GetString(content.Span);
            }

            return Task.FromResult(text.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao decodificar texto/CSV para {DocumentId}", document.Id);
            return Task.FromResult(string.Empty);
        }
    }
}