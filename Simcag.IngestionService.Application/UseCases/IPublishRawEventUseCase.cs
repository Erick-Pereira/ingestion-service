using Simcag.IngestionService.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Simcag.IngestionService.Application.UseCases;

public interface IPublishRawEventUseCase
{
    Task<RawEventPublishOutcome> PublishAsync(RawDocument document, CancellationToken cancellationToken = default);
}