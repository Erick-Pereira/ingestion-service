using Microsoft.Extensions.Logging;
using Simcag.Shared.Events;
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
                _logger.LogInformation("Iniciando processamento do evento de preço para produto {ProductId} (EventId={EventId}) em {Timestamp}", @event.ProductId, @event.EventId, DateTime.UtcNow);

                var validationResult = _validationService.ValidatePriceCollectedEvent(@event);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Validação falhou para evento {EventId}. Erros: {Errors}", @event.EventId, string.Join(", ", validationResult.Errors));
                    return new IngestionResult
                    {
                        Success = false,
                        Errors = validationResult.Errors,
                        Message = "Validação falhou"
                    };
                }

                var toPublish = EnsureEventId(@event);
                _logger.LogInformation("Publicando evento {EventId} em {Timestamp}", toPublish.EventId, DateTime.UtcNow);
                await _publisher.PublishAsync(toPublish, cancellationToken);
                _logger.LogInformation("Evento de preço publicado com sucesso para EventId {EventId} em {Timestamp}", toPublish.EventId, DateTime.UtcNow);

                return new IngestionResult
                {
                    Success = true,
                    Errors = Array.Empty<string>(),
                    Message = "Evento processado com sucesso"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar evento de preço (EventId={EventId}, ProductId={ProductId})", @event.EventId, @event.ProductId);
                return new IngestionResult
                {
                    Success = false,
                    Errors = new[] { "Erro interno ao processar evento" },
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Deserialização JSON comum: EventId fica vazio; gera um GUID antes de publicar.
        /// </summary>
        private static PriceCollectedEvent EnsureEventId(PriceCollectedEvent @event)
        {
            if (@event.EventId != Guid.Empty)
                return @event;

            return new PriceCollectedEvent
            {
                EventId = Guid.NewGuid(),
                CreatedAt = @event.CreatedAt,
                ProductId = @event.ProductId,
                ProductName = @event.ProductName,
                Price = @event.Price,
                Source = @event.Source,
                Market = @event.Market,
                OccurredAt = @event.OccurredAt,
                Timestamp = @event.Timestamp,
                Data = @event.Data
            };
        }
    }
}