using Simcag.Shared.Events;

namespace Simcag.IngestionService.Application.Services;

public class ProductValidationService : IProductValidationService
{
    public ProductValidationService()
    {
    }

    public ValidationResult ValidatePriceCollectedEvent(PriceCollectedEvent @event)
    {
        var errors = new List<string>();

        if (@event == null)
        {
            errors.Add("O evento não pode ser nulo");
            return new ValidationResult { IsValid = false, Errors = errors.ToArray() };
        }

        if (string.IsNullOrWhiteSpace(@event.ProductName))
        {
            errors.Add("O ProductName é obrigatório e deve ter entre 1 e 100 caracteres");
        }
        else if (@event.ProductName.Length > 100)
        {
            errors.Add("O ProductName não pode ter mais de 100 caracteres");
        }

        if (string.IsNullOrWhiteSpace(@event.Source))
        {
            errors.Add("O Source é obrigatório e deve ter entre 1 e 50 caracteres");
        }
        else if (@event.Source.Length > 50)
        {
            errors.Add("O Source não pode ter mais de 50 caracteres");
        }

        if (@event.Price <= 0)
        {
            errors.Add("O preço deve ser maior que zero");
        }
        else if (@event.Price > 999999.99m)
        {
            errors.Add("O preço não pode ser maior que 999.999,99");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        };
    }
}