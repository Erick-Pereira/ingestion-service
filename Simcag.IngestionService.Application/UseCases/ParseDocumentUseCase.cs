using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;

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

        var resolvedType = ResolveDocumentType(documentType, rawText);

        List<ExtractedLineItem> lineItems;
        if (ShouldUseCompactCondominioExtraction(rawText, resolvedType))
        {
            var compact = ExtractCompactCondominioExpenseRows(rawText);
            lineItems = compact.Count >= 3 ? compact : ParseLineItems(rawText, resolvedType);
        }
        else
            lineItems = ParseLineItems(rawText, resolvedType);

        _logger.LogDebug(
            "Parsing concluído: {LineCount} itens | tipo resolvido: {DocType}",
            lineItems.Count,
            resolvedType);

        return new ParseDocumentResult(lineItems, resolvedType);
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
               || Regex.IsMatch(line, @"\d+,\d{2}\b")
               || Regex.IsMatch(line, @"\b\d{1,3}(?:\.\d{3})+\b");
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
            @"(?<![\d.])(\d+,\d{2})\b",
            // Milhares sem centavos: não capturar "1.234" se a linha continua ",56"
            @"\b(\d{1,3}(?:\.\d{3})+)\b(?!,\d{2})"
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
        var cleanedLine = line;
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

        cleanedLine = Regex.Replace(cleanedLine, @"[^a-zA-Z0-9À-ÿ\s\-]", " ");
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
