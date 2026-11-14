using System.Security.Cryptography;

namespace MagicMusicWall.Core;

public sealed record MediaTags
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public string Genre { get; init; } = "";
    public string Comment { get; init; } = "";
    public uint Year { get; init; }
    public uint TrackNumber { get; init; }
    public uint DiscNumber { get; init; }
}

/// <summary>Null leaves a field untouched; an empty string or zero explicitly clears it.</summary>
public sealed record MediaTagPatch
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public string? Comment { get; init; }
    public uint? Year { get; init; }
    public uint? TrackNumber { get; init; }
    public uint? DiscNumber { get; init; }
}

public sealed record MediaTagWriteResult(string BackupPath, MediaTags Tags);

/// <summary>Edits a staged copy, verifies it, then atomically replaces one file with a backup.</summary>
public sealed class MediaTagService
{
    private static readonly HashSet<string> WritableExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".m4a", ".mp4", ".m4v", ".ogg", ".opus", ".wma", ".wmv", ".wav", ".aif", ".aiff" };
    private static readonly SemaphoreSlim Writes = new(1, 1);

    // A capability hint only. The real container, permissions and replace support are checked on save.
    public static bool SupportsWriting(string path) => !string.IsNullOrWhiteSpace(path) && WritableExtensions.Contains(Path.GetExtension(path));

    public static MediaTags Read(string path)
    {
        using var media = TagLib.File.Create(path, TagLib.ReadStyle.Average);
        return Snapshot(media.Tag);
    }

    public static async Task<MediaTagWriteResult> WriteAsync(string path, MediaTagPatch patch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        Validate(patch);
        if (!SupportsWriting(path)) throw new NotSupportedException("Writing tags is not supported for this file format.");
        var fullPath = Path.GetFullPath(path);
        await Writes.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => Write(fullPath, patch, ct), ct).ConfigureAwait(false); }
        finally { Writes.Release(); }
    }

    private static MediaTagWriteResult Write(string path, MediaTagPatch patch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CheckFile(path);
        var initial = new FileInfo(path);
        var originalLength = initial.Length;
        var originalWriteTime = initial.LastWriteTimeUtc;
        var directory = initial.DirectoryName!;
        var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        var staged = Path.Combine(directory, ".mmw-edit-" + suffix + initial.Extension);
        var backup = path + ".mmw-backup-" + suffix + ".bak";

        try
        {
            // Allow readers and replacement, but prevent new in-place writers while staging.
            using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            byte[] originalHash;
            using (var copy = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[128 * 1024];
                int count;
                while ((count = original.Read(buffer)) != 0)
                {
                    ct.ThrowIfCancellationRequested();
                    copy.Write(buffer, 0, count);
                    hash.AppendData(buffer, 0, count);
                }
                copy.Flush(flushToDisk: true);
                originalHash = hash.GetHashAndReset();
            }

            ct.ThrowIfCancellationRequested();
            MediaTags expected;
            string originalLyrics;
            byte[][] originalPictures;
            uint originalTrackCount, originalDiscCount;
            using (var media = TagLib.File.Create(staged, TagLib.ReadStyle.Average))
            {
                if (!media.Writeable || media.PossiblyCorrupt)
                    throw new NotSupportedException("This media container cannot safely be tagged. The original file has not been changed.");
                expected = Expected(Snapshot(media.Tag), patch);
                originalLyrics = media.Tag.Lyrics ?? "";
                originalPictures = media.Tag.Pictures.Select(p => SHA256.HashData(p.Data.Data)).ToArray();
                originalTrackCount = media.Tag.TrackCount; originalDiscCount = media.Tag.DiscCount;
                Apply(media.Tag, patch);
                media.Save();
            }
            MediaTags result;
            using (var written = TagLib.File.Create(staged, TagLib.ReadStyle.Average))
            {
                result = Snapshot(written.Tag);
                if (result != expected || (written.Tag.Lyrics ?? "") != originalLyrics ||
                    written.Tag.TrackCount != originalTrackCount || written.Tag.DiscCount != originalDiscCount ||
                    !originalPictures.Select(Convert.ToHexString).SequenceEqual(written.Tag.Pictures.Select(p => Convert.ToHexString(SHA256.HashData(p.Data.Data)))))
                    throw new IOException("This format did not preserve the selected tags or existing metadata. The original file has not been changed.");
            }
            using (var completedCopy = new FileStream(staged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                completedCopy.Flush(flushToDisk: true);
            ct.ThrowIfCancellationRequested();
            CheckFile(path);

            // Re-open the path as well as retaining the original handle: another program may have replaced it.
            using var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var currentInfo = new FileInfo(path);
            if (currentInfo.Length != originalLength || currentInfo.LastWriteTimeUtc != originalWriteTime ||
                !CryptographicOperations.FixedTimeEquals(originalHash, Hash(current, ct)))
                throw new IOException("The media file changed while its tags were being prepared. Nothing was saved; reopen the file and try again.");

            ct.ThrowIfCancellationRequested();
            try { File.Replace(staged, path, backup, ignoreMetadataErrors: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw new IOException("Could not atomically replace the media file. Close other players and check that its drive or network share supports file replacement. No direct overwrite was attempted." +
                    (File.Exists(backup) ? " An original-file backup is available at: " + backup : ""), ex);
            }
            return new(backup, result);
        }
        finally
        {
            // This is our uniquely named staged copy, never the original or its backup.
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static byte[] Hash(Stream stream, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return hash.GetHashAndReset();
    }

    private static void CheckFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The media file is unavailable.", path);
        if (Path.GetFileName(path).Contains(':')) throw new NotSupportedException("Alternate data streams cannot be tagged.");
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("The media file is read-only.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new NotSupportedException("Symbolic links and placeholder media files cannot be tagged. Choose a regular local or network file.");
        for (var parent = info.Directory; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Media beneath a symbolic link or redirected folder cannot be tagged.");
    }

    private static MediaTags Snapshot(TagLib.Tag tag) => new()
    {
        Title = tag.Title ?? "", Artist = string.Join("; ", tag.Performers ?? []),
        Album = tag.Album ?? "", AlbumArtist = string.Join("; ", tag.AlbumArtists ?? []),
        Genre = string.Join("; ", tag.Genres ?? []), Comment = tag.Comment ?? "",
        Year = tag.Year, TrackNumber = tag.Track, DiscNumber = tag.Disc
    };

    private static void Apply(TagLib.Tag tag, MediaTagPatch patch)
    {
        if (patch.Title is { } title) tag.Title = title;
        if (patch.Artist is { } artist) tag.Performers = artist.Length == 0 ? [] : [artist];
        if (patch.Album is { } album) tag.Album = album;
        if (patch.AlbumArtist is { } albumArtist) tag.AlbumArtists = albumArtist.Length == 0 ? [] : [albumArtist];
        if (patch.Genre is { } genre) tag.Genres = genre.Length == 0 ? [] : [genre];
        if (patch.Comment is { } comment) tag.Comment = comment;
        if (patch.Year is { } year) tag.Year = year;
        if (patch.TrackNumber is { } track) tag.Track = track;
        if (patch.DiscNumber is { } disc) tag.Disc = disc;
    }

    private static MediaTags Expected(MediaTags original, MediaTagPatch patch) => original with
    {
        Title = patch.Title ?? original.Title, Artist = patch.Artist ?? original.Artist,
        Album = patch.Album ?? original.Album, AlbumArtist = patch.AlbumArtist ?? original.AlbumArtist,
        Genre = patch.Genre ?? original.Genre, Comment = patch.Comment ?? original.Comment,
        Year = patch.Year ?? original.Year, TrackNumber = patch.TrackNumber ?? original.TrackNumber,
        DiscNumber = patch.DiscNumber ?? original.DiscNumber
    };

    private static void Validate(MediaTagPatch patch)
    {
        if (patch == new MediaTagPatch()) throw new ArgumentException("Select at least one tag to save.", nameof(patch));
        foreach (var value in new[] { patch.Title, patch.Artist, patch.Album, patch.AlbumArtist, patch.Genre })
            if (value is not null && (value.Length > 1024 || value.Any(char.IsControl)))
                throw new ArgumentException("Tag values must be at most 1,024 characters and cannot contain control characters.", nameof(patch));
        if (patch.Comment is { } comment && (comment.Length > 16384 || comment.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t')))
            throw new ArgumentException("Comments must be at most 16,384 characters and cannot contain unsupported control characters.", nameof(patch));
        if (patch.Year > 9999 || patch.TrackNumber > 9999 || patch.DiscNumber > 9999)
            throw new ArgumentException("Year, track and disc must be between 0 and 9,999.", nameof(patch));
    }
}
