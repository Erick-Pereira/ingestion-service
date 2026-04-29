using Simcag.IngestionService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.Services;

public interface IOcrService
{
    Task<string> PerformOcrAsync(RawDocument document, CancellationToken cancellationToken = default);
}