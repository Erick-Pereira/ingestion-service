using System.Text.RegularExpressions;
using Simcag.IngestionService.Application.DocumentExtraction.Parsing;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Tests;

public class DocumentParsersTripleValueTests
{
    [Fact]
    public void TryExtractDanfeGluedRetailTripleValueRows_webcam_golden()
    {
        var raw = TabularProductTableTestSamples.RetailSingleLineGluedUn1TripleValue299;
        var flat = raw.Replace('\r', ' ').Replace('\n', ' ');

        var idx = flat.IndexOf("34459Webcam", StringComparison.OrdinalIgnoreCase);
        Assert.True(idx >= 0, $"34459Webcam not found in golden (len={flat.Length})");

        var section = DocumentParsers.TrySliceDanfeProductsSection(flat);
        Assert.False(string.IsNullOrWhiteSpace(section));

        var rowsFromSection = DocumentParsers.TryExtractDanfeGluedRetailTripleValueRows(
            Regex.Replace(section!, @"\s+", " ").Trim()).ToList();
        Assert.NotEmpty(rowsFromSection);

        var rows = DocumentParsers.TryExtractDanfeGluedRetailTripleValueRows(flat).ToList();
        Assert.NotEmpty(rows);

        var row = rows[0];
        Assert.Contains("Webcam Pichau", row.description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(260.86m, row.total);
        Assert.Equal(1m, row.quantity);
        Assert.Equal(260.86m, row.unitPrice);
    }

    [Fact]
    public void Execute_webcam_golden_full_pipeline()
    {
        var sut = ParseDocumentTestFactory.CreateUseCase();
        var result = sut.Execute(TabularProductTableTestSamples.RetailSingleLineGluedUn1TripleValue299, DocumentType.NotaFiscal);

        Assert.Single(result.LineItems);
        Assert.Contains("Webcam Pichau", result.LineItems[0].Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(260.86m, result.LineItems[0].Amount!.Amount);
    }
}
