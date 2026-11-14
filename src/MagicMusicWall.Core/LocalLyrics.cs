using System.Text;
using System.Text.RegularExpressions;

namespace MagicMusicWall.Core;

/// <summary>Reads embedded lyrics or a same-base sidecar, without changing media.</summary>
public static class LocalLyrics
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly Regex Timing = new(@"^\s*(?:\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\])+\s*", RegexOptions.Multiline | RegexOptions.NonBacktracking);
    private static readonly Regex MetadataLine = new(@"^\s*\[(?:ar|al|ti|au|by|re|ve|length|offset):[^\]\r\n]*\]\s*$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    public static LyricsResult? Read(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Path)) return null;
        try
        {
            using var media = TagLib.File.Create(item.Path, TagLib.ReadStyle.None);
            var lyrics = media.Tag.Lyrics;
            if (!string.IsNullOrWhiteSpace(lyrics) && lyrics.Length <= MaximumBytes)
            {
                var embedded = Result(lyrics, "Embedded lyrics", item);
                if (embedded is not null) return embedded;
            }
        }
        catch (Exception ex) when (IsReadFailure(ex)) { }

        foreach (var extension in new[] { ".lrc", ".txt" })
        {
            try
            {
                var path = Path.ChangeExtension(item.Path, extension);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MaximumBytes) continue;
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                var oversized = false;
                while (true)
                {
                    var count = stream.Read(chunk, 0, chunk.Length);
                    if (count == 0) break;
                    if (buffer.Length + count > MaximumBytes) { oversized = true; break; }
                    buffer.Write(chunk, 0, count);
                }
                if (oversized) continue;
                buffer.Position = 0;
                using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var result = Result(reader.ReadToEnd(), "Local lyrics file", item);
                if (result is not null) return result;
            }
            catch (Exception ex) when (IsReadFailure(ex)) { }
        }
        return null;
    }

    private static LyricsResult? Result(string lyrics, string sourceName, MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return null;
        var timed = Timing.IsMatch(lyrics);
        var plain = string.Join(Environment.NewLine, lyrics.Replace("\r\n", "\n").Split('\n')
            .Where(line => !MetadataLine.IsMatch(line)).Select(line => timed ? Timing.Replace(line, "") : line)).Trim();
        if (string.IsNullOrWhiteSpace(plain)) return null;
        return new()
        {
            Found = true, PlainText = plain, SyncedText = timed ? lyrics.Trim() : "", SourceName = sourceName, SourceUrl = "",
            MatchedTitle = item.Title, MatchedArtist = item.Artist, MatchedAlbum = item.Album,
            Message = sourceName == "Embedded lyrics" ? "Read from this file's embedded tags." : "Read from a lyrics file beside this media file."
        };
    }

    private static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or NotSupportedException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException;
}
