using Simcag.IngestionService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.Services;

public interface IPdfParserService
{
    Task<string> ExtractTextAsync(RawDocument document, CancellationToken cancellationToken = default);
}