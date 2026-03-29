using System;
using System.Collections.Generic;
using System.Linq;
using shared.Contracts;

namespace Simcag.IngestionService.Application.Services
{
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

            if (string.IsNullOrWhiteSpace(@event.ProductName))
                errors.Add("O nome do produto é obrigatório");

            if (@event.Price <= 0)
                errors.Add("O preço deve ser maior que zero");

            if (@event.CollectionDate == DateTime.MinValue)
                errors.Add("A data de coleta é obrigatória");

            if (string.IsNullOrWhiteSpace(@event.Source))
                errors.Add("A fonte do dado é obrigatória");

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors.ToArray()
            };
        }
    }
}