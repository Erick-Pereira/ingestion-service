namespace Simcag.IngestionService.Application.Configuration;

/// <summary>
/// Controlo gradual da migração ADR-0001 (publicação legada vs canónica).
/// </summary>
public sealed class IngestionEventPublishingOptions
{
    public const string SectionKey = "Ingestion";

    /// <summary>
    /// Quando <c>true</c> (padrão), publica <c>RawFinancialDataEvent</c> para o AI Service (fila legada).
    /// Quando <c>false</c>, só publica <c>DataIngestedEvent</c> — exige que o AI Service consuma o canónico (migração em curso).
    /// </summary>
    public bool PublishLegacyRawFinancialEvent { get; set; } = false;
}
