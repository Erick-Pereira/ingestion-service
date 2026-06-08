using Simcag.Shared.Finance;

namespace Simcag.IngestionService.Tests;

public class BrazilianDocumentSupplierExtractorTests
{
    [Fact]
    public void TryExtract_nfse_fortaleza_prestador_e_cnpj()
    {
        var raw = """
            PREFEITURA MUNICIPAL DE FORTALEZA
            PRESTADOR DE SERVIÇOS
            Razão Social Condomínio Residencial Atlântico Sul
            CNPJ 18.456.782/0001-22
            TOMADOR DE SERVIÇOS
            Nome Ricardo Mendes Oliveira
            """;

        var hint = BrazilianDocumentSupplierExtractor.TryExtract(raw);

        Assert.Equal("Condomínio Residencial Atlântico Sul", hint.Name);
        Assert.Equal("18456782000122", hint.TaxId);
    }

    [Fact]
    public void TryExtract_prestador_colado_sem_quebras()
    {
        var raw = """
            PRESTADOR DE SERVIÇOSRazão SocialCondomínio Residencial Atlântico SulCNPJ18.456.782/0001-22TOMADOR DE SERVIÇOSNome Fulano
            """;

        var hint = BrazilianDocumentSupplierExtractor.TryExtract(raw);

        Assert.Equal("Condomínio Residencial Atlântico Sul", hint.Name);
        Assert.Equal("18456782000122", hint.TaxId);
    }

    [Fact]
    public void TryExtract_nfe_emitente_identificacao_bloco()
    {
        var raw = """
            IDENTIFICAÇÃO DO EMITENTE
            SEGURANCA ELETRONICA BRASIL LTDA
            Rua Tecnologica, 450
            INSCRIÇÃO ESTADUAL DO SUBST. TRIBUT. CNPJ / CPF
            12.345.678/0001-99
            DESTINATÁRIO / REMETENTE
            CONDOMINIO RESIDENCIAL VILA DAS FLORES
            CNPJ / CPF
            98.765.432/0001-11
            """;

        var hint = BrazilianDocumentSupplierExtractor.TryExtract(raw);

        Assert.Equal("SEGURANCA ELETRONICA BRASIL LTDA", hint.Name);
        Assert.Equal("12345678000199", hint.TaxId);
    }
}
