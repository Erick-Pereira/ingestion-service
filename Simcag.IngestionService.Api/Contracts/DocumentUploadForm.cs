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

    public string? TenantId { get; set; }
}
