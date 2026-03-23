using IngestionService.DTOs;
using IngestionService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading.Tasks;

namespace IngestionService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IngestionController : ControllerBase
    {
        private readonly IRabbitMqPublisher _rabbitMqPublisher;
        private readonly ILogger<IngestionController> _logger;

        public IngestionController(IRabbitMqPublisher rabbitMqPublisher, ILogger<IngestionController> logger)
        {
            _rabbitMqPublisher = rabbitMqPublisher;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] PriceCollectedEvent priceCollectedEvent)
        {
            try
            {
                string message = JsonSerializer.Serialize(priceCollectedEvent);
                await _rabbitMqPublisher.PublishAsync(message, "price-events");
                return Accepted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message to RabbitMQ.");
                return StatusCode(500, "Internal Server Error");
            }
        }
    }
}