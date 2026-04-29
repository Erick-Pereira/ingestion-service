namespace Simcag.IngestionService.Domain.ValueObjects;

public class Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "BRL")
    {
        if (amount < 0)
            throw new ArgumentException("Valor não pode ser negativo", nameof(amount));
        
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Moeda é obrigatória", nameof(currency));

        Amount = amount;
        Currency = currency.ToUpper();
    }

    public override string ToString() => $"{Amount:C} ({Currency})";

    public bool IsValid() => Amount >= 0;
}