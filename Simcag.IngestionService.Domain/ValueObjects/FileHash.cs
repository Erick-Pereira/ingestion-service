using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Simcag.IngestionService.Domain.ValueObjects;

/// <summary>
/// SHA-256 do arquivo original (idempotência / deduplicação).
/// </summary>
public sealed class FileHash : IEquatable<FileHash>
{
    private static readonly Regex HexPattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);

    public string Value { get; }

    private FileHash(string value)
    {
        Value = value;
    }

    public static FileHash FromHex(string sha256HexLower)
    {
        if (string.IsNullOrWhiteSpace(sha256HexLower))
            throw new ArgumentException("Hash não pode ser vazio", nameof(sha256HexLower));

        var normalized = sha256HexLower.Trim().ToLowerInvariant();
        if (!HexPattern.IsMatch(normalized))
            throw new ArgumentException("Hash deve ser SHA-256 em hexadecimal (64 caracteres).", nameof(sha256HexLower));

        return new FileHash(normalized);
    }

    public static FileHash ComputeSha256(byte[] content)
    {
        if (content == null || content.Length == 0)
            throw new ArgumentException("Conteúdo é obrigatório para o hash.", nameof(content));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(content);
        return new FileHash(Convert.ToHexString(hashBytes).ToLowerInvariant());
    }

    public bool Equals(FileHash? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is FileHash other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static bool operator ==(FileHash? left, FileHash? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(FileHash? left, FileHash? right) => !(left == right);
}
