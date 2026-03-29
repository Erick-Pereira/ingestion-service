using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using shared.Events;
using Simcag.IngestionService.Infrastructure.Messaging;

namespace Simcag.IngestionService.Application.Services
{
    public class IngestionServiceImpl : IIngestionService
    {
        private readonly IRabbitMqPublisher _publisher;
        private readonly IProductValidationService _validationService;
        private readonly ILogger<IngestionServiceImpl> _logger;

        public IngestionServiceImpl(
            IRabbitMqPublisher publisher,
            IProductValidationService validationService,
            ILogger<IngestionServiceImpl> logger)
        {
            _publisher = publisher;
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<IngestionResult> ProcessPriceCollectedEventAsync(PriceCollectedEvent @event)
        {
            try
            {
                _logger.LogInformation("Iniciando processamento do evento de preço para produto {ProductId}", @event.ProductId);

                var validationResult = _validationService.ValidatePriceCollectedEvent(@event);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Validação falhou para evento {ProductId}. Erros: {Errors}", @event.ProductId, string.Join(", ", validationResult.Errors));
                    return new IngestionResult
                    {
                        Success = false,
                        Errors = validationResult.Errors,
                        Message = "Validação falhou"
                    };
                }

                await _publisher.PublishAsync("price-events", @event);

                _logger.LogInformation("Evento de preço publicado com sucesso para produto {ProductId}", @event.ProductId);

                return new IngestionResult
                {
                    Success = true,
                    Errors = Array.Empty<string>(),
                    Message = "Evento processado com sucesso"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar evento de preço para produto {ProductId}", @event.ProductId);
                return new IngestionResult
                {
                    Success = false,
                    Errors = new[] { "Erro interno ao processar evento" },
                    Message = "Erro interno"
                };
            }
        }
    }
}