using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Domain.Entities;

public class RawDocument
{
    public string Id { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string FileExtension { get; private set; } = string.Empty;
    public string MimeType { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public FileHash FileHash { get; private set; } = null!;
    public string Source { get; private set; } = string.Empty;
    public string Origin { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public Guid? UploadedBy { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public DocumentType DocumentType { get; private set; } = DocumentType.Desconhecido;
    public string RawText { get; private set; } = string.Empty;
    public List<ExtractedLineItem> ExtractedLineItems { get; private set; } = new();
    public DateTime? ProcessedAt { get; private set; }
    private byte[]? _content;

    protected RawDocument() { }

    public RawDocument(
        string id,
        string fileName,
        string fileExtension,
        string mimeType,
        long fileSize,
        FileHash fileHash,
        string source,
        string origin,
        DateTime uploadedAt)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id é obrigatório", nameof(id));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Nome do arquivo é obrigatório", nameof(fileName));

        ArgumentNullException.ThrowIfNull(fileHash);

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source é obrigatório", nameof(source));

        if (fileSize <= 0)
            throw new ArgumentException("Tamanho do arquivo deve ser maior que zero", nameof(fileSize));

        Id = id;
        FileName = fileName;
        FileExtension = fileExtension;
        MimeType = mimeType;
        FileSize = fileSize;
        FileHash = fileHash;
        Source = source;
        Origin = origin ?? string.Empty;
        UploadedAt = uploadedAt == default ? DateTime.UtcNow : uploadedAt;
    }

    /// <summary>
    /// Bytes originais do arquivo (necessários para OCR/parsers na infraestrutura).
    /// </summary>
    public void SetContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new ArgumentException("Conteúdo do arquivo não pode ser vazio.", nameof(content));

        _content = content;
    }

    public ReadOnlyMemory<byte> GetContent() =>
        _content is null ? ReadOnlyMemory<byte>.Empty : _content;

    public bool HasContent() => _content is { Length: > 0 };

    public void SetTenantId(string? tenantId) =>
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId.Trim();

    public void SetUploadedBy(Guid? userId) =>
        UploadedBy = userId is { } u && u != Guid.Empty ? u : null;

    public void SetRawText(string rawText) =>
        RawText = rawText ?? string.Empty;

    public void SetDocumentType(DocumentType documentType) =>
        DocumentType = documentType;

    public void SetExtractedLineItems(List<ExtractedLineItem> lineItems) =>
        ExtractedLineItems = lineItems ?? new List<ExtractedLineItem>();

    public void AddExtractedLineItem(ExtractedLineItem lineItem)
    {
        if (lineItem != null)
            ExtractedLineItems.Add(lineItem);
    }

    public void MarkAsProcessed() =>
        ProcessedAt = DateTime.UtcNow;

    public bool IsProcessed() => ProcessedAt.HasValue;

    /// <summary>
    /// Metadados mínimos após leitura do arquivo (antes de OCR/parsing).
    /// </summary>
    public bool HasIngestIntegrity() =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(FileName) &&
        FileSize > 0 &&
        HasContent();

    /// <summary>
    /// Pronto para publicar evento bruto: texto extraído não vazio.
    /// Itens de linha são opcionais (OCR/parsing podem não estruturar linhas).
    /// </summary>
    public bool CanPublishRawEvent() =>
        !string.IsNullOrWhiteSpace(RawText);
}
