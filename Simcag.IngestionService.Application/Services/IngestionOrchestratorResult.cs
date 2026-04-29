using Simcag.IngestionService.Domain.Entities;

namespace Simcag.IngestionService.Application.Services;

public class IngestionOrchestratorResult
{
    public bool IsSuccess { get; private set; }
    public RawDocument? Document { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string[] Errors { get; private set; } = Array.Empty<string>();

    private IngestionOrchestratorResult() { }

    public static IngestionOrchestratorResult Success(RawDocument document) =>
        new()
        {
            IsSuccess = true,
            Document = document,
            Message = "Ingestão concluída com sucesso"
        };

    public static IngestionOrchestratorResult Failure(string message, string[] errors) =>
        new()
        {
            IsSuccess = false,
            Message = message,
            Errors = errors ?? Array.Empty<string>()
        };
}
