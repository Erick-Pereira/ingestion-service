using Simcag.Shared.Events;

namespace Simcag.IngestionService.Application.Services;

public class ProductValidationService : IProductValidationService
{
    public ValidationResult ValidateDataIngestedEvent(DataIngestedEvent @event)
    {
        var errors = new List<string>();

        if (@event == null)
        {
            errors.Add("O evento não pode ser nulo");
            return new ValidationResult { IsValid = false, Errors = errors.ToArray() };
        }

        if (@event.DocumentId == Guid.Empty)
            errors.Add("O DocumentId é obrigatório");

        if (@event.TenantId == Guid.Empty)
            errors.Add("O TenantId é obrigatório");

        if (string.IsNullOrWhiteSpace(@event.FileHash))
            errors.Add("O FileHash é obrigatório");

        if (string.IsNullOrWhiteSpace(@event.Source))
            errors.Add("O Source é obrigatório e deve ter entre 1 e 50 caracteres");
        else if (@event.Source.Length > 50)
            errors.Add("O Source não pode ter mais de 50 caracteres");

        if (@event.ExtractedFields.Amount is <= 0m or > 999999.99m)
            errors.Add("O Amount extraído deve estar entre 0,01 e 999.999,99 quando informado");

        if (@event.ExtractedFields.Lines is { Count: > 0 } lines)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Amount <= 0)
                    errors.Add($"A linha {i + 1} deve ter valor maior que zero");
                else if (line.Amount > 999999.99m)
                    errors.Add($"A linha {i + 1} não pode ter valor maior que 999.999,99");
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        };
    }
}
