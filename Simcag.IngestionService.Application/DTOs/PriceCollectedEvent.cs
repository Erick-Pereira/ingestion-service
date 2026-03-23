// DTOs/PriceCollectedEvent.cs
namespace IngestionService.DTOs
{
    public class PriceCollectedEvent
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public DateTime CollectionDate { get; set; }
    }
}
