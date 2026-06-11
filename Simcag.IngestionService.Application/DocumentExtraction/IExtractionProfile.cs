using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.DocumentExtraction;

/// <summary>Estratégia de extração estrutural por layout textual (não por fornecedor ou tipo fiscal).</summary>
public interface IExtractionProfile
{
    /// <summary>Identificador estável (ex.: br.tabular_product_table.v1).</summary>
    string ProfileId { get; }

    /// <summary>Confiança de que este perfil se aplica (0–100).</summary>
    int Score(ExtractionContext context);

    /// <summary>Mínimo de linhas para aceitar o resultado deste perfil (0 = fallback).</summary>
    int MinimumItems { get; }

    IReadOnlyList<ExtractedLineItem> Extract(ExtractionContext context);
}
