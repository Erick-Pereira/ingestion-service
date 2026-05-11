using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.Services;

public class IngestionOrchestratorResult
{
    public bool IsSuccess { get; private set; }
    public RawDocument? Document { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string[] Errors { get; private set; } = Array.Empty<string>();

    /// <summary><see cref="Simcag.Shared.Events.DataIngestedEvent"/> — necessário para o Processing persistir despesa / auditoria.</summary>
    public bool PublishedDataIngestedEvent { get; private set; }

    /// <summary>Segundo (ou posterior) upload do mesmo ficheiro para o mesmo tenant: sem reprocessamento.</summary>
    public bool IsDeduplicatedUpload { get; private set; }

    public IngestionDedupEntry? DedupEntry { get; private set; }

    private IngestionOrchestratorResult() { }

    public static IngestionOrchestratorResult Success(RawDocument document, bool publishedDataIngestedEvent) =>
        new()
        {
            IsSuccess = true,
            Document = document,
            Message = "Ingestão concluída com sucesso",
            PublishedDataIngestedEvent = publishedDataIngestedEvent
        };

    public static IngestionOrchestratorResult Duplicate(IngestionDedupEntry entry) =>
        new()
        {
            IsSuccess = true,
            IsDeduplicatedUpload = true,
            DedupEntry = entry,
            Document = null,
            Message = "Mesmo documento já foi ingerido para este tenant (hash idêntico). Retornamos o documento existente.",
            PublishedDataIngestedEvent = false
        };

    public static IngestionOrchestratorResult Failure(string message, string[] errors) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            Errors = errors ?? Array.Empty<string>()
        };
}
