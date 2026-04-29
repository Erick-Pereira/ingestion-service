using Simcag.IngestionService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.UseCases;

public interface IPublishRawEventUseCase
{
    Task PublishAsync(RawDocument document, CancellationToken cancellationToken = default);
}