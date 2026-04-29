using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Application.Services;

namespace Simcag.IngestionService.Infrastructure.Parser;

public class ExcelParserService : IExcelParserService
{
    private readonly ILogger<ExcelParserService> _logger;

    static ExcelParserService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ExcelParserService(ILogger<ExcelParserService> logger)
    {
        _logger = logger;
    }

    public Task<List<List<string>>> ExtractDataAsync(RawDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Extraindo dados via Excel parser para documento {DocumentId}", document.Id);

        var content = document.GetContent();
        if (content.IsEmpty)
        {
            _logger.LogWarning("Sem bytes da planilha para {DocumentId}; retornando conjunto vazio.", document.Id);
            return Task.FromResult(new List<List<string>>());
        }

        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.UTF8
            });

            var data = new List<List<string>>();
            do
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = new List<string>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row.Add(reader.GetValue(i)?.ToString() ?? string.Empty);
                    data.Add(row);
                }
            } while (reader.NextResult());

            _logger.LogInformation(
                "Dados extraídos via Excel parser para documento {DocumentId}: {RowCount} linhas",
                document.Id,
                data.Count);

            return Task.FromResult(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao extrair dados via ExcelDataReader para documento {DocumentId}",
                document.Id);
            throw;
        }
    }
}
