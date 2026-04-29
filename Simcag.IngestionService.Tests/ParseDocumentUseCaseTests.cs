using Microsoft.Extensions.Logging.Abstractions;
using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;

namespace Simcag.IngestionService.Tests;

public class ParseDocumentUseCaseTests
{
    private readonly ParseDocumentUseCase _sut = new(NullLogger<ParseDocumentUseCase>.Instance);

    [Fact]
    public void Execute_detecta_nota_fiscal_e_extrai_linha_com_valor()
    {
        var raw = """
            NOTA FISCAL 123
            Serviço de limpeza R$ 500,00 em 15/04/2026
            """;

        var result = _sut.Execute(raw, DocumentType.Desconhecido);

        Assert.Equal(DocumentType.NotaFiscal, result.DocumentType);
        Assert.NotEmpty(result.LineItems);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount!.Amount == 500m);
    }

    [Fact]
    public void Execute_preserva_tipo_quando_ja_definido()
    {
        var raw = "Linha qualquer R$ 10,00 em 01/01/2026";

        var result = _sut.Execute(raw, DocumentType.Balancete);

        Assert.Equal(DocumentType.Balancete, result.DocumentType);
    }
}
