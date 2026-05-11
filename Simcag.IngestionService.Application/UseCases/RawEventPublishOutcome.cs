namespace Simcag.IngestionService.Application.UseCases;

/// <summary>Resultado da publicação RabbitMQ após ingestão (legado + canónico).</summary>
public sealed record RawEventPublishOutcome(bool DataIngestedEventPublished);
