// Infrastructure/IRabbitMqPublisher.cs
using System.Threading.Tasks;

namespace IngestionService.Infrastructure
{
    public interface IRabbitMqPublisher
    {
        Task PublishAsync<T>(string queueName, T message);
    }
}