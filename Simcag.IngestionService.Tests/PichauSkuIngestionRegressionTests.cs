using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.Events;
using Xunit;

namespace Simcag.IngestionService.Tests;

/// <summary>Regressão: SKUs Pichau (PG-*) preservados desde parser até DataIngestedEvent.</summary>
public sealed class PichauSkuIngestionRegressionTests
{
    private readonly ParseDocumentUseCase _parser = ParseDocumentTestFactory.CreateUseCase();

    [Fact]
    public void Execute_gabinete_pdfpig_extrai_item_code_pg_vy1_bk()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "pichau_nfe_pdfpig.txt");
        Assert.True(File.Exists(goldenPath), $"Golden ausente: {goldenPath}");

        var result = _parser.Execute(File.ReadAllText(goldenPath), DocumentType.NotaFiscal);

        Assert.Single(result.LineItems);
        Assert.Equal("PG-VY1-BK", result.LineItems[0].ItemCode);
        Assert.Contains("PG-VY1-BK", result.LineItems[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_gabinete_espacado_extrai_item_code_pg_vy1_bk()
    {
        var result = _parser.Execute(
            TabularProductTableTestSamples.RetailDevolutionSpacedWithNatureza572,
            DocumentType.NotaFiscal);

        Assert.Single(result.LineItems);
        Assert.Equal("PG-VY1-BK", result.LineItems[0].ItemCode);
    }

    [Fact]
    public void Execute_webcam_com_sku_na_descricao_extrai_pg_indus_bl01()
    {
        const string raw = """
            DANFE
            DADOS DO PRODUTO/SERVIÇO
            45123 Webcam Pichau Indus, 2K, USB, Preto, PG-INDUS-BL01
            85258949 0 00 1202 UN 1 260,86 260,86
            VALOR TOTAL DA NOTA 260,86
            """;

        var result = _parser.Execute(raw, DocumentType.NotaFiscal);

        Assert.Single(result.LineItems);
        Assert.Equal("PG-INDUS-BL01", result.LineItems[0].ItemCode);
        Assert.Contains("PG-INDUS-BL01", result.LineItems[0].Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishRawEvent_propaga_item_code_nas_linhas_canonicas()
    {
        var document = PublishRawEventTestFactory.BuildDocument([
            new ExtractedLineItem(
                lineNumber: 1,
                amount: new Money(572.76m, "BRL"),
                date: null,
                description: "Gabinete Gamer Pichau Voyager One PG-VY1-BK",
                rawLine: "45074 Gabinete ... PG-VY1-BK",
                confidenceScore: 88,
                quantity: 1m,
                unitPrice: 572.76m,
                itemCode: "PG-VY1-BK"),
        ]);

        var useCase = PublishRawEventTestFactory.CreateUseCase();
        var outcome = useCase.PublishAsync(document).GetAwaiter().GetResult();

        Assert.True(outcome.DataIngestedEventPublished);

        var captured = PublishRawEventTestFactory.LastDataIngestedEvent;
        Assert.NotNull(captured);
        Assert.NotNull(captured!.ExtractedFields.Lines);
        Assert.Single(captured.ExtractedFields.Lines);
        Assert.Equal("PG-VY1-BK", captured.ExtractedFields.Lines[0].ItemCode);

        Assert.True(captured.ExtractedFields.Extra.TryGetValue("ingestedLinesJson", out var jsonObj));
        var json = jsonObj as string ?? jsonObj?.ToString() ?? string.Empty;
        Assert.Contains("PG-VY1-BK", json, StringComparison.OrdinalIgnoreCase);
    }
}
