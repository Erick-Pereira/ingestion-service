using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simcag.IngestionService.Application.Configuration;
using Simcag.IngestionService.Application.Services;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Infrastructure.Dedup;
using Simcag.IngestionService.Infrastructure.Ocr;
using Simcag.IngestionService.Infrastructure.Parser;
using Simcag.Shared.Events;
using Simcag.Shared.Messaging;
using Simcag.Shared.Messaging.Configuration;
using Simcag.Shared.Messaging.Extensions;
using Simcag.Shared.ErrorHandling;
using Simcag.Shared.Hosting;
using Simcag.Shared.Security;
using Simcag.Shared.Telemetry;
using RabbitMQ.Client;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.IO;
using StackExchange.Redis;

DotNetEnv.Env.NoClobber().Load();
ContainerListenConfiguration.NormalizeAspNetCoreListenUrlsInContainer();

var builder = WebApplication.CreateBuilder(args);
builder.AddSimcagDistributedTelemetry("Simcag.IngestionService");
ContainerListenConfiguration.ApplyDockerListenUrls(builder);
var isTesting = builder.Environment.IsEnvironment("Testing");

builder.Services.Configure<IngestionEventPublishingOptions>(
    builder.Configuration.GetSection(IngestionEventPublishingOptions.SectionKey));

var urls = GetListeningUrl();
builder.WebHost.UseUrls(urls);
Console.WriteLine($"Ingestion Service listening on: {urls}");

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
builder.Services.AddHealthChecks()
    .AddSimcagLiveSelfCheck();

// Register Infrastructure services
builder.Services.AddSingleton<IOcrService, TesseractOcrService>();
builder.Services.AddSingleton<IPdfParserService, PdfParserService>();
builder.Services.AddSingleton<IExcelParserService, ExcelParserService>();

// Register Application services
builder.Services.AddMemoryCache();

static string? RedisDedupConnection()
{
    foreach (var key in new[] { "REDIS__CONNECTION", "REDIS_CONNECTION" })
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(v))
            continue;
        var t = v.Trim();
        if (string.Equals(t, "memory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "inmemory", StringComparison.OrdinalIgnoreCase))
            return null;
        return t;
    }
    return null;
}

var redisDedupConn = RedisDedupConnection();
if (redisDedupConn is not null)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisDedupConn));
    builder.Services.AddSingleton<IIngestionUploadDedupStore, IngestionUploadDedupRedisStore>();
}
else
{
    builder.Services.AddSingleton<IIngestionUploadDedupStore, IngestionUploadDedupMemoryStore>();
}

builder.Services.AddScoped<IIngestionService, IngestionServiceImpl>();
builder.Services.AddScoped<IProductValidationService, ProductValidationService>();
builder.Services.AddScoped<IngestionOrchestrator>();

// Register Use Cases
builder.Services.AddScoped<IIngestDocumentUseCase, IngestDocumentUseCase>();
builder.Services.AddScoped<IExtractTextUseCase, ExtractTextUseCase>();
builder.Services.AddScoped<IParseDocumentUseCase, ParseDocumentUseCase>();
builder.Services.AddScoped<IPublishRawEventUseCase, PublishRawEventUseCase>();

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
rabbitMqOptions.ApplyMessageSigningFromEnvironment();

if (!isTesting)
{
if (string.IsNullOrEmpty(rabbitMqOptions.Host))
    throw new InvalidOperationException("RabbitMQ host não configurado. Defina RABBITMQ__HOST no .env.");
if (string.IsNullOrEmpty(rabbitMqOptions.UserName))
    throw new InvalidOperationException("RabbitMQ user não configurado. Defina RABBITMQ__USERNAME no .env.");

builder.Services.AddRabbitMqMessaging(rabbitMqOptions);

var eventsExchange = EventBusConstants.GetEventsExchangeName();

// Register RabbitMQ publisher for Simcag.Shared.Events.RawFinancialDataEvent
builder.Services.AddRabbitMqEventPublisher<Simcag.Shared.Events.RawFinancialDataEvent>(eventsExchange);
builder.Services.AddRabbitMqEventPublisher<Simcag.Shared.Events.DataIngestedEvent>(eventsExchange);

builder.Services.AddRabbitMqEventPublisher<Simcag.Shared.Events.PriceCollectedEvent>(eventsExchange);
}

builder.Services.AddLogging(config => config.SetMinimumLevel(LogLevel.Information));

builder.Services.AddSimcagGatewayAuthentication(builder.Environment);

builder.Services.AddSimcagProblemDetails();

var app = builder.Build();

app.ValidateSimcagGatewayTrustAtStartup();

app.UseSimcagExceptionHandler();
app.UseSimcagHttpCorrelationActivityTags();

// app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapSimcagHealthChecks();

app.UseSimcagTelemetryEndpoints();

app.Run();

static string GetListeningUrl()
{
    const int defaultPort = 8080;

    // Em Docker não usar FindAvailablePort: o probe TcpListener pode falhar em portas já “reservadas”
    // pelo runtime e o HEALTHCHECK usa ASPNETCORE_HTTP_PORTS — desalinhamento → unhealthy (ex.: curl :5002).
    if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
    {
        var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(envUrls))
        {
            var first = envUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            if (Uri.TryCreate(first, UriKind.Absolute, out var u) && u.Port > 0)
                return $"http://+:{u.Port}";
        }

        var httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        if (!string.IsNullOrWhiteSpace(httpPorts))
        {
            var token = httpPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            if (int.TryParse(token, out var p) && p > 0)
                return $"http://+:{p}";
        }

        return $"http://+:{defaultPort}";
    }

    var envUrlsLocal = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    var envPort = Environment.GetEnvironmentVariable("PORT");
    var requestedUrl = !string.IsNullOrWhiteSpace(envUrlsLocal)
        ? envUrlsLocal
        : !string.IsNullOrWhiteSpace(envPort)
            ? $"http://0.0.0.0:{envPort}"
            : $"http://0.0.0.0:{defaultPort}";

    var requestedPort = ParsePort(requestedUrl) ?? defaultPort;
    var port = FindAvailablePort(requestedPort);
    try
    {
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "app_port"), port.ToString());
    }
    catch (Exception)
    {
        // Best-effort port file for local dev tooling.
    }
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

public partial class Program
{
}
