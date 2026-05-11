using System.Text.RegularExpressions;
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

    [Fact]
    public void Execute_nao_descarta_linha_so_por_conter_conta_quando_ha_valor()
    {
        var raw = """
            Conta de água e esgoto (CONTA 123)    450,88
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.NotEmpty(result.LineItems);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount!.Amount == 450.88m);
    }

    [Fact]
    public void Execute_pdf_como_nf_eh_reclassificado_para_balancete_em_relatorio_condominio()
    {
        var raw = """
            RELATÓRIO DE DESPESAS DO CONDOMÍNIO EXEMPLO
            Taxa condominial    1.234,56
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(DocumentType.Balancete, result.DocumentType);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount!.Amount == 1234.56m);
    }

    /// <summary>
    /// Texto tal como o PdfPig extrai <c>relatorio_condominio.pdf</c> (tabela colada numa linha).
    /// </summary>
    [Fact]
    public void Execute_relatorio_condominio_pdf_extrai_nove_linhas_de_despesa()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "relatorio_condominio_pdfpig.txt");
        Assert.True(File.Exists(goldenPath),
            $"Copie/mantenha TestData em: {goldenPath}");

        var raw = File.ReadAllText(goldenPath);

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(DocumentType.Balancete, result.DocumentType);
        Assert.Equal(9, result.LineItems.Count);

        Assert.Contains(result.LineItems, li => li.Description.Contains("elevador", StringComparison.OrdinalIgnoreCase)
                                                 && li.Amount!.Amount == 2500m);
        Assert.Contains(result.LineItems, li => li.Amount!.Amount == 5500m);
        Assert.Contains(result.LineItems, li => li.Amount!.Amount == 450m);

        var sum = result.LineItems.Where(li => li.Amount != null).Sum(li => li.Amount!.Amount);
        Assert.Equal(20350m, sum);
    }

    /// <summary>
    /// PdfPig às vezes insere muitas quebras; o parser não deve voltar ao modo linha-a-linha e perder a tabela compacta.
    /// </summary>
    [Fact]
    public void Execute_relatorio_condominio_com_muitas_quebras_ainda_extrai_nove_sem_linha_lixo()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "relatorio_condominio_pdfpig.txt");
        var raw = File.ReadAllText(goldenPath);
        raw = string.Join("\n\n", raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        raw = Regex.Replace(raw, @"(,\d{2})(?=[A-Za-zÀ-ÿ])", "$1\n");

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(DocumentType.Balancete, result.DocumentType);
        Assert.Equal(9, result.LineItems.Count);
        Assert.DoesNotContain(result.LineItems, li =>
            li.Description.Contains("CategoriaDescrição", StringComparison.OrdinalIgnoreCase)
            || li.Description.Contains("referentes ao mês", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(20350m, result.LineItems.Where(li => li.Amount != null).Sum(li => li.Amount!.Amount));
    }
}
