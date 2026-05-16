using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Simcag.Shared.Finance;

namespace Simcag.IngestionService.Application.UseCases;

public class ParseDocumentUseCase : IParseDocumentUseCase
{
    private readonly ILogger<ParseDocumentUseCase> _logger;

    public ParseDocumentUseCase(ILogger<ParseDocumentUseCase> logger)
    {
        _logger = logger;
    }

    public ParseDocumentResult Execute(string rawText, DocumentType documentType)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        // CNPJ/CPF com pontos ou blocos "12 345 678 0001-90" casavam como "valores" milhar (ExtractAmount).
        var sanitized = MaskBrazilianTaxIds(rawText);

        var resolvedType = ResolveDocumentType(documentType, sanitized);

        List<ExtractedLineItem> lineItems;
        var nfseItems = TryExtractGluedNfseDiscriminacaoLineItems(sanitized);
        if (nfseItems.Count >= 1)
        {
            lineItems = nfseItems;
        }
        else
        {
            var gluedInvoice = TryExtractGluedCondominioInvoiceLineItems(sanitized);
            if (gluedInvoice.Count >= 2)
            {
                lineItems = gluedInvoice;
            }
            else if (ShouldUseCompactCondominioExtraction(sanitized, resolvedType))
            {
                var compact = ExtractCompactCondominioExpenseRows(sanitized);
                lineItems = compact.Count >= 3 ? compact : ParseLineItems(sanitized, resolvedType);
            }
            else
                lineItems = ParseLineItems(sanitized, resolvedType);
        }

        _logger.LogDebug(
            "Parsing concluído: {LineCount} itens | tipo resolvido: {DocType}",
            lineItems.Count,
            resolvedType);

        return new ParseDocumentResult(lineItems, resolvedType);
    }

    /// <summary>
    /// Remove padrões de CNPJ/CPF para não serem interpretados como montantes (ex.: <c>12.345.678</c> dentro de CNPJ).
    /// </summary>
    private static string MaskBrazilianTaxIds(string input)
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
    private static bool LooksLikeGluedCondominioInvoice(string text)
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

    private static string? TrySliceCondominioChargesSection(string raw)
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

    private static string TrimSectionBeforeMarkers(string section, params string[] markers)
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

    private static List<ExtractedLineItem> TryExtractGluedCondominioInvoiceLineItems(string text)
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
    private static bool LooksLikeGluedNfsePrefeitura(string text)
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

    private static List<ExtractedLineItem> TryExtractGluedNfseDiscriminacaoLineItems(string text)
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
        if (fromRows.Count > 0)
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

    private static string StripNfseDiscriminacaoColumnHeaders(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        // PdfPig cola "…Valor TotalTaxa…" (sem espaço após Total) — não usar \b após "Total".
        s = Regex.Replace(s, @"^\s*.*?\bValor\s*Total", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    private static List<ExtractedLineItem> ExtractNfseDiscriminacaoValueRows(string section)
    {
        var list = new List<ExtractedLineItem>();
        // Itens colados: "…450,50Manutenção…" — partir após centavos antes da próxima palavra capitalizada.
        var pieces = Regex
            .Split(section, @"(?<=,\d{2})(?=[A-ZÀ-ÿ][a-zà-ÿ])")
            .Select(p => p.Trim())
            .Where(p => p.Length > 4)
            .ToList();

        var lineRx = new Regex(
            @"^(?<desc>.+?)(?<amt>(?:\d{1,3}(?:\.\d{3})+,\d{2})|(?:\d{1,6},\d{2}))$",
            RegexOptions.CultureInvariant);
        var lineNo = 1;
        foreach (var piece in pieces)
        {
            var m = lineRx.Match(piece);
            if (!m.Success)
                continue;
            var desc = m.Groups["desc"].Value.Trim();
            var amtRaw = m.Groups["amt"].Value;
            if (IsNfseJunkDescription(desc))
                continue;
            if (!TryParseBrazilianMoney(amtRaw, out var amt) || amt <= 0m || amt > 500_000m)
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

    private static bool IsNfseJunkDescription(string desc)
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

    private static decimal? TrySniffNfseServiceTotalBrl(string text)
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
    /// PDFs como <c>relatorio_condominio.pdf</c>: PdfPig junta a tabela numa única linha (categoria+descrição+valor).
    /// Quando há cabeçalho <c>Valor (R$)</c>, usamos extração compacta mesmo com muitas quebras (evita alternar para
    /// <see cref="ParseLineItems"/> e resultados inconsistentes entre uploads).
    /// </summary>
    private static bool ShouldUseCompactCondominioExtraction(string rawText, DocumentType resolvedType)
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
    private static bool HasCompactCondominioTableHeader(string rawText)
    {
        var u = rawText.ToUpperInvariant();
        return u.Contains("VALOR (R$)", StringComparison.Ordinal)
               || u.Contains("VALOR(R$)", StringComparison.Ordinal)
               || u.Contains("CATEGORIADESCRI", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extrai linhas &quot;CategoriaDescriçãoValor&quot; coladas (relatórios de gastos).
    /// </summary>
    private static List<ExtractedLineItem> ExtractCompactCondominioExpenseRows(string rawText)
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
    private static string? TrySliceAfterTableHeader(string rawText)
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
    private static bool IsGluedCondominioJunkDescription(string desc)
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

    private static DocumentType ResolveDocumentType(DocumentType fromExtension, string rawText)
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

    private List<ExtractedLineItem> ParseLineItems(string rawText, DocumentType docType)
    {
        var lineItems = new List<ExtractedLineItem>();
        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var (line, index) in lines.Select((l, i) => (l, i)))
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine) || IsHeaderLine(trimmedLine, docType))
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
    private static bool LooksLikeExpenseRow(string line)
    {
        if (line.Contains('\t'))
            return true;
        // Duplo espaço ou mais costuma separar colunas em PDFs textualizados
        return Regex.IsMatch(line, @"\s{2,}\S");
    }

    private static bool LineHasMonetaryCandidate(string line)
    {
        if (line.Contains("R$", StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(line, @"\d{1,3}(?:\.\d{3})*(?:,\d{2})\b")
               || Regex.IsMatch(line, @"\d+,\d{2}\b");
    }

    private static bool IsHeaderLine(string line, DocumentType docType)
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

    private static Money? ExtractAmount(string line)
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

    private static bool TryParseBrazilianMoney(string raw, out decimal amount)
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

    private static DateTime? ExtractDate(string line)
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

    private static string ExtractDescription(string line, Money? amount, DateTime? date)
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

    private static int CalculateConfidence(Money? amount, DateTime? date, string description)
    {
        var confidence = 0;
        if (amount != null) confidence += 40;
        if (date.HasValue) confidence += 30;
        if (!string.IsNullOrWhiteSpace(description)) confidence += 30;
        return confidence;
    }

    private static bool LooksLikeCondominiumExpenseReport(string upperText)
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

    private static DocumentType DetectDocumentType(string rawText)
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
