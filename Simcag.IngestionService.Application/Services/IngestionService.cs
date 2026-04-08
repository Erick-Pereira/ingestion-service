using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Events;
using Simcag.Shared.Messaging.Contracts;

namespace Simcag.IngestionService.Application.Services
{
    public class IngestionServiceImpl : IIngestionService
    {
        private readonly IEventPublisher<PriceCollectedEvent> _publisher;
        private readonly IProductValidationService _validationService;
        private readonly ILogger<IngestionServiceImpl> _logger;

        public IngestionServiceImpl(
            IProductValidationService validationService,
            IEventPublisher<PriceCollectedEvent> publisher,
            ILogger<IngestionServiceImpl> logger)
        {
            _publisher = publisher;
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<IngestionResult> ProcessPriceCollectedEventAsync(PriceCollectedEvent @event, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Iniciando processamento do evento de preço para produto {ProductId} em {Timestamp}", @event.Id, DateTime.UtcNow);

                var validationResult = _validationService.ValidatePriceCollectedEvent(@event);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Validação falhou para evento {Id}. Erros: {Errors}", @event.Id, string.Join(", ", validationResult.Errors));
                    return new IngestionResult
                    {
                        Success = false,
                        Errors = validationResult.Errors,
                        Message = "Validação falhou"
                    };
                }

                _logger.LogInformation("Publicando evento {EventId} em {Timestamp}", @event.Id, DateTime.UtcNow);
                await _publisher.PublishAsync(@event, cancellationToken);
                _logger.LogInformation("Evento de preço publicado com sucesso para produto {Id} em {Timestamp}", @event.Id, DateTime.UtcNow);

                return new IngestionResult
                {
                    Success = true,
                    Errors = Array.Empty<string>(),
                    Message = "Evento processado com sucesso"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar evento de preço para produto {Id}", @event.Id);
                return new IngestionResult
                {
                    Success = false,
                    Errors = new[] { "Erro interno ao processar evento" },
                    Message = ex.Message
                };
            }
        }
    }
}