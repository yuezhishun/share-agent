using System.Security.Cryptography;
using System.Text;

namespace TerminalGateway.Api.Services;

public sealed class WriteTokenService
{
    public string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var hashed = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(hashed).ToLowerInvariant();
    }

    public bool IsMatch(string? providedToken, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return true;
        }

        var current = HashToken(providedToken ?? string.Empty);
        if (current.Length != expectedHash!.Length)
        {
            return false;
        }

        var a = Convert.FromHexString(current);
        var b = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
