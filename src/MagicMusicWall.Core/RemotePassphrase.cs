using System.Security.Cryptography;

namespace MagicMusicWall.Core;

/// <summary>Versioned, salted verifier; the remote passphrase itself is never saved.</summary>
public static class RemotePassphrase
{
    private const int Iterations = 600_000;

    public static string CreateHash(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length is < 8 or > 128)
            throw new ArgumentException("Choose a remote passphrase of 8–128 characters.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Convert.ToHexString(salt)}.{Convert.ToHexString(hash)}";
    }

    public static bool IsConfigured(string? verifier) => TryParse(verifier, out _, out _);

    public static bool Verify(string? passphrase, string? verifier)
    {
        if (passphrase is null || passphrase.Length is < 8 or > 128 || !TryParse(verifier, out var salt, out var expected)) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParse(string? verifier, out byte[] salt, out byte[] hash)
    {
        salt = []; hash = [];
        if (verifier is not { Length: 100 } || !verifier.StartsWith("v1.", StringComparison.Ordinal) || verifier[35] != '.') return false;
        try { salt = Convert.FromHexString(verifier[3..35]); hash = Convert.FromHexString(verifier[36..]); return true; }
        catch (FormatException) { return false; }
    }
}
