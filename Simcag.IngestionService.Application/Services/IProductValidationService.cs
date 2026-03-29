using shared.Contracts;

namespace Simcag.IngestionService.Application.Services
{
    public interface IProductValidationService
    {
        ValidationResult ValidatePriceCollectedEvent(PriceCollectedEvent @event);
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public required string[] Errors { get; set; }
    }
}