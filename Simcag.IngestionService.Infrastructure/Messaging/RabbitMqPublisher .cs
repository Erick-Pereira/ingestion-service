using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Infrastructure.Messaging
{
    public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly ILogger<RabbitMqPublisher> _logger;

public RabbitMqPublisher(string hostName, string userName, string password, int port, ILogger<RabbitMqPublisher> logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    try
    {
        var factory = new ConnectionFactory { HostName = hostName, UserName = userName, Password = password, Port = port };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare the queue only once
        _channel.QueueDeclare(queue: "price-events", durable: false, exclusive: false, autoDelete: false, arguments: null);

        _logger.LogInformation("RabbitMQ connection established.");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to establish RabbitMQ connection.");
        throw;
    }
}
        public async Task PublishAsync<T>(string queueName, T message)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
                _channel.BasicPublish(exchange: "",
                                         routingKey: queueName,
                                         basicProperties: null,
                                         body: body);

                _logger.LogInformation("Message published to RabbitMQ.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message to RabbitMQ.");
                throw;
            }
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
}