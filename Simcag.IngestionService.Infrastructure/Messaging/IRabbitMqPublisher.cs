using System.Threading.Tasks;

namespace Simcag.IngestionService.Infrastructure.Messaging
{
    public interface IRabbitMqPublisher
    {
        Task PublishAsync<T>(string queueName, T message);
    }
}
