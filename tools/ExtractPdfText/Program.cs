using System.Text;
using UglyToad.PdfPig;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: ExtractPdfText <path-to.pdf>");
    return 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

using var pdf = PdfDocument.Open(path);
var sb = new StringBuilder();
foreach (var page in pdf.GetPages())
    sb.AppendLine(page.Text);

Console.Write(sb.ToString().TrimEnd());
return 0;
