using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Simcag.IngestionService.Application.UseCases;

public class IngestDocumentUseCase : IIngestDocumentUseCase
{
    private readonly ILogger<IngestDocumentUseCase> _logger;

    public IngestDocumentUseCase(ILogger<IngestDocumentUseCase> logger)
    {
        _logger = logger;
    }

    public async Task<RawDocument> ExecuteAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        long fileSize,
        string source,
        string origin,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedMime = ResolveMimeType(fileName, mimeType);
        ValidateFile(fileName, resolvedMime, fileSize);

        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();

        if (fileBytes.Length == 0)
            throw new ArgumentException("Arquivo vazio.", nameof(fileStream));

        var fileHash = FileHash.ComputeSha256(fileBytes);
        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
        var documentType = InferDocumentType(fileExtension, resolvedMime);

        var document = new RawDocument(
            id: Guid.NewGuid().ToString(),
            fileName: fileName,
            fileExtension: fileExtension,
            mimeType: resolvedMime,
            fileSize: fileSize,
            fileHash: fileHash,
            source: source,
            origin: origin,
            uploadedAt: DateTime.UtcNow);

        document.SetContent(fileBytes);
        document.SetTenantId(tenantId);
        document.SetDocumentType(documentType);

        _logger.LogInformation(
            "Documento {DocumentId} ingerido | Type: {DocType} | Size: {Size} bytes | Hash: {HashPrefix}",
            document.Id,
            documentType,
            fileSize,
            fileHash.Value[..Math.Min(8, fileHash.Value.Length)]);

        return document;
    }

    private static string ResolveMimeType(string fileName, string mimeType)
    {
        var trimmed = (mimeType ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(trimmed) &&
            !trimmed.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            _ => string.IsNullOrEmpty(trimmed) ? "application/octet-stream" : trimmed.ToLowerInvariant()
        };
    }

    private void ValidateFile(string fileName, string mimeType, long fileSize)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(fileName))
            errors.Add("Nome do arquivo é obrigatório");
        else if (fileName.Length > 255)
            errors.Add("Nome do arquivo excede 255 caracteres");

        if (fileSize <= 0)
            errors.Add("Tamanho do arquivo deve ser maior que zero");
        else if (fileSize > 50 * 1024 * 1024)
            errors.Add("Tamanho do arquivo não pode exceder 50MB");

        var allowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel",
            "image/jpeg",
            "image/png",
            "image/tiff",
            "text/plain",
            "text/csv"
        };

        if (!allowedMimeTypes.Contains(mimeType))
            errors.Add($"Tipo de arquivo não suportado: {mimeType}");

        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Validação de arquivo falhou: {string.Join("; ", errors)}",
                nameof(fileName));
        }
    }

    private static DocumentType InferDocumentType(string fileExtension, string mimeType)
    {
        if (!string.IsNullOrEmpty(fileExtension))
        {
            return fileExtension switch
            {
                ".pdf" => DocumentType.NotaFiscal,
                ".xlsx" or ".xls" => DocumentType.Balancete,
                ".csv" or ".txt" => DocumentType.Balancete,
                ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" => DocumentType.NotaFiscal,
                _ => InferFromMimeOnly(mimeType)
            };
        }

        return InferFromMimeOnly(mimeType);
    }

    private static DocumentType InferFromMimeOnly(string mimeType)
    {
        if (mimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return DocumentType.NotaFiscal;
        if (mimeType.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase))
            return DocumentType.Balancete;
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return DocumentType.NotaFiscal;
        if (mimeType.Contains("csv", StringComparison.OrdinalIgnoreCase) ||
            mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return DocumentType.Balancete;

        return DocumentType.Desconhecido;
    }
}
