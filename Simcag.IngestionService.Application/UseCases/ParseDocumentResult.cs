using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Application.UseCases;

public sealed record ParseDocumentResult(
    List<ExtractedLineItem> LineItems,
    DocumentType DocumentType);
