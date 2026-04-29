using Microsoft.AspNetCore.Mvc;
using Simcag.IngestionService.Api.Contracts;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.Enums;
using Simcag.Shared.Events;

namespace Simcag.IngestionService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("ingestion")]
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

            // Process document through orchestrator
            var result = await _orchestrator.OrchestrateIngestionAsync(
                fileStream,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                file.Length,
                form.Source,
                form.Origin,
                form.TenantId,
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

            _logger.LogInformation("Ingestão concluída com sucesso para {FileName}", file.FileName);

            return Ok(new
            {
                success = true,
                message = result.Message,
                documentId = result.Document?.Id,
                documentType = result.Document?.DocumentType.ToString(),
                extractedItems = result.Document?.ExtractedLineItems.Count ?? 0,
                published = true
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
    /// Endpoint legacy para processamento de PriceCollectedEvent (manter compatibilidade)
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Post(
        [FromBody] PriceCollectedEvent priceCollectedEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "📤 Processing PriceCollectedEvent for product {ProductName}",
                priceCollectedEvent.ProductName);

            var result = await _ingestionService.ProcessPriceCollectedEventAsync(
                priceCollectedEvent,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    errors = result.Errors,
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = true,
                message = result.Message,
                published = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao publicar ou processar evento de preço");
            return StatusCode(500, new
            {
                success = false,
                errors = new[] { "Erro interno do servidor" },
                message = "Erro interno"
            });
        }
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