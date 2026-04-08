using Simcag.IngestionService.Domain.Events;

namespace Simcag.IngestionService.Application.Services
{
    public interface IProductValidationService
    {
        ValidationResult ValidatePriceCollectedEvent(PriceCollectedEvent @event);
    }
}