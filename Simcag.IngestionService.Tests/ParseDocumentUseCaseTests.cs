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

    /// <summary>
    /// PdfPig cola rótulos + valores (centavos com espaço); CNPJ sem barra não pode virar montante milhões.
    /// </summary>
    [Fact]
    public void Execute_condominio_recibo_colado_extrai_itens_e_ignora_cnpj()
    {
        var raw = """
            NOTA FISCAL RECIBO CONDOMINIALCondomínio Residencial Jardim das PalmeirasCNPJ 12 345 678 0001-90
            Rua das Flores 245 CompetênciaMaio 2026
            DescriçãoValor R Taxa Condominial650 00Fundo de Reserva85 00Consumo de Água120 00Manutenção Elevador45 00Taxa Extraordinária100 00TOTAL Este documento
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.DoesNotContain(
            result.LineItems,
            li => li.Amount is { Amount: 12345678m });
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 650m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 85m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 120m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 45m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 100m);
        Assert.Equal(5, result.LineItems.Count(li => li.Amount != null));
        Assert.Equal(1000m, result.LineItems.Where(li => li.Amount != null).Sum(li => li.Amount!.Amount));
    }

    [Fact]
    public void Execute_recibo_pagamento_itemvalor_r_extrai_linhas()
    {
        var raw = """
            RECIBO DE PAGAMENTO CONDOMINIAL Condomínio Vista Mar CNPJ 45 987 321 0001-10
            ItemValor R Condomínio Mensal780 00Energia Área Comum95 00Serviço de Limpeza60 00Segurança140 00VALOR TOTAL Declaro
            """;

        var result = _sut.Execute(raw, DocumentType.Recibo);

        Assert.DoesNotContain(result.LineItems, li => li.Amount is { Amount: >= 1_000_000m });
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 780m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 95m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 60m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 140m);
        Assert.Equal(1075m, result.LineItems.Where(li => li.Amount != null).Sum(li => li.Amount!.Amount));
    }

    [Fact]
    public void Execute_nfse_prefeitura_fortaleza_extrai_linha_por_valor_liquido_quando_discriminacao_truncada()
    {
        var raw = """
            PREFEITURA MUNICIPAL DE FORTALEZA NOTA FISCAL DE SERVIÇOS ELETRÔNICA - NFS-e
            PRESTADOR Condomínio Residencial Atlântico Sul CNPJ 18 456 782 0001-22
            DISCRIMINAÇÃO DOS SERVIÇOS
            DescriçãoQtdValor Unit Valor TotalTaxa Condominial - Competência Maior/2026
            Valor Líquido do Serviço R$ 1.500,75
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Single(result.LineItems, li => li.Amount != null);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 1500.75m);
        Assert.Contains(result.LineItems, li => li.Description.Contains("Taxa Condominial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_nfse_discriminacao_com_valor_na_linha()
    {
        var raw = """
            NOTA FISCAL DE SERVIÇOS ELETRÔNICA NFS-e PREFEITURA X
            DISCRIMINAÇÃO DOS SERVIÇOS
            DescriçãoQtdValor Unit Valor Total
            Serviço limpeza450,50Manutenção predial1.250,75
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(2, result.LineItems.Count(li => li.Amount != null));
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 450.50m);
        Assert.Contains(result.LineItems, li => li.Amount != null && li.Amount.Amount == 1250.75m);
    }
}
