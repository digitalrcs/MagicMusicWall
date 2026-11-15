using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MagicMusicWall.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MagicMusicWall.Streaming;

/// <summary>LAN sign-in with bounded attempts and expiring, signed HttpOnly cookies.</summary>
internal sealed class RemoteSignIn(string accessToken, string passphraseHash)
{
    // Bind remembered access to both credentials. Changing either revokes all cookies,
    // but ordinary restarts preserve them without saving browser secrets on disk.
    private readonly byte[] _signingKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(accessToken),
        Encoding.UTF8.GetBytes("MagicMusicWall.Session.v1:" + passphraseHash));
    private readonly object _attemptLock = new();
    private readonly Dictionary<string, int> _attempts = new();
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private int _totalAttempts;
    private bool Enabled => RemotePassphrase.IsConfigured(passphraseHash);
    private static string CookieName(HttpRequest request) => "mmw-session-" + (request.Host.Port ?? 80);

    public bool Authorized(HttpRequest request)
    {
        if (!Enabled || !request.Cookies.TryGetValue(CookieName(request), out var value) || value.Length > 220) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || parts[1].Length != 32 || parts[2].Length != 64 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expires <= now || expires > now + (long)TimeSpan.FromDays(30).TotalSeconds) return false;
        try
        {
            var expected = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts[2]));
        }
        catch (FormatException) { return false; }
    }

    public void MapRoutes(WebApplication app, Func<HttpRequest, bool> authorized)
    {
        app.MapGet("/auth/status", (HttpContext context) => Results.Json(new { passphraseEnabled = Enabled, authenticated = authorized(context.Request) }));
        app.MapPost("/auth/login", async (HttpContext context) =>
        {
            if (!context.Request.HasJsonContentType()) { context.Response.StatusCode = 415; return; }
            if (!Enabled) { await Error(context, 401, "Set a remote passphrase in the Windows app’s Setup → Remote page first, or use its QR code."); return; }
            if (!AllowAttempt(context.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
            {
                context.Response.Headers.RetryAfter = "60";
                await Error(context, 429, "Too many sign-in attempts. Please wait one minute and try again."); return;
            }
            Login? login;
            try { login = await context.Request.ReadFromJsonAsync<Login>(context.RequestAborted); }
            catch (JsonException) { await Error(context, 400, "Enter a valid passphrase."); return; }
            catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { context.Response.StatusCode = 413; return; }
            if (login is null || !RemotePassphrase.Verify(login.Passphrase, passphraseHash))
            { await Error(context, 401, "That passphrase did not match. Try again."); return; }
            var expires = DateTimeOffset.UtcNow.Add(login.Remember ? TimeSpan.FromDays(30) : TimeSpan.FromHours(8));
            var payload = expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var signature = Convert.ToHexString(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload)));
            context.Response.Cookies.Append(CookieName(context.Request), payload + "." + signature, Options(login.Remember ? expires : null));
            context.Response.StatusCode = 204;
        });
        app.MapPost("/auth/logout", (HttpContext context) =>
        {
            // JSON forces a cross-origin browser request to preflight. Host, Origin and
            // Fetch-Site are checked by the server before any auth route is dispatched.
            if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
            context.Response.Cookies.Delete(CookieName(context.Request), Options(null));
            return Results.NoContent();
        });
    }

    private bool AllowAttempt(string address)
    {
        lock (_attemptLock)
        {
            if (DateTimeOffset.UtcNow - _windowStart >= TimeSpan.FromMinutes(1))
            { _windowStart = DateTimeOffset.UtcNow; _attempts.Clear(); _totalAttempts = 0; }
            _attempts.TryGetValue(address, out var count);
            if (count >= 5 || _totalAttempts >= 30) return false;
            _attempts[address] = count + 1; _totalAttempts++;
            return true;
        }
    }

    // The app serves HTTP on a trusted LAN, so Secure cannot be set. No Domain
    // attribute: this cookie is host-only, HttpOnly, and unavailable cross-site.
    private static CookieOptions Options(DateTimeOffset? expires) => new()
    { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expires, IsEssential = true };
    private static Task Error(HttpContext context, int status, string message)
    { context.Response.StatusCode = status; return context.Response.WriteAsJsonAsync(new { error = message }); }
    private sealed record Login(string? Passphrase, bool Remember = false);
}
