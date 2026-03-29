using System.Threading.Tasks;
using shared.Events;

namespace Simcag.IngestionService.Application.Services
{
    public interface IIngestionService
    {
        Task<IngestionResult> ProcessPriceCollectedEventAsync(PriceCollectedEvent @event);
    }

    public class IngestionResult
    {
        public bool Success { get; set; }
        public required string[] Errors { get; set; }
        public required string Message { get; set; }
    }
}