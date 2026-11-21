using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MagicMusicWall.Core;

/// <summary>Local, read-only media indexing. Library edits never alter or delete media files.</summary>
public sealed partial class LibraryCatalog
{
    public static bool IsSupported(string path) => AudioExtensions.Contains(System.IO.Path.GetExtension(path)) || IsVideo(path);
    public static bool IsVideo(string path) => VideoExtensions.Contains(System.IO.Path.GetExtension(path));
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".wav", ".wave", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif", ".alac", ".ape" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".webm", ".mpg", ".mpeg", ".ts", ".m2ts", ".vob", ".3gp" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly string _directory;
    private bool _loadBlocked;

    public LibraryCatalog(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _directory = System.IO.Path.GetFullPath(dataDirectory);
    }

    public LibraryDocument Document { get; private set; } = CreateDocument();
    public string? LoadWarning { get; private set; }
    public string LibraryPath => System.IO.Path.Combine(_directory, "library.json");

    public void Load()
    {
        lock (_gate)
        {
            LoadWarning = null;
            _loadBlocked = false;
            if (!File.Exists(LibraryPath)) { Document = CreateDocument(); return; }
            // Any read/recovery failure must leave saving disabled until a successful reload.
            _loadBlocked = true;
            try { Document = ReadDocument(LibraryPath); }
            catch (UnsupportedLibraryVersionException) { _loadBlocked = true; throw; }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                // Preserve the exact damaged bytes before permitting any future Save.
                Directory.CreateDirectory(_directory);
                var preserved = System.IO.Path.Combine(_directory, $"library.corrupt.{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.Copy(LibraryPath, preserved);
                var backup = LibraryPath + ".bak";
                try
                {
                    Document = File.Exists(backup) ? ReadDocument(backup) : CreateDocument();
                    LoadWarning = $"The library file was damaged. {(File.Exists(backup) ? "A previous backup was restored." : "An empty library was opened.")} The original is preserved at {preserved}.";
                }
                catch (Exception backupError) when (backupError is JsonException or InvalidDataException)
                {
                    Document = CreateDocument();
                    LoadWarning = $"The library and backup could not be read. An empty library was opened; the original is preserved at {preserved}.";
                }
            }
            // Availability is volatile and must not be trusted across removable-drive reconnects.
            foreach (var item in Document.Items) item.IsAvailable = File.Exists(item.Path);
            _loadBlocked = false;
        }
    }

    public void Save()
    {
        lock (_gate) SaveLocked();
    }

    /// <summary>Refresh catalog references after a separately confirmed file-tag write.</summary>
    public void ApplyWrittenTags(string itemId, MediaTags tags, bool replaceAlbumOverride)
    {
        ArgumentNullException.ThrowIfNull(tags);
        lock (_gate)
        {
            var item = Document.Items.FirstOrDefault(i => i.Id == itemId)
                ?? throw new ArgumentException("This file is no longer in the library.", nameof(itemId));
            var previous = (item.Title, item.Artist, item.Album, item.TaggedAlbum, item.AlbumOverride,
                item.AlbumLookupSource, item.Genre, item.Year, item.TrackNumber, item.DiscNumber);
            var previousMusicBrainz = item.MusicBrainz;
            try
            {
                InvalidateMusicBrainzIdentity(item, NonBlank(tags.Title, System.IO.Path.GetFileNameWithoutExtension(item.Path)),
                    NonBlank(tags.Artist, tags.AlbumArtist), replaceAlbumOverride || string.IsNullOrEmpty(item.AlbumOverride) ? tags.Album : item.AlbumOverride);
                item.Title = NonBlank(tags.Title, System.IO.Path.GetFileNameWithoutExtension(item.Path));
                item.Artist = NonBlank(tags.Artist, tags.AlbumArtist);
                item.TaggedAlbum = tags.Album;
                if (replaceAlbumOverride) item.AlbumOverride = item.AlbumLookupSource = "";
                item.Album = string.IsNullOrEmpty(item.AlbumOverride) ? tags.Album : item.AlbumOverride;
                item.Genre = tags.Genre; item.Year = tags.Year;
                item.TrackNumber = tags.TrackNumber; item.DiscNumber = tags.DiscNumber;
                SaveLocked();
            }
            catch
            {
                item.MusicBrainz = previousMusicBrainz;
                (item.Title, item.Artist, item.Album, item.TaggedAlbum, item.AlbumOverride,
                    item.AlbumLookupSource, item.Genre, item.Year, item.TrackNumber, item.DiscNumber) = previous;
                throw;
            }
        }
    }

    private void SaveLocked()
    {
        if (_loadBlocked) throw new InvalidOperationException("The existing library could not be safely loaded and cannot be overwritten. Restore access or use a compatible app version, then reload it.");
        Directory.CreateDirectory(_directory);
        var temp = System.IO.Path.Combine(_directory, $"library.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, Document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(LibraryPath)) File.Replace(temp, LibraryPath, LibraryPath + ".bak");
            else File.Move(temp, LibraryPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<ImportResult> AddFolderAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var normalized = NormalizePath(path);
        if (!Directory.Exists(normalized)) throw new DirectoryNotFoundException($"The folder is unavailable: {normalized}");
        await _scanGate.WaitAsync(ct);
        try
        {
            var batch = await Task.Run(() => ScanFiles(EnumerateFolder(normalized, ct), progress, ct), ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!Document.Folders.Any(f => SamePath(f.Path, normalized))) Document.Folders.Add(new LibraryFolder { Path = normalized });
                ApplyBatch(batch);
                SaveLocked();
            }
            return batch.Result;
        }
        finally { _scanGate.Release(); }
    }

    public async Task<ImportResult> AddFilesAsync(IEnumerable<string> paths, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = paths.ToArray();
        await _scanGate.WaitAsync(ct);
        try
        {
            var batch = await Task.Run(() => ScanFiles(inputs.Select(p => new ScanEntry(p)), progress, ct), ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate) { ApplyBatch(batch); SaveLocked(); }
            return batch.Result;
        }
        finally { _scanGate.Release(); }
    }

    public async Task<ImportResult> RescanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _scanGate.WaitAsync(ct);
        try
        {
            string[] folders;
            string[] existing;
            lock (_gate)
            {
                folders = Document.Folders.Select(f => f.Path).ToArray();
                existing = Document.Items.Select(i => i.Path).ToArray();
            }
            var batch = await Task.Run(() => ScanFiles(folders.SelectMany(f => EnumerateFolder(f, ct))
                .Concat(existing.Select(p => new ScanEntry(p))), progress, ct), ct);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ApplyBatch(batch);
                foreach (var item in Document.Items) item.IsAvailable = File.Exists(item.Path);
                SaveLocked();
            }
            return batch.Result;
        }
        finally { _scanGate.Release(); }
    }

    private void ApplyBatch(ScanBatch batch)
    {
        var existing = Document.Items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in batch.Items)
        {
            if (existing.TryGetValue(incoming.Path, out var old))
            {
                // Preserve IDs so queue and playlist references survive metadata changes.
                // A transient tag read failure must not erase previously indexed metadata.
                if (!batch.TagReadFailures.Contains(incoming.Path))
                {
                    InvalidateMusicBrainzIdentity(old, incoming.Title, incoming.Artist,
                        string.IsNullOrEmpty(old.AlbumOverride) ? incoming.TaggedAlbum ?? incoming.Album : old.AlbumOverride);
                    old.Title = incoming.Title; old.Artist = incoming.Artist;
                    old.TaggedAlbum = incoming.TaggedAlbum ?? incoming.Album;
                    old.Album = string.IsNullOrEmpty(old.AlbumOverride) ? old.TaggedAlbum : old.AlbumOverride;
                    old.Genre = incoming.Genre; old.Year = incoming.Year; old.TrackNumber = incoming.TrackNumber;
                    old.DiscNumber = incoming.DiscNumber; old.DurationSeconds = incoming.DurationSeconds;
                }
                old.IsVideo = incoming.IsVideo; old.IsAvailable = true;
                batch.Result.Updated++;
            }
            else
            {
                Document.Items.Add(incoming);
                existing.Add(incoming.Path, incoming);
                batch.Result.Added++;
            }
        }
    }

    private static ScanBatch ScanFiles(IEnumerable<ScanEntry> entries, IProgress<string>? progress, CancellationToken ct)
    {
        var batch = new ScanBatch();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Warning != null) { batch.Result.Warn(entry.Warning); continue; }
            string path;
            try { path = NormalizePath(entry.Path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { batch.Result.Skipped++; batch.Result.Warn($"Invalid path: {entry.Path}"); continue; }
            if (!seen.Add(path)) continue;
            var extension = System.IO.Path.GetExtension(path);
            if (!AudioExtensions.Contains(extension) && !VideoExtensions.Contains(extension)) { batch.Result.Skipped++; continue; }
            if (!File.Exists(path)) { batch.Result.Skipped++; batch.Result.Warn($"Unavailable: {path}"); continue; }
            progress?.Report($"Reading {System.IO.Path.GetFileName(path)}");
            var item = new MediaItem { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path), IsVideo = VideoExtensions.Contains(extension) };
            try
            {
                // No Save is called on the tag file: the user's media remains read-only.
                using var media = TagLib.File.Create(path, TagLib.ReadStyle.Average);
                item.Title = NonBlank(media.Tag.Title, item.Title);
                item.Artist = string.Join("; ", media.Tag.Performers.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (item.Artist.Length == 0) item.Artist = string.Join("; ", media.Tag.AlbumArtists.Where(s => !string.IsNullOrWhiteSpace(s)));
                item.Album = media.Tag.Album ?? "";
                item.TaggedAlbum = item.Album;
                item.Genre = string.Join("; ", media.Tag.Genres.Where(s => !string.IsNullOrWhiteSpace(s)));
                item.Year = media.Tag.Year; item.TrackNumber = media.Tag.Track; item.DiscNumber = media.Tag.Disc;
                item.DurationSeconds = Math.Max(0, media.Properties.Duration.TotalSeconds);
            }
            catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                batch.TagReadFailures.Add(path);
                batch.Result.Warn($"Tags unavailable for {System.IO.Path.GetFileName(path)}; retaining existing tags or using its filename.");
            }
            item.TaggedAlbum ??= item.Album;
            batch.Items.Add(item);
        }
        return batch;
    }

    private static IEnumerable<ScanEntry> EnumerateFolder(string root, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            string[] files = [];
            string[] subdirectories = [];
            string? warning = null;
            try
            {
                if (!Directory.Exists(directory)) warning = $"Folder unavailable: {directory}";
                else
                {
                    files = Directory.GetFiles(directory);
                    subdirectories = Directory.GetDirectories(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warning = $"Cannot read folder: {directory} ({ex.Message})"; }
            if (warning != null) yield return new ScanEntry(directory, warning);
            foreach (var file in files) yield return new ScanEntry(file);
            foreach (var child in subdirectories)
            {
                bool shouldVisit = false;
                try { shouldVisit = (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (shouldVisit) pending.Push(child);
            }
        }
    }

    public MusicPlaylist CreatePlaylist(string name)
    {
        lock (_gate)
        {
            var playlist = new MusicPlaylist { Name = ValidatePlaylistName(name) };
            Document.Playlists.Add(playlist); SaveLocked(); return playlist;
        }
    }

    public void RenamePlaylist(string id, string name)
    {
        lock (_gate) { FindPlaylist(id).Name = ValidatePlaylistName(name, id); SaveLocked(); }
    }

    public void DeletePlaylist(string id)
    {
        lock (_gate) { Document.Playlists.Remove(FindPlaylist(id)); SaveLocked(); }
    }

    public void AddToPlaylist(string playlistId, IEnumerable<string> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        lock (_gate)
        {
            var list = FindPlaylist(playlistId).ItemIds;
            var known = Document.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var added = list.ToHashSet(StringComparer.Ordinal);
            foreach (var id in itemIds) if (known.Contains(id) && added.Add(id)) list.Add(id);
            SaveLocked();
        }
    }

    public void RemoveFromPlaylist(string playlistId, IEnumerable<string> itemIds)
    {
        var removed = itemIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate) { FindPlaylist(playlistId).ItemIds.RemoveAll(removed.Contains); SaveLocked(); }
    }

    public void RemoveFolder(string folderId)
    {
        lock (_gate) { Document.Folders.RemoveAll(f => f.Id == folderId); SaveLocked(); }
    }

    public void RemoveItems(IEnumerable<string> ids)
    {
        var removed = ids.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            Document.Items.RemoveAll(i => removed.Contains(i.Id));
            foreach (var playlist in Document.Playlists) playlist.ItemIds.RemoveAll(removed.Contains);
            SaveLocked();
        }
    }

    /// <summary>Applies an explicitly selected online album to the local catalog only.</summary>
    public void SetAlbumOverride(string itemId, string album, string sourceUrl)
    {
        var name = album?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 500) throw new ArgumentException("Choose an album name between 1 and 500 characters.", nameof(album));
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps
            || !source.Host.Equals("musicbrainz.org", StringComparison.OrdinalIgnoreCase) || !source.IsDefaultPort
            || source.UserInfo.Length > 0 || source.Query.Length > 0 || source.Fragment.Length > 0)
            throw new ArgumentException("Choose an album from a MusicBrainz recording or release.", nameof(sourceUrl));
        var parts = source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("recording" or "release") || !Guid.TryParseExact(parts[1], "D", out var sourceId))
            throw new ArgumentException("Choose an album from a MusicBrainz recording or release.", nameof(sourceUrl));
        var canonicalSource = $"https://musicbrainz.org/{parts[0]}/{sourceId:D}";
        lock (_gate)
        {
            var item = FindItem(itemId);
            var prior = (item.Album, item.TaggedAlbum, item.AlbumOverride, item.AlbumLookupSource, item.MusicBrainz);
            try
            {
                InvalidateMusicBrainzIdentity(item, item.Title, item.Artist, name);
                item.TaggedAlbum ??= item.Album;
                item.Album = item.AlbumOverride = name;
                item.AlbumLookupSource = canonicalSource;
                SaveLocked();
            }
            catch { (item.Album, item.TaggedAlbum, item.AlbumOverride, item.AlbumLookupSource, item.MusicBrainz) = prior; throw; }
        }
    }

    /// <summary>Restores the last indexed album tag without writing to the media file.</summary>
    public void ClearAlbumOverride(string itemId)
    {
        lock (_gate)
        {
            var item = FindItem(itemId);
            var prior = (item.Album, item.TaggedAlbum, item.AlbumOverride, item.AlbumLookupSource, item.MusicBrainz);
            try
            {
                InvalidateMusicBrainzIdentity(item, item.Title, item.Artist, item.TaggedAlbum ?? item.Album);
                item.TaggedAlbum ??= item.Album;
                item.Album = item.TaggedAlbum;
                item.AlbumOverride = item.AlbumLookupSource = "";
                SaveLocked();
            }
            catch { (item.Album, item.TaggedAlbum, item.AlbumOverride, item.AlbumLookupSource, item.MusicBrainz) = prior; throw; }
        }
    }

    /// <summary>All whitespace-delimited tokens must occur across the item's searchable fields.</summary>
    public static IEnumerable<MediaItem> Search(IEnumerable<MediaItem> items, string query)
    {
        var tokens = Fold(query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return items;
        return items.Where(item =>
        {
            var searchable = Fold(string.Join(" ", item.Title, item.Artist, item.Album, item.Genre, item.Path, item.Year == 0 ? "" : item.Year.ToString(CultureInfo.InvariantCulture)));
            return tokens.All(token => searchable.Contains(token, StringComparison.Ordinal));
        });
    }

    private static string Fold(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(c));
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private string ValidatePlaylistName(string name, string? currentId = null)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) throw new ArgumentException("Enter a playlist name.", nameof(name));
        if (trimmed.Length > 120) throw new ArgumentException("Playlist names can be at most 120 characters.", nameof(name));
        if (Document.Playlists.Any(p => p.Id != currentId && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A playlist with that name already exists.", nameof(name));
        return trimmed;
    }

    private MusicPlaylist FindPlaylist(string id) => Document.Playlists.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("The playlist no longer exists.", nameof(id));
    private MediaItem FindItem(string id) => Document.Items.FirstOrDefault(i => i.Id == id)
        ?? throw new ArgumentException("The media item no longer exists.", nameof(id));
    private static string NonBlank(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string NormalizePath(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    private static LibraryDocument CreateDocument() => new()
    { Playlists = [.. new[] { "Dinner", "Jazz", "Rock", "80's" }.Select(name => new MusicPlaylist { Name = name })] };

    private static LibraryDocument ReadDocument(string path)
    {
        using var stream = File.OpenRead(path);
        using var json = JsonDocument.Parse(stream);
        if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("SchemaVersion", out var version)
            && version.TryGetInt32(out var schemaVersion) && schemaVersion != 1) throw new UnsupportedLibraryVersionException();
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Items", out _)
            || !json.RootElement.TryGetProperty("Playlists", out _)) throw new InvalidDataException("Not a music library document.");
        var doc = json.Deserialize<LibraryDocument>(JsonOptions) ?? throw new InvalidDataException("Empty library document.");
        doc.PortableBasePath ??= "";
        if (doc.SchemaVersion != 1) throw new UnsupportedLibraryVersionException();
        if (doc.Items == null || doc.Folders == null || doc.Playlists == null) throw new InvalidDataException("Missing library collections.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in doc.Items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Path)
                || !ids.Add(item.Id) || !paths.Add(item.Path)) throw new InvalidDataException("Invalid or duplicate media item.");
            item.Title ??= ""; item.Artist ??= ""; item.Album ??= ""; item.Genre ??= "";
            item.TaggedAlbum ??= item.Album;
            item.AlbumOverride ??= ""; item.AlbumLookupSource ??= "";
            item.Album = item.AlbumOverride.Length > 0 ? item.AlbumOverride : item.TaggedAlbum;
            if (item.MusicBrainz is not null)
                try { item.MusicBrainz = NormalizeMusicBrainzInfo(item.MusicBrainz); }
                catch (ArgumentException) { item.MusicBrainz = null; }
        }
        if (doc.Playlists.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || p.ItemIds == null)
            || doc.Folders.Any(f => f == null || string.IsNullOrWhiteSpace(f.Path))) throw new InvalidDataException("Invalid folder or playlist.");
        foreach (var playlist in doc.Playlists) playlist.ItemIds = playlist.ItemIds.Where(ids.Contains).Distinct(StringComparer.Ordinal).ToList();
        return doc;
    }

    private sealed class ScanBatch
    {
        public List<MediaItem> Items { get; } = [];
        public HashSet<string> TagReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ImportResult Result { get; } = new();
    }
    private sealed record ScanEntry(string Path, string? Warning = null);
    private sealed class UnsupportedLibraryVersionException : IOException
    {
        public UnsupportedLibraryVersionException() : base("This library uses a newer or unsupported format. It was not changed.") { }
    }
}
