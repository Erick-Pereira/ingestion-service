using Simcag.IngestionService.Application.DocumentExtraction;

namespace Simcag.IngestionService.Tests.Application;

public sealed class NfeDocumentMetadataExtractorTests
{
    [Fact]
    public void Extract_pichau_nfe_returns_44_digit_access_key()
    {
        var text = File.ReadAllText(Path.Combine("TestData", "pichau_nfe_pdfpig.txt"));

        var meta = NfeDocumentMetadataExtractor.Extract(text);

        Assert.NotNull(meta.AccessKey);
        Assert.Equal(44, meta.AccessKey!.Length);
        Assert.StartsWith("532603", meta.AccessKey);
    }

    [Fact]
    public void Extract_consulta_danfe_returns_access_key_and_fallback()
    {
        var text = File.ReadAllText(Path.Combine("TestData", "consulta_danfe_pdfpig.txt"));

        var meta = NfeDocumentMetadataExtractor.Extract(text);

        Assert.NotNull(meta.AccessKey);
        Assert.Equal(44, meta.AccessKey!.Length);
        Assert.StartsWith("532605", meta.AccessKey);
        Assert.NotNull(meta.FallbackCompositeKey);
        Assert.Contains("12345678000190", meta.FallbackCompositeKey, StringComparison.Ordinal);
    }
}
