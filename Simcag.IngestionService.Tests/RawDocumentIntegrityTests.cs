using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Tests;

public class RawDocumentIntegrityTests
{
    [Fact]
    public void HasIngestIntegrity_verdadeiro_apos_SetContent()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var hash = FileHash.ComputeSha256(bytes);
        var doc = new RawDocument(
            "id-1",
            "a.pdf",
            ".pdf",
            "application/pdf",
            4,
            hash,
            "manual",
            "",
            DateTime.UtcNow);

        doc.SetContent(bytes);

        Assert.True(doc.HasIngestIntegrity());
        Assert.False(doc.CanPublishRawEvent());
        doc.SetRawText("texto");
        Assert.True(doc.CanPublishRawEvent());
    }
}
