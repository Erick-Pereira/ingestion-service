using System;

namespace Simcag.IngestionService.Infrastructure.Messaging
{
    public class RabbitMqOptions
    {
        public string Host { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int Port { get; set; } = 5672;
    }
}