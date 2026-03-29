using Simcag.IngestionService.Infrastructure.Messaging;
using Simcag.IngestionService.Application.Services;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// 🔥 Bind para Docker (ESSENCIAL)
builder.WebHost.UseUrls("http://localhost:8080");

// Controllers
builder.Services.AddControllers();

// ✅ Swagger (faltava isso)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Validação
builder.Services.AddSingleton<IProductValidationService, ProductValidationService>();

// ✅ Serviço de aplicação
builder.Services.AddSingleton<IIngestionService, IngestionServiceImpl>();

// ✅ RabbitMQ via ENV VAR
builder.Services.AddSingleton<IRabbitMqPublisher>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var host = configuration["RabbitMQ:Host"];
    var userName = configuration["RabbitMQ:UserName"];
    var password = configuration["RabbitMQ:Password"];
    var port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672");
    var logger = sp.GetRequiredService<ILogger<RabbitMqPublisher>>();

    Console.WriteLine($"HOST: {host}");
    Console.WriteLine($"USER: {userName}");
    Console.WriteLine($"PASS: {password}");
    Console.WriteLine($"PORT: {port}");
    return new RabbitMqPublisher(host, userName, password, port, logger);
});

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