namespace Simcag.IngestionService.Application.Services
{
    using global::IngestionService.Infrastructure;
    using shared.Events;

    public class IngestionService
    {
        private readonly IRabbitMqPublisher _publisher;

        public IngestionService(IRabbitMqPublisher publisher)
        {
            _publisher = publisher;
        }

        public async Task IngestAsync()
        {
            var evt = new DataIngestedEvent
            {
                Id = Guid.NewGuid(),
                ProductName = "Notebook Dell",
                Price = 3500,
                Timestamp = DateTime.UtcNow
            };

            await _publisher.PublishAsync("price-events", evt);
        }
    }
}