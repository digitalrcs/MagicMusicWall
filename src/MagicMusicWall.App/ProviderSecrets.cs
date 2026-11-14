using System.Security.Cryptography;
using System.Text;

namespace MagicMusicWall.App;

internal static class ProviderSecrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MagicMusicWall.TMDB.v1");

    internal static string Protect(string token)
    {
        token = token.Trim();
        if (token.Length == 0) return "";
        if (token.Length > 4096 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            throw new ArgumentException("Paste the TMDB API Read Access Token without spaces or line breaks.");
        var bytes = Encoding.UTF8.GetBytes(token);
        try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal static string Unprotect(string protectedToken)
    {
        if (string.IsNullOrEmpty(protectedToken)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidOperationException("The saved movie lookup token could not be opened by this Windows account. Enter it again in Setup > Metadata.");
        }
    }
}
