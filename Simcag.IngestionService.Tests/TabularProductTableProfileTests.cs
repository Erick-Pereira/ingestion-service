using Simcag.IngestionService.Application.UseCases;
using Simcag.IngestionService.Domain.Enums;
using Simcag.Shared.Finance;

namespace Simcag.IngestionService.Tests;

/// <summary>Regressão do perfil br.tabular_product_table.v1 — dimensões de layout, não fornecedor.</summary>
public class TabularProductTableProfileTests
{
    private readonly ParseDocumentUseCase _sut = ParseDocumentTestFactory.CreateUseCase();

    [Fact]
    public void Execute_matriz_layouts_tabulares_extracao_correta()
    {
        var failures = new List<string>();

        void Layout(string id, Action assert)
        {
            try
            {
                assert();
            }
            catch (Exception ex)
            {
                failures.Add($"{id}: {ex.Message}");
            }
        }

        Layout("colado_servico_3850", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.ServiceSingleLineGlued3850, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 3850m, descContains: "MANUTENCAO", descContains2: "elevador");
            AssertNoJunk(r);
        });

        Layout("espacado_material_un_3500", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.MaterialSingleLineSpacedUn3500, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 3500m, qty: 1m, descContains: "Material de manutencao predial");
            Assert.DoesNotContain(
                r.LineItems[0].Description,
                "Documento emitido para condominio",
                StringComparison.OrdinalIgnoreCase);
        });

        Layout("multi_espacado_qty12", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.SecurityMultiLineSpaced, DocumentType.NotaFiscal);
            Assert.Equal(2, r.LineItems.Count);
            Assert.Contains(r.LineItems, li =>
                li.Description.Contains("Camera IP", StringComparison.OrdinalIgnoreCase)
                && li.Amount!.Amount == 10680m && li.Quantity == 12m);
            Assert.Contains(r.LineItems, li =>
                li.Description.Contains("NVR 16 canais", StringComparison.OrdinalIgnoreCase)
                && li.Amount!.Amount == 4200m && li.Quantity == 1m);
        });

        Layout("multi_colado_qty12", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.SecurityMultiLineGlued, DocumentType.NotaFiscal);
            Assert.Equal(2, r.LineItems.Count);
            Assert.Contains(r.LineItems, li =>
                li.Description.Contains("Camera IP", StringComparison.OrdinalIgnoreCase)
                && li.Amount!.Amount == 10680m && li.Quantity == 12m && li.UnitPrice == 890m);
            Assert.Contains(r.LineItems, li =>
                li.Description.Contains("NVR 16 canais", StringComparison.OrdinalIgnoreCase)
                && li.Amount!.Amount == 4200m && li.Quantity == 1m && li.UnitPrice == 4200m);
            Assert.DoesNotContain(r.LineItems, li => li.Description.Contains("UN12,0000", StringComparison.Ordinal));
        });

        Layout("devolucao_espacado_natureza_572", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailDevolutionSpacedWithNatureza572, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 572.76m, qty: 1m, descContains: "Gabinete Gamer Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
        });

        Layout("devolucao_colado_natureza_572", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailDevolutionGluedWithNatureza572, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 572.76m, qty: 1m, descContains: "Gabinete Gamer Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
        });

        Layout("pdfpig_golden_devolucao", () =>
        {
            var goldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "pichau_nfe_pdfpig.txt");
            Assert.True(File.Exists(goldenPath), $"Golden ausente: {goldenPath}");
            var r = _sut.Execute(File.ReadAllText(goldenPath), DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 572.76m, qty: 1m, descContains: "Gabinete Gamer Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
        });

        Layout("espacado_un_299_natureza", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailSingleLineSpacedUn299, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 299.99m, qty: 1m, descContains: "Webcam Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(r.LineItems[0].Description, "Merc Terc", StringComparison.OrdinalIgnoreCase);
        });

        Layout("colado_valor_repetido_299", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailSingleLineGlued299, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 299.99m, qty: 1m, descContains: "Webcam Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
        });

        Layout("unid_unit_299", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailSingleLineUnidUnit299, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 299.99m, qty: 1m, descContains: "Webcam Pichau");
            Assert.DoesNotContain(r.LineItems[0].Description, "Dev Venda", StringComparison.OrdinalIgnoreCase);
        });

        Layout("colado_un1_triple_value_299", () =>
        {
            var r = _sut.Execute(TabularProductTableTestSamples.RetailSingleLineGluedUn1TripleValue299, DocumentType.NotaFiscal);
            AssertItem(r, count: 1, amount: 260.86m, qty: 1m, descContains: "Webcam Pichau");
            Assert.Equal(260.86m, r.LineItems[0].UnitPrice);
            Assert.DoesNotContain(r.LineItems[0].Description, "Garantia", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(r.LineItems[0].Description, "Nr.Serie", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(r.LineItems[0].Description, "Nota fiscal eletrônica", StringComparison.OrdinalIgnoreCase);
        });

        Assert.True(
            failures.Count == 0,
            "Layouts tabulares com falha:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TryExtract_emitente_nfe_nao_destinatario()
    {
        var failures = new List<string>();

        void Case(string id, string raw, string expectedName, string? expectedTaxId = null)
        {
            var hint = BrazilianDocumentSupplierExtractor.TryExtract(raw);
            if (!string.Equals(expectedName, hint.Name, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{id}: name expected '{expectedName}' got '{hint.Name}'");
            if (expectedTaxId is not null && hint.TaxId != expectedTaxId)
                failures.Add($"{id}: taxId expected '{expectedTaxId}' got '{hint.TaxId}'");
        }

        Case(
            "colado_recebemos_de",
            TabularProductTableTestSamples.ServiceSingleLineGlued3850,
            "ALFA MANUTENCAO PREDIAL LTDA",
            "12345678000190");

        Case(
            "devolucao_recebemos_de",
            TabularProductTableTestSamples.RetailDevolutionSpacedWithNatureza572,
            "BAZAM E PICHAU INFORMATICA LTDA");

        Case(
            "emitente_bloco",
            """
            IDENTIFICAÇÃO DO EMITENTE
            SEGURANCA ELETRONICA BRASIL LTDA
            Rua Tecnologica, 450
            CNPJ 12.345.678/0001-99
            DESTINATÁRIO CONDOMINIO RESIDENCIAL
            """,
            "SEGURANCA ELETRONICA BRASIL LTDA",
            "12345678000199");

        Assert.True(
            failures.Count == 0,
            "Fornecedor documental com falha:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static void AssertItem(
        ParseDocumentResult r,
        int count,
        decimal amount,
        string descContains,
        string? descContains2 = null,
        decimal? qty = null)
    {
        Assert.Equal(count, r.LineItems.Count);
        var item = r.LineItems[0];
        Assert.Equal(amount, item.Amount!.Amount);
        Assert.Contains(descContains, item.Description, StringComparison.OrdinalIgnoreCase);
        if (descContains2 is not null)
            Assert.Contains(descContains2, item.Description, StringComparison.OrdinalIgnoreCase);
        if (qty is not null)
            Assert.Equal(qty.Value, item.Quantity);
    }

    private static void AssertNoJunk(ParseDocumentResult r)
    {
        foreach (var li in r.LineItems)
        {
            Assert.DoesNotContain(li.Description, "RECEBEMOS DE", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(li.Description, "DANFE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
