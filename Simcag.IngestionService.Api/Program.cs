using Microsoft.Extensions.Options;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Domain.Events;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using RabbitMQ.Client;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

var urls = GetListeningUrl();
builder.WebHost.UseUrls(urls);
Console.WriteLine($"Listening on {urls}");

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

static string GetListeningUrl()
{
    const int defaultPort = 8080;

    var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var envPort = Environment.GetEnvironmentVariable("PORT");
    var requestedUrl = !string.IsNullOrWhiteSpace(envUrls)
        ? envUrls
        : !string.IsNullOrWhiteSpace(envPort)
            ? $"http://0.0.0.0:{envPort}"
            : $"http://0.0.0.0:{defaultPort}";

    var requestedPort = ParsePort(requestedUrl) ?? defaultPort;
    var port = FindAvailablePort(requestedPort);
    return $"http://0.0.0.0:{port}";
}

static int? ParsePort(string url)
{
    var match = Regex.Match(url, @":(\d+)");
    return match.Success && int.TryParse(match.Groups[1].Value, out var port)
        ? port
        : null;
}

static int FindAvailablePort(int startPort)
{
    for (var port = startPort; port < startPort + 50; port++)
    {
        if (IsPortAvailable(port))
            return port;
    }

    return startPort;
}

static bool IsPortAvailable(int port)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        return true;
    }
    catch
    {
        return false;
    }
}