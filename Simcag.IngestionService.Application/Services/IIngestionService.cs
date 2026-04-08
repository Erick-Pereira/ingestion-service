using System.Threading.Tasks;
using Simcag.IngestionService.Domain.Events;

namespace Simcag.IngestionService.Application.Services
{
    public interface IIngestionService
    {
        Task<IngestionResult> ProcessPriceCollectedEventAsync(PriceCollectedEvent @event, CancellationToken cancellationToken);
    }

    public class IngestionResult
    {
        public bool Success { get; set; }
        public required string[] Errors { get; set; }
        public required string Message { get; set; }
    }
}