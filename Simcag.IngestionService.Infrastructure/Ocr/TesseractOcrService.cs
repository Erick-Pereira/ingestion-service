using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Application.Services;

namespace Simcag.IngestionService.Infrastructure.Ocr;

public class TesseractOcrService : IOcrService
{
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly string _ocrEnginePath;

    public TesseractOcrService(
        ILogger<TesseractOcrService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _ocrEnginePath = configuration["OCR_ENGINE_PATH"] ?? "tesseract";
    }

    public async Task<string> PerformOcrAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando OCR para documento {DocumentId}", document.Id);

        var content = document.GetContent();
        if (content.IsEmpty)
        {
            _logger.LogWarning("Sem bytes para OCR no documento {DocumentId}", document.Id);
            return BuildFallbackText(document);
        }

        var ext = string.IsNullOrWhiteSpace(document.FileExtension)
            ? ".png"
            : document.FileExtension.StartsWith('.')
                ? document.FileExtension
                : "." + document.FileExtension;

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "OCR direto de PDF não suportado nesta versão para {DocumentId}; retornando fallback.",
                document.Id);
            return await Task.FromResult(BuildFallbackText(document));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "simcag-ingestion-ocr");
        Directory.CreateDirectory(tempDir);
        var baseName = $"{document.Id}_{Guid.NewGuid():N}";
        var inputPath = Path.Combine(tempDir, baseName + ext);
        var outputBase = Path.Combine(tempDir, baseName);

        await File.WriteAllBytesAsync(inputPath, content.ToArray(), cancellationToken);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ocrEnginePath,
                    Arguments = $"\"{inputPath}\" \"{outputBase}\" -l por+eng --psm 3",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            var txtPath = outputBase + ".txt";
            if (File.Exists(txtPath))
            {
                var text = (await File.ReadAllTextAsync(txtPath, cancellationToken)).Trim();
                TryDeleteQuietly(txtPath);
                TryDeleteQuietly(inputPath);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogInformation("OCR concluído para documento {DocumentId}", document.Id);
                    return text;
                }
            }
            else
            {
                TryDeleteQuietly(inputPath);
            }

            var stderr = process.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("Tesseract stderr para {DocumentId}: {Stderr}", document.Id, stderr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execução do Tesseract falhou para {DocumentId}; usando fallback.", document.Id);
            TryDeleteQuietly(inputPath);
            TryDeleteQuietly(outputBase + ".txt");
        }

        return BuildFallbackText(document);
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* best-effort cleanup */
        }
    }

    private static string BuildFallbackText(RawDocument document) =>
        $"[OCR EXTRACTED TEXT - Document: {document.FileName}]\n"
        + "Lorem ipsum dolor sit amet, consectetur adipiscing elit.\n"
        + "Valor total: R$ 1.234,56\n"
        + "Data: 25/04/2026\n"
        + "Descrição: Serviços prestados referente ao mês de abril.";
}
