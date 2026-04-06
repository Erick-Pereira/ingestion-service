using shared.Events;

namespace Simcag.IngestionService.Application.Services;

public class ProductValidationService : IProductValidationService
{
    public ValidationResult ValidatePriceCollectedEvent(PriceCollectedEvent @event)
    {
        var errors = new List<string>();

        if (@event == null)
        {
            errors.Add("O evento não pode ser nulo");
            return new ValidationResult { IsValid = false, Errors = errors.ToArray() };
        }

        if (string.IsNullOrWhiteSpace(@event.ProductId))
        {
            errors.Add("O ProductId é obrigatório");
        }

        if (string.IsNullOrWhiteSpace(@event.ProductName))
        {
            errors.Add("O ProductName é obrigatório");
        }

        if (string.IsNullOrWhiteSpace(@event.Source))
        {
            errors.Add("O Source é obrigatório");
        }

        if (@event.Price <= 0)
        {
            errors.Add("O preço deve ser maior que zero");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        };
    }
}