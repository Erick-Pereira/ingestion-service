using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Simcag.IngestionService.Api.Contracts;

/// <summary>
/// Formulário multipart para upload — um único parâmetro <c>[FromForm]</c> evita falha do Swashbuckle ao gerar OpenAPI.
/// </summary>
public sealed class DocumentUploadForm
{
    [Required]
    public IFormFile File { get; set; } = null!;

    public string Source { get; set; } = "manual";

    public string Origin { get; set; } = string.Empty;

    /// <summary>
    /// Opcional. Deve ser um GUID válido. Via gateway autenticado, pode omitir: usa-se o claim <c>tenant_id</c> do JWT (<c>X-Tenant-Id</c>).
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Se <c>true</c>, ignora deduplicação por hash (força novo <c>documentId</c> e novo processamento).
    /// </summary>
    public bool Force { get; set; }
}
