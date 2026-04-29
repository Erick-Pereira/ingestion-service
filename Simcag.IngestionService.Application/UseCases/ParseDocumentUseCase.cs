using Simcag.IngestionService.Domain.Entities;
using Simcag.IngestionService.Domain.Enums;
using Simcag.IngestionService.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

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

        var lineItems = ParseLineItems(rawText, documentType);
        var resolvedType = documentType == DocumentType.Desconhecido
            ? DetectDocumentType(rawText)
            : documentType;

        _logger.LogDebug(
            "Parsing concluído: {LineCount} itens | tipo resolvido: {DocType}",
            lineItems.Count,
            resolvedType);

        return new ParseDocumentResult(lineItems, resolvedType);
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

    private static bool IsHeaderLine(string line, DocumentType docType)
    {
        var headerKeywords = new[]
        {
            "NOTA FISCAL", "CNPJ", "CABEÇALHO", "BALANCETE",
            "SALDO", "TOTAL", "SUBTOTAL", "RESUMO", "EXTRATO",
            "DATA", "DESCRIÇÃO", "VALOR", "CONTA"
        };

        return headerKeywords.Any(k => line.ToUpperInvariant().Contains(k));
    }

    private static Money? ExtractAmount(string line)
    {
        var patterns = new[]
        {
            @"R\$\s*([\d,.]+)",
            @"\b(\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{2}))\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var valueStr = match.Groups[1].Value.Replace(".", "", StringComparison.Ordinal)
                    .Replace(",", ".", StringComparison.Ordinal);
                if (decimal.TryParse(valueStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var amount) && amount > 0)
                {
                    return new Money(amount, "BRL");
                }
            }
        }

        return null;
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
            if (match.Success && DateTime.TryParse(match.Value, out var date))
                return date;
        }

        return null;
    }

    private static string ExtractDescription(string line, Money? amount, DateTime? date)
    {
        var cleanedLine = line;
        if (amount != null)
        {
            var amountStr = amount.Amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            cleanedLine = cleanedLine.Replace(amountStr, "", StringComparison.OrdinalIgnoreCase);
        }

        if (date.HasValue)
        {
            var dateStr = date.Value.ToString("dd/MM/yyyy");
            cleanedLine = cleanedLine.Replace(dateStr, "", StringComparison.OrdinalIgnoreCase);
        }

        cleanedLine = Regex.Replace(cleanedLine, @"[^a-zA-Z0-9\s\-]", " ");
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

    private static DocumentType DetectDocumentType(string rawText)
    {
        var upperText = rawText.ToUpperInvariant();
        if (upperText.Contains("NOTA FISCAL", StringComparison.Ordinal) || upperText.Contains("NF-E", StringComparison.Ordinal))
            return DocumentType.NotaFiscal;
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
