using Simcag.IngestionService.Application.DocumentExtraction;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.IngestionService.Application.UseCases;
using Microsoft.Extensions.Logging;
using Simcag.Shared.ErrorHandling;

namespace Simcag.IngestionService.Application.Services;

public class IngestionOrchestrator
{
    private readonly IIngestDocumentUseCase _ingestDocumentUseCase;
    private readonly IExtractTextUseCase _extractTextUseCase;
    private readonly IParseDocumentUseCase _parseDocumentUseCase;
    private readonly IPublishRawEventUseCase _publishRawEventUseCase;
    private readonly IIngestionUploadDedupStore? _uploadDedup;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        IIngestDocumentUseCase ingestDocumentUseCase,
        IExtractTextUseCase extractTextUseCase,
        IParseDocumentUseCase parseDocumentUseCase,
        IPublishRawEventUseCase publishRawEventUseCase,
        ILogger<IngestionOrchestrator> logger,
        IIngestionUploadDedupStore? uploadDedup = null)
    {
        _ingestDocumentUseCase = ingestDocumentUseCase;
        _extractTextUseCase = extractTextUseCase;
        _parseDocumentUseCase = parseDocumentUseCase;
        _publishRawEventUseCase = publishRawEventUseCase;
        _uploadDedup = uploadDedup;
        _logger = logger;
    }

    /// <summary>Chave estável para dedupe: mesmo ficheiro sem tenant no pedido continua a casar com uploads anteriores.</summary>
    private static string NormalizeDedupTenantKey(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) ? "__no_tenant__" : tenantId.Trim();

    public async Task<IngestionOrchestratorResult> OrchestrateIngestionAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        long fileSize,
        string source,
        string origin,
        string? tenantId,
        Guid? uploadedBy,
        bool forceNewDocument,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando orquestração de ingestão para arquivo {FileName}", fileName);

        try
        {
            await using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            var fileBytes = buffer.ToArray();
            if (fileBytes.Length == 0)
            {
                return IngestionOrchestratorResult.Failure(
                    "Documento inválido para processamento",
                    new[] { "Arquivo vazio" });
            }

            var fileHash = FileHash.ComputeSha256(fileBytes);
            var dedupTenant = NormalizeDedupTenantKey(tenantId);
            if (!forceNewDocument
                && _uploadDedup is not null
                && _uploadDedup.TryGet(dedupTenant, fileHash, out var priorHash))
            {
                return IngestionOrchestratorResult.Duplicate(priorHash, IngestionDuplicateReasons.FileHash);
            }

            using var readStream = new MemoryStream(fileBytes, writable: false);
            var document = await _ingestDocumentUseCase.ExecuteAsync(
                readStream,
                fileName,
                mimeType,
                fileSize,
                source,
                origin,
                tenantId,
                cancellationToken);

            document.SetUploadedBy(uploadedBy);

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

            if (document.DocumentType == DocumentType.Desconhecido)
                document.SetDocumentType(parseResult.DocumentType);
            else if (document.DocumentType == DocumentType.NotaFiscal
                     && parseResult.DocumentType != DocumentType.NotaFiscal
                     && parseResult.DocumentType != DocumentType.Desconhecido)
                document.SetDocumentType(parseResult.DocumentType);

            document.SetExtractedLineItems(parseResult.LineItems);

            var nfeMeta = NfeDocumentMetadataExtractor.Extract(rawText);

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

            var publishOutcome = await _publishRawEventUseCase.PublishAsync(document, nfeMeta, cancellationToken);

            if (_uploadDedup is not null)
            {
                var dedupEntry = new IngestionDedupEntry(
                    DocumentId: document.Id,
                    TenantId: document.TenantId,
                    DocumentType: document.DocumentType.ToString(),
                    ExtractedItemCount: document.ExtractedLineItems.Count,
                    PublishedDataIngestedEvent: publishOutcome.DataIngestedEventPublished,
                    FileHash: fileHash.Value);

                _uploadDedup.Remember(dedupTenant, fileHash, dedupEntry);
                _uploadDedup.RememberDocumentIndex(document.Id, dedupEntry);
            }

            return IngestionOrchestratorResult.Success(document, publishOutcome.DataIngestedEventPublished);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na orquestração de ingestão para arquivo {FileName}", fileName);
            return IngestionOrchestratorResult.Failure(
                "Erro interno na orquestração",
                new[] { ErrorSanitizer.Sanitize(ex.Message) });
        }
    }
}
