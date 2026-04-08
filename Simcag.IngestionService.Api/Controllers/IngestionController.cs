using Simcag.IngestionService.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Simcag.Shared.Messaging.Contracts;
using Simcag.IngestionService.Domain.Events;

namespace Simcag.IngestionService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IngestionController : ControllerBase
    {
        private readonly IIngestionService _ingestionService;
        private readonly ILogger<IngestionController> _logger;

        public IngestionController(
            IIngestionService ingestionService,
            ILogger<IngestionController> logger)
        {
            _ingestionService = ingestionService;
            _logger = logger;
        }

        [HttpPost]
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
    }
}