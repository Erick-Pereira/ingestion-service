using Simcag.IngestionService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.UseCases;

public interface IExtractTextUseCase
{
    Task<string> ExtractAsync(RawDocument document, CancellationToken cancellationToken = default);
}