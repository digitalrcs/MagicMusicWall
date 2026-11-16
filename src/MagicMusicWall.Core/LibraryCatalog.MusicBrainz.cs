using System.Globalization;
using System.Text;

namespace MagicMusicWall.Core;

public sealed partial class LibraryCatalog
{
    /// <summary>Save or clear reviewed online information without changing media tags.</summary>
    public void SetMusicBrainzInfo(string itemId, MusicBrainzInfo? info)
    {
        var normalized = info is null ? null : NormalizeMusicBrainzInfo(info);
        lock (_gate)
        {
            var item = Document.Items.FirstOrDefault(i => i.Id == itemId)
                ?? throw new ArgumentException("This file is no longer in the library.", nameof(itemId));
            if (item.IsVideo) throw new ArgumentException("MusicBrainz information is only available for music files.", nameof(itemId));
            var previous = item.MusicBrainz;
            try { item.MusicBrainz = normalized; SaveLocked(); }
            catch { item.MusicBrainz = previous; throw; }
        }
    }

    // Called before an existing library entry receives new local title/artist tags.
    // Case, spacing, punctuation and diacritic corrections do not change its identity.
    private static void InvalidateMusicBrainzIdentity(MediaItem item, string? title, string? artist, string? album)
    {
        if (MusicBrainzIdentity(item.Title) != MusicBrainzIdentity(title)
            || MusicBrainzIdentity(item.Artist) != MusicBrainzIdentity(artist)
            || (!string.IsNullOrEmpty(item.MusicBrainz?.ReleaseId)
                && MusicBrainzIdentity(item.Album) != MusicBrainzIdentity(album))) item.MusicBrainz = null;
    }

    private static string MusicBrainzIdentity(string? value) => new((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
        .Select(char.ToUpperInvariant).ToArray());

    private static MusicBrainzInfo NormalizeMusicBrainzInfo(MusicBrainzInfo info)
    {
        string Id(string? value, bool required)
        {
            if (!required && string.IsNullOrWhiteSpace(value)) return "";
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
                throw new ArgumentException("MusicBrainz information needs valid recording and release identifiers.", nameof(info));
            return id.ToString("D");
        }
        string Text(string? value)
        {
            value = value?.Trim() ?? "";
            if (value.Length > 512 || value.Any(char.IsControl))
                throw new ArgumentException("MusicBrainz text is too long or contains unsupported control characters.", nameof(info));
            return value;
        }
        string[] Labels(string[]? values)
        {
            if (values is { Length: > 64 }) throw new ArgumentException("MusicBrainz returned too many labels.", nameof(info));
            return (values ?? []).Select(Text).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        var sources = Labels(info.Sources);
        if (sources.Any(source => !ValidMusicBrainzSource(source)))
            throw new ArgumentException("MusicBrainz sources must identify a recording, release, release group, artist or work.", nameof(info));
        return info with
        {
            RecordingId = Id(info.RecordingId, true), ReleaseId = Id(info.ReleaseId, false),
            Title = Text(info.Title), Artist = Text(info.Artist), Album = Text(info.Album),
            FirstReleaseDate = Text(info.FirstReleaseDate),
            Genres = Labels(info.Genres), Tags = Labels(info.Tags), Instruments = Labels(info.Instruments),
            Credits = Labels(info.Credits), Sources = sources,
            RetrievedUtc = info.RetrievedUtc == default ? DateTimeOffset.UtcNow : info.RetrievedUtc.ToUniversalTime()
        };
    }

    private static bool ValidMusicBrainzSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("musicbrainz.org", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] is "recording" or "release" or "release-group" or "artist" or "work"
            && Guid.TryParse(parts[1], out var id) && id != Guid.Empty;
    }
}
