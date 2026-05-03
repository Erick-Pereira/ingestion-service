using Microsoft.Extensions.Options;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Infrastructure.Ocr;
using Simcag.IngestionService.Infrastructure.Parser;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using Simcag.Shared.Hosting;
using RabbitMQ.Client;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.IO;

DotNetEnv.Env.Load();
ContainerListenConfiguration.NormalizeAspNetCoreListenUrlsInContainer();

var builder = WebApplication.CreateBuilder(args);
ContainerListenConfiguration.ApplyDockerListenUrls(builder);

var urls = GetListeningUrl();
builder.WebHost.UseUrls(urls);
Console.WriteLine($"🚀 Ingestion Service listening on: {urls}");
Console.WriteLine($"📡 Access via: http://localhost:{ParsePort(urls)} or http://container-host:{ParsePort(urls)}");


builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "SIMC-AG Service", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name        = "Authorization",
        In          = Microsoft.OpenApi.ParameterLocation.Header,
        Type        = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        Description = "Cole apenas o JWT (sem 'Bearer ')."
    });
    c.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddHealthChecks();

// Register Infrastructure services
builder.Services.AddSingleton<IOcrService, TesseractOcrService>();
builder.Services.AddSingleton<IPdfParserService, PdfParserService>();
builder.Services.AddSingleton<IExcelParserService, ExcelParserService>();

// Register Application services
builder.Services.AddSingleton<IIngestionService, IngestionServiceImpl>();
builder.Services.AddSingleton<IProductValidationService, ProductValidationService>();
builder.Services.AddSingleton<IngestionOrchestrator>();

// Register Use Cases
builder.Services.AddSingleton<IIngestDocumentUseCase, IngestDocumentUseCase>();
builder.Services.AddSingleton<IExtractTextUseCase, ExtractTextUseCase>();
builder.Services.AddSingleton<IParseDocumentUseCase, ParseDocumentUseCase>();
builder.Services.AddSingleton<IPublishRawEventUseCase, PublishRawEventUseCase>();

static string? RmqEnv(params string[] keys)
{
    foreach (var key in keys)
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(v))
            return v;
    }
    return null;
}

var rabbitMqHost = RmqEnv("RABBITMQ__HOST", "RABBITMQ_HOST") ?? "localhost";
var rabbitMqPort = int.Parse(RmqEnv("RABBITMQ__PORT", "RABBITMQ_PORT") ?? "5672");
var rabbitMqUserName = RmqEnv("RABBITMQ__USERNAME", "RABBITMQ_USERNAME") ?? "guest";
var rabbitMqPassword = RmqEnv("RABBITMQ__PASSWORD", "RABBITMQ_PASSWORD") ?? "guest";
var rabbitMqVirtualHost = RmqEnv("RABBITMQ__VIRTUALHOST", "RABBITMQ_VIRTUALHOST") ?? "/";

var rabbitMqOptions = new RabbitMqOptions
{
    Host = rabbitMqHost,
    Port = rabbitMqPort,
    UserName = rabbitMqUserName,
    Password = rabbitMqPassword,
    VirtualHost = rabbitMqVirtualHost
};

if (string.IsNullOrEmpty(rabbitMqOptions.Host))
    throw new InvalidOperationException("RabbitMQ host não configurado. Defina RABBITMQ__HOST no .env.");
if (string.IsNullOrEmpty(rabbitMqOptions.UserName))
    throw new InvalidOperationException("RabbitMQ user não configurado. Defina RABBITMQ__USERNAME no .env.");

builder.Services.AddRabbitMqMessaging(rabbitMqOptions);

var eventsExchange = EventBusConstants.GetEventsExchangeName();

// Register RabbitMQ publisher for Simcag.Shared.Events.RawFinancialDataEvent
builder.Services.AddRabbitMqEventPublisher<Simcag.Shared.Events.RawFinancialDataEvent>(eventsExchange);

builder.Services.AddRabbitMqEventPublisher<Simcag.Shared.Events.PriceCollectedEvent>(eventsExchange);

builder.Services.AddLogging(config => config.SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

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
    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "app_port"), port.ToString()); } catch { }
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
