using Simcag.IngestionService.Domain.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.Services;

public interface IExcelParserService
{
    Task<List<List<string>>> ExtractDataAsync(RawDocument document, CancellationToken cancellationToken = default);
}