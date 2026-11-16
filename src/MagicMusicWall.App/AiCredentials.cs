using System.Security.Cryptography;
using System.Text;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

internal static class AiCredentials
{
    public static string Protect(string key) => Convert.ToBase64String(ProtectedData.Protect(
        Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
    public static AiProviderOptions Options(AppPreferences settings)
    {
        var key = "";
        if (!string.IsNullOrEmpty(settings.AiKeyProtected) && settings.AiKeyEndpoint == settings.AiBaseUrl && settings.AiKeyProvider == settings.AiProvider)
        {
            try { key = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.AiKeyProtected), null, DataProtectionScope.CurrentUser)); }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            { throw new InvalidOperationException("Re-enter your AI key in Setup > AI for this Windows account."); }
        }
        return new(settings.AiProvider, settings.AiBaseUrl, settings.AiModel, key);
    }
}
