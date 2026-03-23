// Program.cs
using IngestionService.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure RabbitMQ publisher as a singleton
builder.Services.AddSingleton<IRabbitMqPublisher>(sp =>
{
    var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"];
    var logger = sp.GetRequiredService<ILogger<RabbitMqPublisher>>();
    return new RabbitMqPublisher(rabbitMqHostName, logger);
});

// Add logging
builder.Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();