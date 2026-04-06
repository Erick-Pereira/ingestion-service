using shared.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simcag.IngestionService.Domain.Events
{
    public class PriceCollectedEvent : BaseEvent
    {
        public string ProductId { get; init; } = string.Empty;

        public string ProductName { get; init; } = string.Empty;

        public decimal Price { get; init; }

        public string Source { get; init; } = string.Empty;

        public string Market { get; init; } = string.Empty;

        public override string EventType => "price-collected";

        public PriceCollectedEvent()
        {
        }

        public PriceCollectedEvent(string productId, string productName, string source)
        {
            ProductId = productId;
            ProductName = productName;
            Source = source;
            Market = source;
        }
    }
}