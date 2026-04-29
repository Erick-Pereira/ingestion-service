using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace Simcag.IngestionService.Application.Services;

public class IngestionOrchestrator
{
    private readonly IIngestDocumentUseCase _ingestDocumentUseCase;
    private readonly IExtractTextUseCase _extractTextUseCase;
    private readonly IParseDocumentUseCase _parseDocumentUseCase;
    private readonly IPublishRawEventUseCase _publishRawEventUseCase;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        IIngestDocumentUseCase ingestDocumentUseCase,
        IExtractTextUseCase extractTextUseCase,
        IParseDocumentUseCase parseDocumentUseCase,
        IPublishRawEventUseCase publishRawEventUseCase,
        ILogger<IngestionOrchestrator> logger)
    {
        _ingestDocumentUseCase = ingestDocumentUseCase;
        _extractTextUseCase = extractTextUseCase;
        _parseDocumentUseCase = parseDocumentUseCase;
        _publishRawEventUseCase = publishRawEventUseCase;
        _logger = logger;
    }

    public async Task<IngestionOrchestratorResult> OrchestrateIngestionAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        long fileSize,
        string source,
        string origin,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando orquestração de ingestão para arquivo {FileName}", fileName);

        try
        {
            var document = await _ingestDocumentUseCase.ExecuteAsync(
                fileStream,
                fileName,
                mimeType,
                fileSize,
                source,
                origin,
                tenantId,
                cancellationToken);

            if (!document.HasIngestIntegrity())
            {
                return IngestionOrchestratorResult.Failure(
                    "Documento inválido para processamento",
                    new[] { "Arquivo vazio ou metadados incompletos após a ingestão" });
            }

            _logger.LogInformation("Documento {DocumentId} ingerido com sucesso", document.Id);

            var rawText = await _extractTextUseCase.ExtractAsync(document, cancellationToken);

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return IngestionOrchestratorResult.Failure(
                    "Falha na extração de texto",
                    new[] { "Não foi possível extrair texto do documento" });
            }

            document.SetRawText(rawText);

            _logger.LogInformation(
                "Texto extraído do documento {DocumentId}: {CharCount} caracteres",
                document.Id,
                rawText.Length);

            var parseResult = _parseDocumentUseCase.Execute(rawText, document.DocumentType);
            document.SetExtractedLineItems(parseResult.LineItems);

            if (document.DocumentType == DocumentType.Desconhecido)
                document.SetDocumentType(parseResult.DocumentType);

            document.MarkAsProcessed();
            _logger.LogInformation(
                "Documento {DocumentId} processado com {LineCount} itens extraídos",
                document.Id,
                parseResult.LineItems.Count);

            if (!document.CanPublishRawEvent())
            {
                return IngestionOrchestratorResult.Failure(
                    "Documento sem texto bruto para publicação",
                    new[] { "RawText vazio após processamento" });
            }

            await _publishRawEventUseCase.PublishAsync(document, cancellationToken);

            return IngestionOrchestratorResult.Success(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na orquestração de ingestão para arquivo {FileName}", fileName);
            return IngestionOrchestratorResult.Failure(
                "Erro interno na orquestração",
                new[] { ex.Message });
        }
    }
}
