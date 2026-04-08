using Simcag.Shared.Events;

namespace Simcag.IngestionService.Domain.Events;

/// <summary>
/// Published when a new user is created in the system.
/// Subscribers: Email service (send welcome), Analytics service, CRM sync, etc.
/// </summary>
public class PriceCollectedEvent : BaseEvent
{
    // === Domain-Specific Properties ===
    public string Id { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public string Source { get; init; } = string.Empty;

    public string Market { get; init; } = string.Empty;

    // === Override EventType ===
    public override string EventType => "price-collected";

    public PriceCollectedEvent(Guid id, string productId, string productName, decimal price, string source, string market)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id cannot be empty", nameof(id));

        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("ProductId is required", nameof(productId));

        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("ProductName is required", nameof(productName));

        if (price <= 0)
            throw new ArgumentException("Price must be greater than zero", nameof(price));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required", nameof(source));

        // Initialize
        Id = id.ToString();
        ProductName = productName;
        Price = price;
        Source = source;
        Market = market ?? string.Empty;
    }

    public PriceCollectedEvent()
    {
    }
}