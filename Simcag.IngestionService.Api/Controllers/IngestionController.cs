using Microsoft.AspNetCore.Mvc;
using Simcag.IngestionService.Api.Contracts;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.Enums;
using Simcag.Shared.Security;

namespace Simcag.IngestionService.Api.Controllers;

[ApiController]
[Route("api/ingestion")]
public class IngestionController : ControllerBase
{
    private readonly IngestionOrchestrator _orchestrator;
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IngestionOrchestrator orchestrator,
        IIngestionService ingestionService,
        ILogger<IngestionController> logger)
    {
        _orchestrator = orchestrator;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Endpoint de upload de documentos financeiros
    /// Suporta: PDF, Excel, imagens (JPEG, PNG, TIFF)
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadDocument(
        [FromForm] DocumentUploadForm form,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = form.File;
            _logger.LogInformation("Recebido upload de arquivo: {FileName}", file.FileName);

            // Validate file presence
            if (file == null || file.Length == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Nenhum arquivo foi enviado",
                    errors = new[] { "O arquivo é obrigatório" }
                });
            }

            using var fileStream = file.OpenReadStream();

            var tenantId = ResolveTenantId(form.TenantId, Request.Headers);

            if (!TryNormalizeUploadSource(form.Source, out var uploadSource, out var sourceError))
            {
                return BadRequest(new
                {
                    success = false,
                    message = sourceError,
                    errors = new[] { sourceError }
                });
            }

            // Process document through orchestrator
            var result = await _orchestrator.OrchestrateIngestionAsync(
                fileStream,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                file.Length,
                uploadSource,
                form.Origin,
                tenantId,
                form.Force,
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Ingestão falhou para {FileName}: {Errors}",
                    file.FileName,
                    string.Join("; ", result.Errors));

                return BadRequest(new
                {
                    success = false,
                    message = result.Message,
                    errors = result.Errors
                });
            }

            if (result.IsDeduplicatedUpload && result.DedupEntry is not null)
            {
                var d = result.DedupEntry;
                _logger.LogInformation(
                    "Upload deduplicado para {FileName} → documento existente {DocumentId}",
                    file.FileName,
                    d.DocumentId);

                return Ok(new
                {
                    success = true,
                    deduplicated = true,
                    message = result.Message,
                    documentId = d.DocumentId,
                    tenantId = d.TenantId,
                    documentType = d.DocumentType,
                    extractedItems = d.ExtractedItemCount,
                    publishedRawFinancialEvent = false,
                    publishedDataIngestedEvent = false,
                    processingNote =
                        "Mesmo PDF e tenant que um upload anterior: não republicámos eventos. O documentId é o da primeira ingestão bem-sucedida. Use Force=true no formulário para ingerir de novo como cópia independente."
                });
            }

            _logger.LogInformation("Ingestão concluída com sucesso para {FileName}", file.FileName);

            return Ok(new
            {
                success = true,
                deduplicated = false,
                message = result.Message,
                documentId = result.Document?.Id,
                tenantId = result.Document?.TenantId,
                documentType = result.Document?.DocumentType.ToString(),
                extractedItems = result.Document?.ExtractedLineItems.Count ?? 0,
                publishedRawFinancialEvent = true,
                publishedDataIngestedEvent = result.PublishedDataIngestedEvent,
                processingNote = result.PublishedDataIngestedEvent
                    ? null
                    : "DataIngestedEvent não foi publicado: envie TenantId como GUID válido ou use o gateway com JWT (header " + GatewayForwardedAuthHeaders.TenantId + "). Sem este evento o Processing Service não persiste despesa."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar upload de arquivo");
            return StatusCode(500, new
            {
                success = false,
                errors = new[] { "Erro interno do servidor: " + ex.Message },
                message = "Falha ao processar documento"
            });
        }
    }

    /// <summary>
    /// Normaliza <c>source</c> do upload. Rejeita <c>NFS_SCRAPING</c> (fora de escopo).
    /// Aceita alias TCC <c>MANUAL_UPLOAD</c> → <c>manual</c>.
    /// </summary>
    private static bool TryNormalizeUploadSource(string? raw, out string normalized, out string? error)
    {
        normalized = string.IsNullOrWhiteSpace(raw) ? "manual" : raw.Trim();
        if (normalized.Equals("NFS_SCRAPING", StringComparison.OrdinalIgnoreCase))
        {
            error =
                "Integração com portal NFS municipal não é suportada; envie o arquivo PDF/imagem da nota (NfsScrapingNotSupported).";
            normalized = string.Empty;
            return false;
        }

        if (normalized.Equals("MANUAL_UPLOAD", StringComparison.OrdinalIgnoreCase))
            normalized = "manual";

        error = null;
        return true;
    }

    /// <summary>
    /// GUID do formulário tem prioridade; se inválido ou vazio, usa <see cref="GatewayForwardedAuthHeaders.TenantId"/> (gateway injeta a partir do JWT).
    /// </summary>
    private static string? ResolveTenantId(string? formTenantId, IHeaderDictionary headers)
    {
        var trimmed = formTenantId?.Trim();
        if (!string.IsNullOrEmpty(trimmed)
            && Guid.TryParse(trimmed, out var formGuid)
            && formGuid != Guid.Empty)
            return formGuid.ToString();

        if (!headers.TryGetValue(GatewayForwardedAuthHeaders.TenantId, out var headerVals))
            return null;

        var header = headerVals.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(header))
            return null;

        return Guid.TryParse(header, out var hg) && hg != Guid.Empty ? hg.ToString() : null;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "Ingestion Service",
            version = "2.0",
            features = new[]
            {
                "document_upload",
                "ocr_processing",
                "pdf_parsing",
                "excel_parsing",
                "event_publishing"
            }
        });
    }
}