using System.Globalization;
using System.Text.RegularExpressions;
using Simcag.IngestionService.Application.DocumentExtraction;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.Finance;

namespace Simcag.IngestionService.Application.DocumentExtraction.Parsing;

/// <summary>Helpers de parsing estrutural partilhados entre perfis de layout.</summary>
internal static class DocumentParsers
{
    /// <summary>
    /// Remove padrões de CNPJ/CPF para não serem interpretados como montantes (ex.: <c>12.345.678</c> dentro de CNPJ).
    /// </summary>
    internal static string MaskBrazilianTaxIds(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // CNPJ sem barra: "12 345 678 0001-90" (comum em PDFs condominiais)
        input = Regex.Replace(
            input,
            @"\b\d{2}\s+\d{3}\s+\d{3}\s+\d{4}\s*-\s*\d{2}\b",
            " #CNPJ# ",
            RegexOptions.CultureInvariant);
        // CNPJ com barra / pontos
        input = Regex.Replace(
            input,
            @"\b\d{2}[\s\.]?\d{3}[\s\.]?\d{3}\s*/\s*\d{4}[\s\-]?\d{2}\b",
            " #CNPJ# ",
            RegexOptions.CultureInvariant);
        // CPF
        input = Regex.Replace(
            input,
            @"\b\d{3}[\s\.]?\d{3}[\s\.]?\d{3}[\s\-]?\d{2}\b",
            " #CPF# ",
            RegexOptions.CultureInvariant);

        return input;
    }

    /// <summary>
    /// PdfPig cola tabelas tipo "Taxa Condominial650 00Fundo de Reserva85 00" (centavos separados por espaço).
    /// </summary>
    internal static bool LooksLikeGluedCondominioInvoice(string text)
    {
        var u = text.ToUpperInvariant();
        if (!u.Contains("CONDOM", StringComparison.Ordinal))
            return false;

        var invoiceCue =
            u.Contains("RECIBO", StringComparison.Ordinal)
            || u.Contains("NFS", StringComparison.Ordinal)
            || u.Contains("COBRAN", StringComparison.Ordinal)
            || u.Contains("VALOR R", StringComparison.Ordinal)
            || u.Contains("ITEMVALOR", StringComparison.Ordinal)
            || u.Contains("DISCRIMIN", StringComparison.Ordinal);

        if (!invoiceCue)
            return false;

        // Pelo menos um bloco "…00Letra" (fim de valor em centavos colado ao próximo rótulo, ex.: "650 00Fundo")
        return Regex.IsMatch(text, @"\d{2}(?=[A-Za-zÀ-ÿ])", RegexOptions.CultureInvariant);
    }

    internal static string? TrySliceCondominioChargesSection(string raw)
    {
        var bestStart = -1;
        foreach (var marker in new[]
                 {
                     "Discriminação dos Serviços",
                     "Discriminacao dos Servicos",
                     "DescriçãoValor",
                     "DescricaoValor",
                     "ItemValor",
                     "Item Valor",
                     "Valor R",
                 })
        {
            var i = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            var endPos = i + marker.Length;
            if (endPos > bestStart)
                bestStart = endPos;
        }

        if (bestStart < 0)
            return null;

        var end = raw.Length;
        foreach (var endMarker in new[]
                 {
                     "Valor total dos serviços",
                     "Valor Total dos Serviços",
                     "VALOR TOTAL DO SERVIÇO",
                     "Valor Total do Serviço",
                     "VALOR TOTAL DA NOTA",
                     "Valor Total da Nota",
                     // Evitar "VALOR TOTAL" isolado: na NFS-e cola-se "Valor TotalTaxa…" (cabeçalho da tabela).
                     "VALOR TOTAL Declaro",
                     "VALOR TOTAL Declar",
                     "TOTAL ESTE",
                     "TOTAL Este",
                     "TOTAIS",
                     "Administrador Responsável",
                     "Administrador Responsavel",
                     "Síndico Responsável",
                     "Sindico Responsavel",
                 })
        {
            var j = raw.IndexOf(endMarker, bestStart, StringComparison.OrdinalIgnoreCase);
            if (j >= 0)
                end = Math.Min(end, j);
        }

        if (end <= bestStart)
            return null;

        return raw[bestStart..end].Trim();
    }

    internal static string TrimSectionBeforeMarkers(string section, params string[] markers)
    {
        var end = section.Length;
        foreach (var m in markers)
        {
            var i = section.IndexOf(m, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                end = Math.Min(end, i);
        }

        return section[..end].Trim();
    }

    internal static List<ExtractedLineItem> TryExtractGluedCondominioInvoiceLineItems(string text)
    {
        var list = new List<ExtractedLineItem>();
        if (!LooksLikeGluedCondominioInvoice(text))
            return list;

        var section = TrySliceCondominioChargesSection(text);
        if (string.IsNullOrWhiteSpace(section))
            return list;

        section = section.Replace('\u00a0', ' ').Trim();

        // "…650 00Fundo de Reserva85 00…" → partes por limite centavos + próximo rótulo ("00Manutenção", "00Fundo")
        var parts = Regex
            .Split(section, @"(?<=\d{2})(?=[A-Za-zÀ-ÿ])")
            .Select(p => p.Trim())
            .Where(p => p.Length > 3)
            .ToList();

        var lineNo = 1;
        foreach (var chunk in parts)
        {
            if (chunk.Contains("TOTAL", StringComparison.OrdinalIgnoreCase)
                && chunk.Length < 40)
                continue;

            decimal? amt = null;
            string desc;

            var mSpace = Regex.Match(
                chunk,
                @"^(?<desc>.+?)(?<int>\d{1,5})\s+(?<cent>\d{2})$",
                RegexOptions.CultureInvariant);
            if (mSpace.Success
                && int.TryParse(mSpace.Groups["int"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var intPart)
                && int.TryParse(mSpace.Groups["cent"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var centPart)
                && centPart is >= 0 and < 100)
            {
                amt = intPart + centPart / 100m;
                desc = mSpace.Groups["desc"].Value.Trim();
            }
            else
            {
                var mBr = Regex.Match(
                    chunk,
                    @"^(?<desc>.+?)(?<br>\d{1,3}(?:\.\d{3})*,\d{2})$",
                    RegexOptions.CultureInvariant);
                if (!mBr.Success || !TryParseBrazilianMoney(mBr.Groups["br"].Value, out var brAmt))
                    continue;
                amt = brAmt;
                desc = mBr.Groups["desc"].Value.Trim();
            }

            if (amt is null or <= 0m || string.IsNullOrWhiteSpace(desc))
                continue;
            if (desc.Length > 200 || amt > 500_000m)
                continue;

            list.Add(new ExtractedLineItem(
                lineNumber: lineNo++,
                amount: new Money(amt.Value, "BRL"),
                date: null,
                description: desc,
                rawLine: chunk.Length > 400 ? chunk[..400] : chunk,
                confidenceScore: 82));
        }

        return list;
    }

    /// <summary>
    /// NFS-e municipal (ex. Fortaleza): PdfPig cola "DISCRIMINAÇÃO…DescriçãoQtdValor Unit Valor Total" + linhas de serviço.
    /// Quando o PDF trunca antes do valor na discriminação, tenta-se ler totais do corpo (Valor Líquido, etc.).
    /// </summary>
    internal static bool LooksLikeGluedNfsePrefeitura(string text)
    {
        var u = text.ToUpperInvariant();
        if (!u.Contains("NFS", StringComparison.Ordinal) && !u.Contains("NOTA FISCAL DE SERV", StringComparison.Ordinal))
            return false;
        if (!u.Contains("DISCRIMIN", StringComparison.Ordinal))
            return false;

        return u.Contains("PREFEITURA", StringComparison.Ordinal)
               || u.Contains("TOMADOR", StringComparison.Ordinal)
               || u.Contains("PRESTADOR", StringComparison.Ordinal)
               || u.Contains("VALOR UNIT", StringComparison.Ordinal);
    }

    internal static List<ExtractedLineItem> TryExtractGluedNfseDiscriminacaoLineItems(string text)
    {
        var list = new List<ExtractedLineItem>();
        if (!LooksLikeGluedNfsePrefeitura(text))
            return list;

        var section = TrySliceCondominioChargesSection(text);
        if (string.IsNullOrWhiteSpace(section))
            return list;

        section = section.Replace('\u00a0', ' ').Trim();
        var sectionForRows = TrimSectionBeforeMarkers(
            section,
            "Valor Líquido",
            "VALOR LÍQUIDO",
            "Valor do ISS",
            "Base de Cálculo",
            "Valor Aproximado");

        sectionForRows = StripNfseDiscriminacaoColumnHeaders(sectionForRows);

        var fromRows = ExtractNfseDiscriminacaoValueRows(sectionForRows);
        if (fromRows.Count >= 2)
            return fromRows;

        // Uma linha bem parseada (ex. discriminação truncada) ainda é preferível ao agregado.
        if (fromRows.Count == 1)
            return fromRows;

        var total = TrySniffNfseServiceTotalBrl(text);
        if (total is null || total <= 0m)
            return list;

        var desc = Regex.Replace(sectionForRows, @"\s+", " ").Trim();
        if (desc.Length > 220)
            desc = desc[..220];
        if (desc.Length < 6)
            return list;

        list.Add(new ExtractedLineItem(
            lineNumber: 1,
            amount: new Money(total.Value, "BRL"),
            date: null,
            description: desc,
            rawLine: desc.Length > 400 ? desc[..400] : desc,
            confidenceScore: 68));

        return list;
    }

    internal static string StripNfseDiscriminacaoColumnHeaders(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        // PdfPig cola "…Valor TotalTaxa…" (sem espaço após Total) — não usar \b após "Total".
        s = Regex.Replace(s, @"^\s*.*?\bValor\s*Total", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    internal static List<ExtractedLineItem> ExtractNfseDiscriminacaoValueRows(string section)
    {
        var list = new List<ExtractedLineItem>();

        // PdfPig pode preservar quebras — tentar linha a linha primeiro.
        var logicalLines = section
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 4)
            .ToList();

        if (logicalLines.Count <= 1)
        {
            logicalLines = Regex
                .Split(section, @"(?<=\d{2},\d{2})(?=[A-ZÀ-ÿ][a-zà-ÿ])")
                .Select(p => p.Trim())
                .Where(p => p.Length > 4)
                .ToList();
        }

        var lineNo = 1;
        foreach (var piece in logicalLines)
        {
            if (!TryParseNfseDiscriminacaoRow(piece, out var desc, out var amt))
                continue;
            if (IsNfseJunkDescription(desc))
                continue;

            list.Add(new ExtractedLineItem(
                lineNumber: lineNo++,
                amount: new Money(amt, "BRL"),
                date: null,
                description: desc,
                rawLine: piece.Length > 400 ? piece[..400] : piece,
                confidenceScore: 85));
        }

        if (list.Count > 0)
            return list;

        // Fallback: blocos colados "…450,50Manutenção…"
        var pieces = Regex
            .Split(section, @"(?<=,\d{2})(?=[A-ZÀ-ÿ][a-zà-ÿ])")
            .Select(p => p.Trim())
            .Where(p => p.Length > 4)
            .ToList();

        foreach (var piece in pieces)
        {
            if (!TryParseNfseDiscriminacaoRow(piece, out var desc, out var amt))
                continue;
            if (IsNfseJunkDescription(desc))
                continue;

            list.Add(new ExtractedLineItem(
                lineNumber: lineNo++,
                amount: new Money(amt, "BRL"),
                date: null,
                description: desc,
                rawLine: piece.Length > 400 ? piece[..400] : piece,
                confidenceScore: 80));
        }

        return list;
    }

    /// <summary>
    /// Linha típica NFS-e: descrição + qtd + R$ unit + R$ total (espaços opcionais / colados pelo PdfPig).
    /// </summary>
    internal static bool TryParseNfseDiscriminacaoRow(string piece, out string desc, out decimal amt)
    {
        desc = string.Empty;
        amt = 0m;
        if (string.IsNullOrWhiteSpace(piece))
            return false;

        piece = piece.Replace('\u00a0', ' ').Trim();
        if (piece.Contains("Descrição", StringComparison.OrdinalIgnoreCase)
            || piece.Contains("Descricao", StringComparison.OrdinalIgnoreCase)
            || piece.Contains("Valor Unit", StringComparison.OrdinalIgnoreCase))
            return false;

        var money = @"(?:\d{1,3}(?:\.\d{3})*,\d{2}|\d+,\d{2})";
        var patterns = new[]
        {
            $@"^(?<desc>.+?)\s+(?<qty>\d{{1,6}})\s+R\$\s*(?<unit>{money})\s*R\$\s*(?<total>{money})\s*$",
            $@"^(?<desc>.+?)(?<qty>\d{{1,6}})\s+R\$\s*(?<unit>{money})\s*R\$\s*(?<total>{money})\s*$",
            $@"^(?<desc>.+?)(?<qty>\d{{1,6}})R\$\s*(?<unit>{money})R\$\s*(?<total>{money})\s*$",
            $@"^(?<desc>.+?)(?<qty>\d{{1,6}})R\$\s*(?<unit>{money})\s*R\$\s*(?<total>{money})\s*$",
            $@"^(?<desc>.+?)(?<amt>{money})\s*$",
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(piece, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
                continue;

            var amtRaw = m.Groups["total"].Success ? m.Groups["total"].Value : m.Groups["amt"].Value;
            if (!TryParseBrazilianMoney(amtRaw, out amt) || amt <= 0m || amt > 500_000m)
                continue;

            var rawDesc = m.Groups["desc"].Value.Trim();
            var repaired = FinancialLineItemSemanticNormalizer.Repair(rawDesc, amt);
            desc = repaired.CleanDescription;
            if (string.IsNullOrWhiteSpace(desc))
                desc = rawDesc;

            return !string.IsNullOrWhiteSpace(desc);
        }

        return false;
    }

    internal static bool IsNfseJunkDescription(string desc)
    {
        if (desc.Length > 240)
            return true;
        var u = desc.ToUpperInvariant();
        return u.Contains("PRESTADOR", StringComparison.Ordinal)
               || u.Contains("TOMADOR", StringComparison.Ordinal)
               || u.Contains("PREFEITURA", StringComparison.Ordinal)
               || u.Contains("ENDEREÇO", StringComparison.Ordinal)
               || u.Contains("ENDERECO", StringComparison.Ordinal);
    }

    internal static decimal? TrySniffNfseServiceTotalBrl(string text)
    {
        var patterns = new[]
        {
            @"Valor\s+L[ií]quido\s+d[oa]\s+Servi[cç]o\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"Valor\s+L[ií]quido\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"Valor\s+Total\s+d[oa]\s+Servi[cç]o\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"Valor\s+Total\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"Valor\s+d[oa]\s+Servi[cç]o\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"Total\s+d[oa]\s+Nota\s+R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success && TryParseBrazilianMoney(m.Groups[1].Value, out var d) && d > 0m)
                return d;
        }

        return null;
    }

    /// <summary>
    /// DANFE (NF-e produto/serviço): PdfPig cola cabeçalho + tabela numa linha; extrai linhas da seção
    /// "DADOS DOS PRODUTOS / SERVIÇOS" (código + descrição + … + UN + qtd + valores).
    /// </summary>
    internal static bool LooksLikeDanfeNfe(string text)
    {
        var u = text.ToUpperInvariant();
        var hasProductTable = u.Contains("DADOS DOS PRODUTOS", StringComparison.Ordinal)
                              || u.Contains("DADOS DO PRODUTO", StringComparison.Ordinal)
                              || u.Contains("DESCRIÇÃO DO PRODUTO", StringComparison.Ordinal)
                              || u.Contains("DESCRICAO DO PRODUTO", StringComparison.Ordinal);
        if (!hasProductTable)
            return false;

        return u.Contains("DANFE", StringComparison.Ordinal)
               || u.Contains("CHAVE DE ACESSO", StringComparison.Ordinal)
               || u.Contains("NATUREZA DA OPERA", StringComparison.Ordinal)
               || u.Contains("VALOR TOTAL DA NOTA", StringComparison.Ordinal)
               || u.Contains("NF-E", StringComparison.Ordinal)
               || (u.Contains("RECEBEMOS DE", StringComparison.Ordinal)
                   && u.Contains("NOTA FISCAL", StringComparison.Ordinal));
    }

    internal static List<ExtractedLineItem> TryExtractDanfeNfeProductLineItems(string text)
    {
        var list = new List<ExtractedLineItem>();
        if (!LooksLikeDanfeNfe(text))
            return list;

        var section = TrySliceDanfeProductsSection(text);
        if (string.IsNullOrWhiteSpace(section))
            return list;

        section = section.Replace('\u00a0', ' ').Trim();
        section = MergeDanfeMultilineProductRows(section);

        var lineNo = 1;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ExtractAllDanfeProductRows(section))
        {
            if (ExcludedFieldPatterns.IsTaxOrAncillaryProductLine(row.description))
                continue;

            var key = $"{row.total:0.00}|{row.description}";
            if (!seen.Add(key))
                continue;

            list.Add(CreateExtractedLineItem(row, lineNo++, confidenceScore: 88));
        }

        if (list.Count >= 1)
            return list;

        foreach (var row in TryExtractLooseDanfeProductRows(section))
        {
            if (ExcludedFieldPatterns.IsTaxOrAncillaryProductLine(row.description))
                continue;

            var key = $"{row.total:0.00}|{row.description}";
            if (!seen.Add(key))
                continue;

            list.Add(CreateExtractedLineItem(row, lineNo++, confidenceScore: 80));
        }

        if (list.Count >= 1)
            return list;

        var complementary = TryExtractDanfeComplementaryDescription(text);
        var noteTotal = TrySniffDanfeNoteTotalBrl(text);
        if (noteTotal is null or <= 0m)
            return list;

        var sectionDesc = TryExtractDanfeProductDescriptionFromSection(section);
        var fallbackDesc = sectionDesc is not null && !ExcludedFieldPatterns.IsOperationalMetadata(sectionDesc)
            ? sectionDesc
            : complementary is not null && !ExcludedFieldPatterns.IsOperationalMetadata(complementary)
                ? complementary
                : "Nota fiscal eletrônica (NF-e)";
        fallbackDesc = CleanDanfeProductDescription(fallbackDesc);
        if (ExcludedFieldPatterns.IsOperationalMetadata(fallbackDesc))
            fallbackDesc = "Nota fiscal eletrônica (NF-e)";
        if (fallbackDesc.Length > 220)
            fallbackDesc = fallbackDesc[..220];

        list.Add(new ExtractedLineItem(
            lineNumber: 1,
            amount: new Money(noteTotal.Value, "BRL"),
            date: null,
            description: fallbackDesc,
            rawLine: fallbackDesc,
            confidenceScore: 72));

        return list;
    }

    internal sealed record TabularProductRow(
        decimal total,
        string description,
        decimal quantity,
        decimal? unitPrice,
        string rawLine,
        string? itemCode = null);

    private static ExtractedLineItem CreateExtractedLineItem(
        TabularProductRow row,
        int lineNumber,
        int confidenceScore) =>
        new(
            lineNumber,
            new Money(row.total, "BRL"),
            null,
            row.description,
            row.rawLine,
            confidenceScore,
            row.quantity,
            row.unitPrice,
            row.itemCode);

    internal static IEnumerable<TabularProductRow> ExtractAllDanfeProductRows(string section)
    {
        section = MergeDanfeMultilineProductRows(section);
        var flat = Regex.Replace(section, @"\s+", " ").Trim();
        // PdfPig cola linhas: "…18,00NVR01…" — inserir quebra antes do próximo código de produto.
        flat = Regex.Replace(
            flat,
            @"(?<=\d{1,3}(?:\.\d{3})*,\d{2})(?=(?:[A-Z]{2,4}\d{2,3}|\d{3,6})(?:\s+)?[A-Za-zÀ-ÿ])",
            "\n",
            RegexOptions.CultureInvariant);

        var results = new List<TabularProductRow>();

        foreach (var rowRx in DanfeProductRowPatterns)
        {
            foreach (Match m in rowRx.Matches(flat))
            {
                if (TryParseDanfeMatch(m, out var row))
                    results.Add(row);
            }

            if (results.Count >= 1)
                return results;
        }

        var tripleValueRows = TryExtractDanfeGluedRetailTripleValueRows(flat).ToList();
        if (tripleValueRows.Count >= 1)
            return tripleValueRows;

        foreach (var piece in SplitDanfeProductPieces(section))
        {
            if (TryParseDanfeProductRow(piece, out var total, out var desc, out var qty, out var unitPrice))
            {
                var rawPiece = piece.Length > 400 ? piece[..400] : piece;
                results.Add(new TabularProductRow(
                    total,
                    desc,
                    qty,
                    unitPrice,
                    rawPiece,
                    ResolveProductSku(desc, rawPiece)));
            }
        }

        return results;
    }

    internal static bool TryParseDanfeMatch(Match m, out TabularProductRow row)
    {
        row = default!;
        if (!m.Success)
            return false;

        var code = m.Groups["code"].Value.Trim();
        if (!DanfeProductCodeRegex.IsMatch(code))
            return false;

        if (!TryParseBrazilianMoney(m.Groups["total"].Value, out var total) || total <= 0m || total > 50_000_000m)
            return false;

        var desc = CleanDanfeProductDescription(m.Groups["desc"].Value);
        if (string.IsNullOrWhiteSpace(desc) || IsDanfeJunkDescription(desc) || ExcludedFieldPatterns.IsOperationalMetadata(desc))
            return false;

        TryParseDanfeQuantity(m.Groups["qty"].Value, out var qty);
        if (qty <= 0m)
            qty = 1m;

        decimal? unitPrice = null;
        if (m.Groups["unit"].Success
            && TryParseBrazilianMoney(m.Groups["unit"].Value, out var unit)
            && unit >= 0m)
        {
            unitPrice = unit;
            if (total > unit * qty * 1.08m && Math.Abs(total - unit * qty) > 0.01m)
                total = Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            unitPrice = Math.Round(total / qty, 4, MidpointRounding.AwayFromZero);
        }

        var repaired = FinancialLineItemSemanticNormalizer.Repair(desc, total);
        desc = string.IsNullOrWhiteSpace(repaired.CleanDescription) ? desc : repaired.CleanDescription;

        var rawLine = m.Value.Length > 400 ? m.Value[..400] : m.Value;
        var itemCode = ResolveProductSku(desc, m.Groups["desc"].Value, rawLine);

        row = new TabularProductRow(
            total,
            desc,
            qty,
            unitPrice,
            rawLine,
            itemCode);
        return true;
    }

    internal static string? ResolveProductSku(params string?[] sources) =>
        ProductCodeHelper.TryExtractFirst(sources);

    internal static readonly Regex DanfeProductCodeRegex = new(
        @"^(?:[A-Z]{2,4}\d{2,3}|\d{3,6})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static readonly Regex[] DanfeProductRowPatterns =
    [
        // Varejo colado (Pichau PdfPig): …1202UN1572,76572,76 ou UN1299,99299,99
        new(
            @"(?<![0-9])(?<code>\d{4,6})(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]*?)(?<ncm>\d{8})\s*(?:\d{1,3}\s*)?(?:\d{2}\s*)?(?<cfop>\d{4})\s*(?:UN(?:ID\.?)?)(?<qty>\d)(?=(?<unit>\d{1,3}(?:\.\d{3})*,\d{2})(?<total>\d{1,3}(?:\.\d{3})*,\d{2}))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // Varejo espaçado: 45074 Gabinete … 84733019 0 00 1202 UN 1 572,76 572,76
        new(
            @"(?<![0-9])(?<code>\d{4,6})\s+(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]+?)\s+(?<ncm>\d{8})\s*(?:\d{1,3}\s*)?(?:\d{2}\s*)?(?<cfop>\d{4})\s+(?:UN(?:ID\.?)?)\s+(?<qty>\d{1,5}(?:,\d{4})?)(?=\s+\d{1,3}(?:\.\d{3})*,\d{2,4})\s+(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})\s+(?<total>\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // Espaços normais: CAM01 Camera … 85258919 000 5102 UN 12,0000 890,0000 10.680,00
        new(
            @"(?<![0-9])(?<code>(?:[A-Z]{2,4}\d{2,3}|\d{3,6}))\s+(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]+?)\s*(?<ncm>\d{8})\s*(?:\d{1,3}\s*)?(?:\d{2}\s*)?(?<cfop>\d{4})\s+(?:UN(?:ID\.?)?)\s+(?<qty>\d{1,5}(?:,\d{4})?)(?=\s+\d{1,3}(?:\.\d{3})*,\d{2,4})\s+(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})\s+(?<total>\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // Colado alfanumérico: CAM01Camera IP…852589190005102UN12,0000890,000010.680,00
        new(
            @"(?<![0-9])(?<code>(?:[A-Z]{2,4}\d{2,3}|\d{3,6}))(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]*?)(?<ncm>\d{8})\s*(?:\d{1,3}\s*)?(?:\d{2}\s*)?(?<cfop>\d{4})\s*(?:UN(?:ID\.?)?)\s*(?<qty>\d{1,5}(?:,\d{4})?)(?=\d{1,3}(?:\.\d{3})*,\d{2,4})\s*(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})\s*(?<total>\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        // Colado numérico: 001Material…732690900005102UN1,00003.500,00003.500,00
        new(
            @"(?<![0-9])(?<code>\d{3,6})(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]*?)(?<ncm>\d{8})\s*(?:\d{1,3}\s*)?(?:\d{2}\s*)?(?<cfop>\d{4})\s*(?:UN(?:ID\.?)?)\s*(?<qty>\d{1,5}(?:,\d{4})?)(?=\d{1,3}(?:\.\d{3})*,\d{2,4})\s*(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})\s*(?<total>\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    internal static bool TryParseDanfeProductRow(
        string piece,
        out decimal total,
        out string description,
        out decimal quantity,
        out decimal? unitPrice)
    {
        total = 0m;
        description = string.Empty;
        quantity = 1m;
        unitPrice = null;

        piece = piece.Trim();
        if (piece.Length < 12)
            return false;

        foreach (var rowRx in DanfeProductRowPatterns)
        {
            var m = rowRx.Match(piece);
            if (!m.Success || !TryParseDanfeMatch(m, out var row))
                continue;

            total = row.total;
            description = row.description;
            quantity = row.quantity;
            unitPrice = row.unitPrice;
            return true;
        }

        return false;
    }

    internal static bool IsDanfeProductTableHeader(string line)
    {
        var u = line.ToUpperInvariant();
        return u.Contains("CÓDIGO PRODUTO", StringComparison.Ordinal)
               || u.Contains("CODIGO PRODUTO", StringComparison.Ordinal)
               || u.Contains("DESCRIÇÃO DO PRODUTO", StringComparison.Ordinal)
               || u.Contains("DESCRICAO DO PRODUTO", StringComparison.Ordinal)
               || u.Contains("VALOR UNIT", StringComparison.Ordinal)
               || u.Contains("NCM/SH", StringComparison.Ordinal);
    }

    /// <summary>Linhas físicas ou blocos colados pelo PdfPig (CAM01…UN…NVR01…).</summary>
    internal static IEnumerable<string> SplitDanfeProductPieces(string section)
    {
        var logicalLines = section
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 8 && !IsDanfeProductTableHeader(l))
            .ToList();

        if (logicalLines.Count >= 2)
            return logicalLines;

        var flat = Regex.Replace(section, @"\s+", " ").Trim();
        if (flat.Length < 20)
            return logicalLines;

        // PdfPig cola várias linhas de produto — segmentar após valor total de linha (…,00) antes do próximo código.
        var segments = Regex.Split(
            flat,
            @"(?<=[0-9]{1,3}(?:\.[0-9]{3})*,[0-9]{2})(?=[A-Z][A-Z0-9]{1,8}[A-Za-zÀ-ÿ])");

        var pieces = segments
            .Select(s => s.Trim())
            .Where(s => s.Length > 12 && !IsDanfeProductTableHeader(s))
            .ToList();

        if (pieces.Count >= 1)
            return pieces;

        return [flat];
    }

    internal static string? TrySliceDanfeProductsSection(string raw)
    {
        var start = -1;
        foreach (var marker in new[]
                 {
                     "DADOS DOS PRODUTOS / SERVIÇOS",
                     "DADOS DOS PRODUTOS / SERVICOS",
                     "DADOS DO PRODUTO/SERVIÇO",
                     "DADOS DO PRODUTO/SERVICO",
                     "DADOS DOS PRODUTOS",
                     "DADOS DO PRODUTO",
                 })
        {
            var i = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            var endPos = i + marker.Length;
            if (endPos > start)
                start = endPos;
        }

        if (start < 0)
            return null;

        var end = raw.Length;
        foreach (var endMarker in new[]
                 {
                     "RECEBEMOS DE",
                     "NATUREZA DA OPERA",
                     "NATUREZA DA OPERACAO",
                     "VALOR TOTAL DA NOTA",
                     "DADOS ADICIONAIS",
                     "INFORMAÇÕES COMPLEMENTARES",
                     "INFORMACOES COMPLEMENTARES",
                     "RESERVADO AO FISCO",
                     "TRANSPORTADOR / VOLUMES",
                 })
        {
            var j = raw.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
            if (j >= 0)
                end = Math.Min(end, j);
        }

        if (end <= start)
            return null;

        var section = raw[start..end].Trim();
        // Remove cabeçalhos de coluna colados pelo PdfPig até o primeiro código de produto (CAM01, 45074, …).
        section = Regex.Replace(
            section,
            @"^\s*.*?(?=(?:[A-Z]{2,4}\d{2,3}|\d{3,6})(?:\s+)?[A-Za-zÀ-ÿ])",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return section.Trim();
    }

    internal static string? TryExtractDanfeComplementaryDescription(string text)
    {
        var m = Regex.Match(
            text,
            @"Inf\.?\s*Contribuinte:\s*(?<info>.+?)(?:Valor\s+Aproximado|RESERVADO\s+AO\s+FISCO|Impresso\s+em|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return null;

        var info = Regex.Replace(m.Groups["info"].Value, @"\s+", " ").Trim().TrimEnd('.');
        return info.Length >= 8 ? info : null;
    }

    internal static string? TryExtractDanfeNaturezaOperacao(string text)
    {
        var m = Regex.Match(
            text,
            @"NATUREZA\s+DA\s+OPERA[ÇC][AÃ]O\s*(?<op>.+?)(?:PROTOCOLO|INSCRI[ÇC][AÃ]O\s+ESTADUAL|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return null;

        var op = Regex.Replace(m.Groups["op"].Value, @"\s+", " ").Trim();
        return op.Length >= 6 ? op : null;
    }

    /// <summary>Chave de acesso NF-e (44 dígitos) após o rótulo DANFE.</summary>
    internal static string? TryExtractNfeAccessKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var u = text.ToUpperInvariant();
        var idx = u.IndexOf("CHAVE DE ACESSO", StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var slice = text[idx..];
        var digits = new string(slice.Where(char.IsDigit).Take(44).ToArray());
        return digits.Length == 44 ? digits : null;
    }

    internal static string? TryExtractDanfeNfeNumber(string text)
    {
        var m = Regex.Match(
            text,
            @"NF-?e\s*N[ºo°\.]*\s*(?<num>\d{1,9})",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        var num = m.Groups["num"].Value.Trim();
        return num.Length > 0 ? num.PadLeft(9, '0') : null;
    }

    internal static string? TryExtractDanfeNfeSeries(string text)
    {
        var m = Regex.Match(
            text,
            @"S[ée]rie\s*(?<ser>\d{1,3})",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        return m.Groups["ser"].Value.Trim();
    }

    internal static string? TryExtractDanfeIssuerTaxId(string text)
    {
        var m = Regex.Match(
            text,
            @"CNPJ\s*/\s*CPF\s*(?<doc>[\d\.\-/]+)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        var digits = new string(m.Groups["doc"].Value.Where(char.IsDigit).ToArray());
        return digits.Length is 11 or 14 ? digits : null;
    }

    internal static string? TryBuildNfeFallbackCompositeKey(string? issuerTaxId, string? nfeNumber, string? nfeSeries)
    {
        if (string.IsNullOrWhiteSpace(issuerTaxId) || string.IsNullOrWhiteSpace(nfeNumber))
            return null;

        var series = string.IsNullOrWhiteSpace(nfeSeries) ? "0" : nfeSeries.Trim();
        return $"{issuerTaxId.Trim()}:{nfeNumber.Trim()}:{series}";
    }

    internal static bool TryParseDanfeQuantity(string raw, out decimal qty)
    {
        qty = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim().Replace('\u00a0', ' ');
        // NF-e: "12,0000" ou "1,0000" ou quantidade inteira "1"
        if (decimal.TryParse(s.Replace(".", "").Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out qty))
            return qty > 0m;

        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asInt)
               && asInt > 0
               && (qty = asInt) > 0m;
    }

    internal static decimal? TrySniffDanfeNoteTotalBrl(string text)
    {
        var patterns = new[]
        {
            @"V\.\s*TOTAL\s+DA\s+NOTA\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"VALOR\s+TOTAL\s+DA\s+NOTA\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"VALOR\s+TOTAL:\s*R\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
            @"V\.\s+TOTAL\s+PRODUTOS\s*(\d{1,3}(?:\.\d{3})*,\d{2})",
        };

        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success && TryParseBrazilianMoney(m.Groups[1].Value, out var d) && d > 0m)
                return d;
        }

        return null;
    }

    internal static string CleanDanfeProductDescription(string desc)
    {
        desc = Regex.Replace(desc.Replace('\u00a0', ' '), @"\s+", " ").Trim();
        desc = Regex.Replace(desc, @"Garantia\s+\d+\s+meses", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        desc = Regex.Replace(
            desc,
            @"Nr\.?\s*Serie\s*:?\s*.+?(?=\d{8}(?:0+\s*)?(?:\d{2}\s*)?\d{4}\s*(?:UN(?:ID\.?)?))",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        desc = Regex.Replace(desc, @"(?<sku>PG-[A-Z0-9\-]+)(?:\s*\1)+", "${sku}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        desc = Regex.Replace(desc, @"DEELEVADORES", "DE ELEVADORES", RegexOptions.IgnoreCase);
        desc = Regex.Replace(desc, @"DEMAIO", "DE MAIO", RegexOptions.IgnoreCase);
        desc = Regex.Replace(desc, @"\s*/\s*", " / ");
        desc = Regex.Replace(desc, @"\s{2,}", " ").Trim().TrimEnd(',', '.');
        if (desc.Length > 120)
            desc = desc[..120].TrimEnd();
        return desc;
    }

    /// <summary>NF-e varejo colado: UN1 + sequência de valores (unit, base, total c/ IPI).</summary>
    internal static IEnumerable<TabularProductRow> TryExtractDanfeGluedRetailTripleValueRows(string flat)
    {
        flat = Regex.Replace(flat, @"Garantia\s+\d+\s+meses", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        flat = Regex.Replace(
            flat,
            @"Nr\.?\s*Serie\s*:?\s*.+?(?=\d{8}(?:0+\s*)?(?:\d{2}\s*)?\d{4}\s*(?:UN(?:ID\.?)?))",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var headerRx = new Regex(
            @"(?<![0-9])(?<code>\d{4,6})(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,:\-+]*?)(?<ncm>\d{8})(?=(?:0+\s*)?(?:\d{2}\s*)?\d{4}\s*(?:UN(?:ID\.?)?))(?:0+\s*)?(?:\d{2}\s*)?\d{4}\s*(?:UN(?:ID\.?)?)(?<qty>\d)(?=\d{1,3}(?:\.\d{3})*,\d{2})(?<rest>.+?)(?=RECEBEMOS|NATUREZA|DADOS ADICIONAIS|RESERVADO AO FISCO|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match m in headerRx.Matches(flat))
        {
            var code = m.Groups["code"].Value.Trim();
            if (!DanfeProductCodeRegex.IsMatch(code))
                continue;

            var desc = CleanDanfeProductDescription(m.Groups["desc"].Value);
            if (string.IsNullOrWhiteSpace(desc) || IsDanfeJunkDescription(desc) || ExcludedFieldPatterns.IsOperationalMetadata(desc))
                continue;

            TryParseDanfeQuantity(m.Groups["qty"].Value, out var qty);
            if (qty <= 0m)
                qty = 1m;

            var tokens = ExtractBrazilianMoneyTokens(m.Groups["rest"].Value);
            if (tokens.Count < 2)
                continue;

            if (tokens.Count == 2 && Math.Abs(tokens[1] - tokens[0]) <= tokens[0] * 0.02m)
                continue;

            var unitPrice = tokens[0];
            var lineTotal = ResolveDanfeProductLineTotal(tokens, qty);
            var repaired = FinancialLineItemSemanticNormalizer.Repair(desc, lineTotal);
            desc = string.IsNullOrWhiteSpace(repaired.CleanDescription) ? desc : repaired.CleanDescription;

            var rawLine = m.Value.Length > 400 ? m.Value[..400] : m.Value;
            yield return new TabularProductRow(
                lineTotal,
                desc,
                qty,
                unitPrice,
                rawLine,
                ResolveProductSku(desc, m.Groups["desc"].Value, rawLine));
        }
    }

    internal static List<decimal> ExtractBrazilianMoneyTokens(string text)
    {
        var tokens = new List<decimal>();
        foreach (Match money in Regex.Matches(text, @"\d{1,3}(?:\.\d{3})*,\d{2}", RegexOptions.CultureInvariant))
        {
            if (TryParseBrazilianMoney(money.Value, out var amount) && amount > 0m)
                tokens.Add(amount);
        }

        return tokens;
    }

    internal static decimal ResolveDanfeProductLineTotal(IReadOnlyList<decimal> tokens, decimal qty)
    {
        if (tokens.Count == 0)
            return 0m;

        var unit = tokens[0];
        if (tokens.Count == 1)
            return Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);

        if (Math.Abs(tokens[1] - unit) <= unit * 0.02m)
            return Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);

        if (tokens[1] > unit * 2m)
            return Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);

        if (tokens.Count >= 3 && tokens[2] > unit * 1.05m)
            return Math.Round(unit * qty, 2, MidpointRounding.AwayFromZero);

        return Math.Round(tokens[1], 2, MidpointRounding.AwayFromZero);
    }

    internal static string? TryExtractDanfeProductDescriptionFromSection(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return null;

        var m = Regex.Match(
            section,
            @"(?<![0-9])(?:[A-Z]{2,4}\d{2,3}|\d{4,6})(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,:\-+]{8,}?)(?<ncm>\d{8})(?=(?:0+\s*)?(?:\d{2}\s*)?\d{4}\s*(?:UN(?:ID\.?)?))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
            return null;

        var desc = CleanDanfeProductDescription(m.Groups["desc"].Value);
        if (desc.Length < 8 || IsDanfeJunkDescription(desc) || ExcludedFieldPatterns.IsOperationalMetadata(desc))
            return null;

        return desc;
    }

    /// <summary>Junta linha código+descrição com linha NCM/valores (PdfPig quebra a tabela).</summary>
    internal static string MergeDanfeMultilineProductRows(string section)
    {
        var lines = section
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !IsDanfeProductTableHeader(l))
            .ToList();

        if (lines.Count <= 1)
            return section;

        var merged = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (Regex.IsMatch(line, @"^PG-[A-Z0-9]+-[A-Z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && merged.Count > 0
                && i + 1 < lines.Count
                && Regex.IsMatch(lines[i + 1], @"^\d{8}\b", RegexOptions.CultureInvariant))
            {
                merged[^1] = $"{merged[^1]} {line} {lines[i + 1]}";
                i++;
                continue;
            }

            if (i + 1 < lines.Count
                && Regex.IsMatch(line, @"^(?:[A-Z]{2,4}\d{2,3}|\d{4,6})\s+[A-Za-zÀ-ÿ]", RegexOptions.CultureInvariant)
                && Regex.IsMatch(lines[i + 1], @"^\d{8}\b", RegexOptions.CultureInvariant))
            {
                merged.Add($"{line} {lines[i + 1]}");
                i++;
            }
            else
            {
                merged.Add(line);
            }
        }

        return string.Join('\n', merged);
    }

    internal static IEnumerable<TabularProductRow> TryExtractLooseDanfeProductRows(string section)
    {
        var flat = Regex.Replace(MergeDanfeMultilineProductRows(section), @"\s+", " ").Trim();
        if (flat.Length < 20)
            yield break;

        var loose = new Regex(
            @"(?<![0-9])(?<code>(?:[A-Z]{2,4}\d{2,3}|\d{4,6}))\s*(?<desc>[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ0-9\s/.,\-+]{4,200}?)(?<ncm>\d{8})\s*(?:\d[\d\s]{0,8})?(?<cfop>\d{4})\s*(?:UN(?:ID\.?)?)\.?\s*(?<qty>\d{1,5}(?:,\d{4})?)\s+(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})\s+(?<total>\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match m in loose.Matches(flat))
        {
            if (!TryParseDanfeMatch(m, out var row))
                continue;
            yield return row;
        }
    }


    internal static bool IsDanfeJunkDescription(string desc)
    {
        if (desc.Length > 240)
            return true;
        var u = desc.ToUpperInvariant();
        if (u.Contains("RECEBEMOS DE", StringComparison.Ordinal)
            || u.Contains("DANFE", StringComparison.Ordinal)
            || u.Contains("CHAVE DE ACESSO", StringComparison.Ordinal)
            || u.Contains("IDENTIFICAÇÃO DO EMITENTE", StringComparison.Ordinal)
            || u.Contains("IDENTIFICACAO DO EMITENTE", StringComparison.Ordinal))
            return true;

        if (ExcludedFieldPatterns.IsOperationalMetadata(desc))
            return true;

        // Vazamento de colunas UN/qtd/valores no campo descrição (PdfPig colado).
        if (Regex.IsMatch(desc, @"\bUN\s*\d+,\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;
        if (Regex.Matches(desc, @"\d{1,3}(?:\.\d{3})*,\d{2}").Count >= 2)
            return true;

        return false;
    }

    internal static bool IsDanfeHeaderBlob(string line)
    {
        if (line.Length < 160)
            return false;
        var u = line.ToUpperInvariant();
        return u.Contains("RECEBEMOS DE", StringComparison.Ordinal)
               && (u.Contains("DANFE", StringComparison.Ordinal) || u.Contains("NF-E", StringComparison.Ordinal));
    }

    /// <summary>
    /// PDFs como <c>relatorio_condominio.pdf</c>: PdfPig junta a tabela numa única linha (categoria+descrição+valor).
    /// Quando há cabeçalho <c>Valor (R$)</c>, usamos extração compacta mesmo com muitas quebras (evita alternar para
    /// <see cref="ParseLineItems"/> e resultados inconsistentes entre uploads).
    /// </summary>
    internal static bool ShouldUseCompactCondominioExtraction(string rawText, DocumentType resolvedType)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        var u = rawText.ToUpperInvariant();
        if (!u.Contains("CONDOM", StringComparison.Ordinal) && !u.Contains("CONDOMINIO", StringComparison.Ordinal))
            return false;

        if (!u.Contains("RELAT", StringComparison.Ordinal)
            && !u.Contains("GASTO", StringComparison.Ordinal)
            && !u.Contains("DESPESA", StringComparison.Ordinal))
            return false;

        var moneyCount = Regex.Matches(rawText, @"\d{1,3}(?:\.\d{3})*,\d{2}").Count;
        if (moneyCount < 4)
            return false;

        if (HasCompactCondominioTableHeader(rawText))
            return true;

        var logicalLines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        return logicalLines <= 6;
    }

    /// <summary>Cabeçalho típico antes das linhas coladas <c>ManutençãoReparo…2.500,00</c>.</summary>
    internal static bool HasCompactCondominioTableHeader(string rawText)
    {
        var u = rawText.ToUpperInvariant();
        return u.Contains("VALOR (R$)", StringComparison.Ordinal)
               || u.Contains("VALOR(R$)", StringComparison.Ordinal)
               || u.Contains("CATEGORIADESCRI", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extrai linhas &quot;CategoriaDescriçãoValor&quot; coladas (relatórios de gastos).
    /// </summary>
    internal static List<ExtractedLineItem> ExtractCompactCondominioExpenseRows(string rawText)
    {
        // Sem ancoragem, o primeiro "(Manutenção|Serviços|…)" pode ser a palavra "manutenção" no parágrafo introdutório
        // e o valor o da primeira linha da tabela → uma linha gigante ("…CategoriaDescriçãoValor…").
        var scanText = TrySliceAfterTableHeader(rawText) ?? rawText;

        var list = new List<ExtractedLineItem>();
        // Categorias típicas de relatório fictício/real; ordem do grupo alternativo evita falso positivo no meio do texto.
        var rx = new Regex(
            @"(Manutenção|Manutencao|Serviços|Servicos|Utilidades|Administrativo|Outros)(.+?)(\d{1,3}(?:\.\d{3})*,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var lineNo = 1;
        foreach (Match m in rx.Matches(scanText))
        {
            if (!m.Success || m.Groups.Count < 4)
                continue;

            var cat = m.Groups[1].Value.Trim();
            var desc = m.Groups[2].Value.Trim();
            var amtRaw = m.Groups[3].Value;

            if (desc.Contains("total", StringComparison.OrdinalIgnoreCase)
                && desc.Contains("gasto", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsGluedCondominioJunkDescription(desc))
                continue;

            if (!TryParseBrazilianMoney(amtRaw, out var amt))
                continue;

            var fullDesc = $"{cat} — {desc}".Trim();
            list.Add(new ExtractedLineItem(
                lineNumber: lineNo++,
                amount: new Money(amt, "BRL"),
                date: null,
                description: fullDesc,
                rawLine: m.Value.Trim(),
                confidenceScore: 75));
        }

        return list;
    }

    /// <summary>Recorta o texto a partir do cabeçalho da tabela para o regex não “comer” o parágrafo inicial.</summary>
    internal static string? TrySliceAfterTableHeader(string rawText)
    {
        var markers = new[]
        {
            "Valor (R$)", "Valor(R$)", "VALOR (R$)", "Valor( R$ )",
            "CategoriaDescriçãoValor (R$)", "CategoriaDescricaoValor (R$)"
        };
        foreach (var mk in markers)
        {
            var idx = rawText.IndexOf(mk, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return rawText.AsSpan(idx + mk.Length).ToString();
        }

        return null;
    }

    /// <summary>Rejeita capturas que colaram parágrafo + cabeçalho de colunas + linha da tabela.</summary>
    internal static bool IsGluedCondominioJunkDescription(string desc)
    {
        if (desc.Length > 140)
            return true;

        var u = desc.ToUpperInvariant();
        if (u.Contains("CATEGORIADESCRI", StringComparison.Ordinal))
            return true;
        if (u.Contains("REFERENTES AO MÊS", StringComparison.Ordinal) || u.Contains("REFERENTES AO MES", StringComparison.Ordinal))
            return true;
        if (u.Contains("VALOR (R$)", StringComparison.Ordinal))
            return true;

        return false;
    }

    internal static DocumentType ResolveDocumentType(DocumentType fromExtension, string rawText)
    {
        var fromContent = DetectDocumentType(rawText);
        if (fromExtension == DocumentType.Desconhecido)
            return fromContent;

        // PDF e imagens assumem NotaFiscal por extensão; conteúdo pode ser relatório / demonstrativo.
        if (fromExtension == DocumentType.NotaFiscal &&
            fromContent != DocumentType.Desconhecido &&
            fromContent != DocumentType.NotaFiscal)
            return fromContent;

        return fromExtension;
    }

    internal static List<ExtractedLineItem> ParseLineItems(string rawText, DocumentType docType)
    {
        var lineItems = new List<ExtractedLineItem>();
        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine) || IsHeaderLine(trimmedLine, docType))
                continue;

            if (IsDanfeHeaderBlob(trimmedLine))
                continue;

            var amount = ExtractAmount(trimmedLine);
            var date = ExtractDate(trimmedLine);
            var description = ExtractDescription(trimmedLine, amount, date);

            if (amount is null && !date.HasValue && !LooksLikeExpenseRow(trimmedLine))
                continue;

            var lineItem = new ExtractedLineItem(
                lineNumber: index + 1,
                amount: amount,
                date: date,
                description: description,
                rawLine: trimmedLine,
                confidenceScore: CalculateConfidence(amount, date, description));

            if (lineItem.HasValidData())
                lineItems.Add(lineItem);
        }

        return lineItems;
    }

    /// <summary>
    /// Linhas só com texto livre (sem valor/data) só entram se parecerem linha de tabela ou lista de despesa.
    /// </summary>
    internal static bool LooksLikeExpenseRow(string line)
    {
        if (line.Contains('\t'))
            return true;
        // Duplo espaço ou mais costuma separar colunas em PDFs textualizados
        return Regex.IsMatch(line, @"\s{2,}\S");
    }

    internal static bool LineHasMonetaryCandidate(string line)
    {
        if (line.Contains("R$", StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(line, @"\d{1,3}(?:\.\d{3})*(?:,\d{2})\b")
               || Regex.IsMatch(line, @"\d+,\d{2}\b");
    }

    internal static bool IsHeaderLine(string line, DocumentType docType)
    {
        if (LineHasMonetaryCandidate(line))
            return false;

        var upper = line.ToUpperInvariant();

        var strongHeaders = new[]
        {
            "NOTA FISCAL", "NF-E", "NF E", "NFE ", " DANFE", "CABEÇALHO", "CABECALHO"
        };
        if (strongHeaders.Any(k => upper.Contains(k, StringComparison.Ordinal)))
            return true;

        var summaryOnly = new[]
        {
            "SUBTOTAL", "TOTAL GERAL", "SOMA GERAL", "RESUMO FINANCEIRO", "VALOR TOTAL DO DOCUMENTO"
        };
        if (summaryOnly.Any(k => upper.Contains(k, StringComparison.Ordinal)))
            return true;

        var weakCols = new[]
        {
            "DATA", "DESCRIÇÃO", "DESCRICAO", "VALOR", "CONTA", "HISTÓRICO", "HISTORICO",
            "FORNECEDOR", "VENCIMENTO", "DOCUMENTO"
        };
        var weakHits = weakCols.Count(k => upper.Contains(k, StringComparison.Ordinal));
        if (weakHits >= 2 && line.Length < 140)
            return true;

        return false;
    }

    internal static Money? ExtractAmount(string line)
    {
        var patterns = new[]
        {
            @"R\$\s*([\d\.\s]+,\d{2})\b",
            @"R\$\s*([\d\.]+)\b(?!\s*,\d{2})",
            @"\b(\d{1,3}(?:\.\d{3})+(?:,\d{2}))\b",
            // Evita casar só "234,56" dentro de "1.234,56"
            @"(?<![\d.])(\d+,\d{2})\b"
        };

        Money? last = null;
        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(line, pattern, RegexOptions.IgnoreCase))
            {
                var raw = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                raw = raw.Replace(" ", "", StringComparison.Ordinal);
                if (TryParseBrazilianMoney(raw, out var amount))
                    last = new Money(amount, "BRL");
            }
        }

        return last;
    }

    internal static bool TryParseBrazilianMoney(string raw, out decimal amount)
    {
        amount = 0;
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw))
            return false;

        var hasComma = raw.Contains(',');
        var hasDot = raw.Contains('.');
        string normalized;
        if (hasComma && hasDot)
            normalized = raw.Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        else if (hasComma && !hasDot)
            normalized = raw.Replace(",", ".", StringComparison.Ordinal);
        else if (!hasComma && hasDot)
        {
            var parts = raw.Split('.');
            if (parts.Length > 1 && parts[^1].Length == 3 && parts[^1].All(char.IsDigit))
                normalized = raw.Replace(".", "", StringComparison.Ordinal);
            else
                normalized = raw;
        }
        else
            normalized = raw;

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
               && amount > 0;
    }

    internal static DateTime? ExtractDate(string line)
    {
        var patterns = new[]
        {
            @"\b(\d{2}[-/]\d{2}[-/]\d{4})\b",
            @"\b(\d{4}[-/]\d{2}[-/]\d{2})\b",
            @"\b(\d{2}[-/]\d{2}[-/]\d{2})\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(line, pattern);
            if (match.Success && DateTime.TryParse(match.Value, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var date))
                return date;
        }

        return null;
    }

    internal static string ExtractDescription(string line, Money? amount, DateTime? date)
    {
        var total = amount?.Amount ?? 0m;
        var cleanedLine = FinancialLineItemSemanticNormalizer.Repair(line.Trim(), total).CleanDescription;
        cleanedLine = Regex.Replace(cleanedLine, @"R\$\s*[\d\.\s]+,\d{2}", " ", RegexOptions.IgnoreCase);
        cleanedLine = Regex.Replace(cleanedLine, @"R\$\s*[\d\.]+", " ", RegexOptions.IgnoreCase);

        if (amount != null)
        {
            var amountStr = amount.Amount.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));
            cleanedLine = cleanedLine.Replace(amountStr, "", StringComparison.OrdinalIgnoreCase);
            var invariant = amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            cleanedLine = cleanedLine.Replace(invariant, "", StringComparison.OrdinalIgnoreCase);
            var pt = amount.Amount.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            cleanedLine = cleanedLine.Replace(pt, "", StringComparison.OrdinalIgnoreCase);
        }

        if (date.HasValue)
        {
            cleanedLine = cleanedLine.Replace(date.Value.ToString("dd/MM/yyyy"), "", StringComparison.OrdinalIgnoreCase);
            cleanedLine = cleanedLine.Replace(date.Value.ToString("yyyy-MM-dd"), "", StringComparison.OrdinalIgnoreCase);
        }

        cleanedLine = Regex.Replace(cleanedLine, @"[^a-zA-Z0-9À-ÿ\s\-/]", " ");
        cleanedLine = Regex.Replace(cleanedLine, @"\s+", " ").Trim();
        if (cleanedLine.Length > 500)
            cleanedLine = cleanedLine[..500];

        return cleanedLine;
    }

    internal static int CalculateConfidence(Money? amount, DateTime? date, string description)
    {
        var confidence = 0;
        if (amount != null) confidence += 40;
        if (date.HasValue) confidence += 30;
        if (!string.IsNullOrWhiteSpace(description)) confidence += 30;
        return confidence;
    }

    internal static bool LooksLikeCondominiumExpenseReport(string upperText)
    {
        var reportCue =
            upperText.Contains("RELATÓRIO", StringComparison.Ordinal)
            || upperText.Contains("RELATORIO", StringComparison.Ordinal)
            || upperText.Contains("PRESTAÇÃO DE CONTAS", StringComparison.Ordinal)
            || upperText.Contains("PRESTACAO DE CONTAS", StringComparison.Ordinal)
            || upperText.Contains("DEMONSTRATIVO", StringComparison.Ordinal)
            || upperText.Contains("RATEIO", StringComparison.Ordinal);

        if (!reportCue)
            return false;

        var expenseCue =
            upperText.Contains("DESPESA", StringComparison.Ordinal)
            || upperText.Contains("CONDOM", StringComparison.Ordinal);

        return expenseCue
               || upperText.Contains("SÍNDICO", StringComparison.Ordinal)
               || upperText.Contains("SINDICO", StringComparison.Ordinal);
    }

    internal static DocumentType DetectDocumentType(string rawText)
    {
        var upperText = rawText.ToUpperInvariant();

        if (upperText.Contains("NOTA FISCAL", StringComparison.Ordinal)
            || upperText.Contains("NF-E", StringComparison.Ordinal)
            || upperText.Contains("NFE ", StringComparison.Ordinal))
            return DocumentType.NotaFiscal;

        if (LooksLikeCondominiumExpenseReport(upperText))
            return DocumentType.Balancete;

        if (upperText.Contains("BALANCETE", StringComparison.Ordinal) || upperText.Contains("BALANÇO", StringComparison.Ordinal))
            return DocumentType.Balancete;
        if (upperText.Contains("RECIBO", StringComparison.Ordinal) || upperText.Contains("RECEBEMOS", StringComparison.Ordinal))
            return DocumentType.Recibo;
        if (upperText.Contains("CONTRATO", StringComparison.Ordinal) || upperText.Contains("CONTRATUAL", StringComparison.Ordinal))
            return DocumentType.Contrato;
        if (upperText.Contains("BOLETO", StringComparison.Ordinal) || upperText.Contains("CÓDIGO DE BARRAS", StringComparison.Ordinal))
            return DocumentType.Boleto;

        return DocumentType.Desconhecido;
    }
}
