namespace Simcag.IngestionService.Application.Services;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public required string[] Errors { get; set; }
}