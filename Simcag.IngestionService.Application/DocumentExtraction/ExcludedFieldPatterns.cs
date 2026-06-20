namespace Simcag.IngestionService.Application.DocumentExtraction;

/// <summary>Metadados de documento que nunca devem virar linha de despesa.</summary>
public static class ExcludedFieldPatterns
{
    public static bool IsOperationalMetadata(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
            return false;

        var u = desc.ToUpperInvariant();
        if (u.Contains("NATUREZA DA OPER", StringComparison.Ordinal)
            || u.Contains("VALOR TOTAL DA NOTA", StringComparison.Ordinal)
            || u.Contains("PROTOCOLO DE AUTORIZ", StringComparison.Ordinal))
            return true;

        if (u.Contains("VENDA MERC", StringComparison.Ordinal)
            || u.Contains("DEV VENDA", StringComparison.Ordinal)
            || u.Contains("DEVOLU", StringComparison.Ordinal))
            return true;

        return u.Contains("PRESTA", StringComparison.Ordinal)
               && u.Contains("SERV", StringComparison.Ordinal);
    }

    /// <summary>Linhas tributárias/ancilares (IPI, ICMS, garantia…) não devem virar item de produto na despesa.</summary>
    public static bool IsTaxOrAncillaryProductLine(string? desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
            return false;

        var upper = desc.Trim().ToUpperInvariant();
        string[] taxOnly = ["ICMS", "IPI", "PIS", "COFINS", "ISS", "DIFAL", "FCP", "CSLL", "IRRF", "INSS"];
        if (taxOnly.Contains(upper))
            return true;

        string[] tokens =
        [
            "GARANTIA", "SEGURO", "FRETE", "ICMS", "IPI", "PIS", "COFINS", "ISS", "TRIBUTO", "IMPOSTO",
            "VALOR ICMS", "VALOR IPI", "ALÍQ. ICMS", "ALIQ. ICMS",
        ];
        return tokens.Any(t => upper.Contains(t, StringComparison.Ordinal));
    }

    public static bool IsDocumentHeaderNoise(string desc)
    {
        if (desc.Length > 240)
            return true;

        var u = desc.ToUpperInvariant();
        return u.Contains("RECEBEMOS DE", StringComparison.Ordinal)
               || u.Contains("DANFE", StringComparison.Ordinal)
               || u.Contains("CHAVE DE ACESSO", StringComparison.Ordinal)
               || u.Contains("IDENTIFICAÇÃO DO EMITENTE", StringComparison.Ordinal)
               || u.Contains("IDENTIFICACAO DO EMITENTE", StringComparison.Ordinal);
    }
}
