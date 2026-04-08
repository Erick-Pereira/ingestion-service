using Microsoft.Extensions.Options;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.Events;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using RabbitMQ.Client;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

//builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IIngestionService, IngestionServiceImpl>();
builder.Services.AddSingleton<IProductValidationService, ProductValidationService>();

var rabbitMqHost = Environment.GetEnvironmentVariable("RABBITMQ__HOST") ?? builder.Configuration["RabbitMq:Host"] ?? "localhost";
var rabbitMqPort = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ__PORT") ?? builder.Configuration["RabbitMq:Port"] ?? "5672");
var rabbitMqUserName = Environment.GetEnvironmentVariable("RABBITMQ__USERNAME") ?? builder.Configuration["RabbitMq:UserName"] ?? "guest";
var rabbitMqPassword = Environment.GetEnvironmentVariable("RABBITMQ__PASSWORD") ?? builder.Configuration["RabbitMq:Password"] ?? "guest";
var rabbitMqVirtualHost = Environment.GetEnvironmentVariable("RABBITMQ__VIRTUALHOST") ?? builder.Configuration["RabbitMq:VirtualHost"] ?? "/";

var rabbitMqOptions = new RabbitMqOptions
{
    Host = rabbitMqHost,
    Port = rabbitMqPort,
    UserName = rabbitMqUserName,
    Password = rabbitMqPassword,
    VirtualHost = rabbitMqVirtualHost
};

if (string.IsNullOrEmpty(rabbitMqOptions.Host))
    throw new InvalidOperationException("RabbitMq:Host is not configured. Check appsettings.json or environment variables.");
if (string.IsNullOrEmpty(rabbitMqOptions.UserName))
    throw new InvalidOperationException("RabbitMq:UserName is not configured. Check appsettings.json or environment variables.");

builder.Services.AddRabbitMqMessaging(rabbitMqOptions);

builder.Services.AddRabbitMqEventPublisher<PriceCollectedEvent>("simcag-events");

builder.Services.AddLogging(config => config.SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();

app.Run();