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

    /// <summary>NFS-e Fortaleza realista (nota_fiscal_condominio_realista.pdf) — prestador + 3 serviços com R$.</summary>
    [Fact]
    public void Execute_nfse_fortaleza_realista_extrai_tres_itens_e_descricoes_limpas()
    {
        var raw = """
            PREFEITURA MUNICIPAL DE FORTALEZA
            NOTA FISCAL DE SERVIÇOS ELETRÔNICA - NFS-e
            PRESTADOR DE SERVIÇOS
            Razão Social Condomínio Residencial Atlântico Sul
            CNPJ 18.456.782/0001-22
            TOMADOR DE SERVIÇOS
            DISCRIMINAÇÃO DOS SERVIÇOS
            Descrição Qtd Valor Unit. Valor Total
            Taxa Condominial - Competência Maio/2026 1 R$ 820,00 R$ 820,00
            Fundo de Reserva 1 R$ 120,00 R$ 120,00
            Taxa de Segurança 1 R$ 75,00 R$ 75,00
            Valor Total dos Serviços R$ 1.015,00
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(3, result.LineItems.Count);
        Assert.Contains(result.LineItems, li => li.Description.Contains("Taxa Condominial", StringComparison.OrdinalIgnoreCase) && li.Amount!.Amount == 820m);
        Assert.Contains(result.LineItems, li => li.Description == "Fundo de Reserva" && li.Amount!.Amount == 120m);
        Assert.Contains(result.LineItems, li => li.Description.Contains("Taxa de Segurança", StringComparison.OrdinalIgnoreCase) && li.Amount!.Amount == 75m);
        Assert.Equal(1015m, result.LineItems.Sum(li => li.Amount!.Amount));
    }

    [Fact]
    public void Execute_nfse_fortaleza_colada_pelo_pdfpig_limpa_fundo_reserva()
    {
        var raw = """
            PREFEITURA MUNICIPAL DE FORTALEZANOTA FISCAL DE SERVIÇOS ELETRÔNICA - NFS-eNúmero da Nota373850PRESTADOR DE SERVIÇOSRazão SocialCondomínio Residencial Atlântico SulCNPJ18.456.782/0001-22TOMADOR DE SERVIÇOSDISCRIMINAÇÃO DOS SERVIÇOSDescriçãoQtdValor Unit.Valor TotalTaxa Condominial - Competência Maio/20261R$ 820,00R$ 820,00Fundo de Reserva1R$ 120,00R$ 120,00Taxa de Segurança1R$ 75,00R$ 75,00Valor Total dos ServiçosR$ 1.015,00
            """;

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(3, result.LineItems.Count);
        Assert.Contains(result.LineItems, li => li.Description.Contains("Taxa Condominial", StringComparison.OrdinalIgnoreCase) && li.Amount!.Amount == 820m);
        Assert.Contains(result.LineItems, li => li.Description == "Fundo de Reserva" && li.Amount!.Amount == 120m);
        Assert.Contains(result.LineItems, li => li.Description.Contains("Taxa de Segurança", StringComparison.OrdinalIgnoreCase) && li.Amount!.Amount == 75m);
        Assert.DoesNotContain(result.LineItems, li => li.Description.Contains("R$", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_danfe_nfe_colada_extrai_descricao_limpa_e_valor()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "consulta_danfe_pdfpig.txt");
        string raw;
        if (File.Exists(goldenPath))
        {
            raw = File.ReadAllText(goldenPath);
        }
        else
        {
            raw = """
                RECEBEMOS DE ALFA MANUTENCAO PREDIAL LTDA OS PRODUTOS E/OU SERVIÇOS CONSTANTES DA NOTA FISCAL ELETRÔNICA INDICADAABAIXO. EMISSÃO: 31/05/2026 VALOR TOTAL: R$ 3.850,00 DESTINATÁRIO: CONDOMINIO DO BLOCO B DA SQS 107 - SQS 107 BLOCO B, S/N ASASUL BRASILIA-DFNF-eNº. 000.000.120Série 001DATA DE RECEBIMENTOIDENTIFICAÇÃO E ASSINATURA DO RECEBEDORIDENTIFICAÇÃO DO EMITENTEALFA MANUTENCAO PREDIAL LTDASIA TRECHO 03, 100ZONA INDUSTRIAL - 71200-000BRASILIA - DF Fone/Fax: 6133334444DANFEDocumento Auxiliar da NotaFiscal Eletrônica0 - ENTRADA1 - SAÍDA1Nº. 000.000.120Série 001Folha 1/1CHAVE DE ACESSO5326 0512 3456 7800 0190 5500 1000 0001 2010 0000 1201Consulta de autenticidade no portal nacional da NF-ewww.nfe.fazenda.gov.br/portal ou no site da Sefaz AutorizadoraNATUREZA DA OPERAÇÃOPrestação de Serviços de ManutençãoPROTOCOLO DE AUTORIZAÇÃO DE USOINSCRIÇÃO ESTADUAL07456321001INSCRIÇÃO ESTADUAL DO SUBST. TRIBUT.CNPJ / CPF12.345.678/0001-90SEM VALOR FISCALAMBIENTE DE HOMOLOGAÇÃODESTINATÁRIO / REMETENTENOME / RAZÃO SOCIALCONDOMINIO DO BLOCO B DA SQS 107CNPJ / CPF73.904.290/0001-06DATA DA EMISSÃO31/05/2026ENDEREÇOSQS 107 BLOCO B, S/NBAIRRO / DISTRITOASA SULCEP70346-020DATA DA SAÍDA/ENTRADAMUNICÍPIOBRASILIAUFDFFONE / FAXINSCRIÇÃO ESTADUALHORA DA SAÍDA/ENTRADACÁLCULO DO IMPOSTOBASE DE CÁLC. DO ICMS0,00VALOR DO ICMS0,00BASE DE CÁLC. ICMS S.T.0,00VALOR DO ICMS SUBST.0,00V. IMP. IMPORTAÇÃO0,00V. ICMS UF REMET.0, 00V. FCP UF DEST.0, 00VALOR DO PIS0,00V. TOTAL PRODUTOS3.850,00VALOR DO FRETE0,00VALOR DO SEGURO0,00DESCONTO0,00OUTRAS DESPESAS0,00VALOR TOTAL IPI0,00V. ICMS UF DEST.0, 00V. TOT. TRIB.0,00VALOR DA COFINS0,00V. TOTAL DA NOTA3.850,00TRANSPORTADOR / VOLUMES TRANSPORTADOSNOME / RAZÃO SOCIALFRETE9-Sem TransporteCÓDIGO ANTTPLACA DO VEÍCULOUFCNPJ / CPFENDEREÇOMUNICÍPIOUFINSCRIÇÃO ESTADUALQUANTIDADEESPÉCIEMARCANUMERAÇÃOPESO BRUTOPESO LÍQUIDODADOS DOS PRODUTOS / SERVIÇOSCÓDIGO PRODUTODESCRIÇÃO DO PRODUTO / SERVIÇONCM/SHO/CSOSNCFOPUNQUANTVALORUNITVALORTOTALB.CÁLCICMSVALORICMSVALORIPIALÍQ.ICMSALÍQ. IPI001MANUTENCAO PREVENTIVA E CORRETIVA DEELEVADORES491101001025933UN1,00003.850,00003.850,000, 000, 000, 00DADOS ADICIONAISINFORMAÇÕES COMPLEMENTARESInf. Contribuinte: Prestação de serviços de manutenção preventiva e corretiva de elevadores do condomínio referente ao mês demaio/2026.Valor Aproximado dos Tributos : R$ 0,00RESERVADO AO FISCOImpresso em 31/05/2026 as 20:33:27Consulta DANFE - https://consultadanfe.com
                """;
        }

        var result = _sut.Execute(raw, DocumentType.NotaFiscal);

        Assert.Equal(DocumentType.NotaFiscal, result.DocumentType);
        Assert.Single(result.LineItems);
        var item = result.LineItems[0];
        Assert.Equal(3850m, item.Amount!.Amount);
        Assert.Contains("manutenção", item.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("elevador", item.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.LineItems, li => li.Description.Contains("RECEBEMOS DE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.LineItems, li => li.Description.Contains("DANFE", StringComparison.OrdinalIgnoreCase));
    }
}
