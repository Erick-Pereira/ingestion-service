using Simcag.IngestionService.Application.Services;
using Microsoft.AspNetCore.Mvc;
using shared.Events;

namespace Simcag.IngestionService.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IngestionController : ControllerBase
    {
        private readonly IIngestionService _ingestionService;
        private readonly ILogger<IngestionController> _logger;

        public IngestionController(IIngestionService ingestionService, ILogger<IngestionController> logger)
        {
            _ingestionService = ingestionService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] PriceCollectedEvent priceCollectedEvent)
        {
            try
            {
                var result = await _ingestionService.ProcessPriceCollectedEventAsync(priceCollectedEvent);

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
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao processar evento de preço");
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