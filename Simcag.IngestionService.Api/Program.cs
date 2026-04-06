using Simcag.IngestionService.Application.Services;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using Simcag.IngestionService.Domain.Events;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// 🔥 Bind para Docker (ESSENCIAL)
builder.WebHost.UseUrls("http://0.0.0.0:8080");

// Controllers
builder.Services.AddControllers();

// ✅ Swagger (faltava isso)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Serviço de aplicação
builder.Services.AddSingleton<IIngestionService, IngestionServiceImpl>();
builder.Services.AddSingleton<IProductValidationService, ProductValidationService>();

// ✅ Mensageria via ENV
var rabbitMqOptions = new RabbitMqOptions
{
    Host = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? throw new InvalidOperationException("RABBITMQ__HOST not set"),
    Port = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ__PORT") ?? "5672"),
    UserName = Environment.GetEnvironmentVariable("RABBITMQ__USERNAME") ?? throw new InvalidOperationException("RABBITMQ__USERNAME not set"),
    Password = Environment.GetEnvironmentVariable("RABBITMQ__PASSWORD") ?? throw new InvalidOperationException("RABBITMQ__PASSWORD not set"),
    VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ__VIRTUALHOST") ?? "/"
};

builder.Services.AddRabbitMqMessaging(rabbitMqOptions, "simcag-events");
builder.Services.AddRabbitMqEventPublisher<PriceCollectedEvent>();
builder.Services.AddRabbitMqQueueDeclaration<PriceCollectedEvent>("price-collected");

// Logging
builder.Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// app.UseHttpsRedirection();

// Swagger sempre ativo (pra debug)
app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();

app.Run();