using Simcag.IngestionService.Domain.ValueObjects;

namespace Simcag.IngestionService.Domain.Entities;

public class ExtractedLineItem
{
    public int LineNumber { get; private set; }
    public Money? Amount { get; private set; }
    public DateTime? Date { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string RawLine { get; private set; } = string.Empty;
    public int ConfidenceScore { get; private set; }

    protected ExtractedLineItem() { }

    public ExtractedLineItem(
        int lineNumber,
        Money? amount,
        DateTime? date,
        string description,
        string rawLine,
        int confidenceScore = 0)
    {
        if (lineNumber < 0)
            throw new ArgumentException("Número da linha deve ser não negativo", nameof(lineNumber));
        
        if (confidenceScore < 0 || confidenceScore > 100)
            throw new ArgumentException("Score de confiança deve estar entre 0 e 100", nameof(confidenceScore));

        LineNumber = lineNumber;
        Amount = amount;
        Date = date;
        Description = description ?? string.Empty;
        RawLine = rawLine ?? string.Empty;
        ConfidenceScore = confidenceScore;
    }

    public void SetAmount(Money? amount)
    {
        Amount = amount;
    }

    public void SetDate(DateTime? date)
    {
        Date = date;
    }

    public void SetDescription(string description)
    {
        Description = description ?? string.Empty;
    }

    public void SetConfidenceScore(int confidenceScore)
    {
        if (confidenceScore < 0 || confidenceScore > 100)
            throw new ArgumentException("Score de confiança deve estar entre 0 e 100", nameof(confidenceScore));
        
        ConfidenceScore = confidenceScore;
    }

    public bool HasValidData()
    {
        return Amount != null && Amount.IsValid() ||
               Date.HasValue ||
               !string.IsNullOrWhiteSpace(Description);
    }
}