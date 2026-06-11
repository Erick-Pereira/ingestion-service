"""One-off: move static parsing helpers from ParseDocumentUseCase to DocumentParsers."""
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "Simcag.IngestionService.Application"
src = root / "UseCases" / "ParseDocumentUseCase.cs"
dst = root / "DocumentExtraction" / "Parsing" / "DocumentParsers.cs"

lines = src.read_text(encoding="utf-8").splitlines(keepends=True)

# Body: from MaskBrazilianTaxIds (line 67) through DetectDocumentType end (line 1399)
body = lines[66:1399]
text = "".join(body)

text = text.replace("private static", "internal static")
text = text.replace("private List<ExtractedLineItem>", "internal static List<ExtractedLineItem>")
text = text.replace("private sealed record DanfeParsedRow", "internal sealed record TabularProductRow")
text = text.replace("DanfeParsedRow", "TabularProductRow")
text = text.replace("IsNaturezaOperacaoLike(desc)", "ExcludedFieldPatterns.IsOperationalMetadata(desc)")
text = text.replace("private List<ExtractedLineItem> ParseLineItems", "internal static List<ExtractedLineItem> ParseLineItems")

# Remove duplicate IsNaturezaOperacaoLike method block
import re
text = re.sub(
    r"\n    internal static bool IsNaturezaOperacaoLike\(string desc\)\s*\{[\s\S]*?\n    \}\n",
    "\n",
    text,
    count=1,
)

header = """using System.Globalization;
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
"""

dst.parent.mkdir(parents=True, exist_ok=True)
dst.write_text(header + text + "}\n", encoding="utf-8")
print(f"Wrote {dst} ({len(text)} chars)")
