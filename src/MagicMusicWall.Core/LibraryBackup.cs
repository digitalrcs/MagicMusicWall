using System.IO.Compression;
using System.Text.Json;

namespace MagicMusicWall.Core;

/// <summary>Private library/settings/session backups, never media files. Restore requires the app to stop writing first.</summary>
public static class LibraryBackup
{
    private static readonly string[] Names = ["library.json", "settings.json", "session.json"];
    private const long MaxBytes = 256L * 1024 * 1024;

    public static void Create(string dataDirectory, string archivePath)
    {
        var target = Path.GetFullPath(archivePath);
        if (Names.Any(name => string.Equals(target, Path.GetFullPath(Path.Combine(dataDirectory, name)), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose a separate ZIP file for the backup.", nameof(archivePath));
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var name in Names)
                {
                    var source = Path.Combine(dataDirectory, name);
                    if (File.Exists(source)) archive.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
                }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static IReadOnlyList<string> Inspect(string archivePath) => ReadValidated(archivePath).Keys.ToArray();

    /// <summary>Restore only included supported files. Returns the retained pre-restore backup path. Caller must close/reload app state.</summary>
    public static string Restore(string dataDirectory, string archivePath)
    {
        var entries = ReadValidated(archivePath); // Validate the entire archive before touching existing data.
        Directory.CreateDirectory(dataDirectory);
        var directory = Path.GetFullPath(dataDirectory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Restore requires a regular application data directory.");
        foreach (var name in entries.Keys)
        {
            var target = Path.Combine(directory, name);
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Restore will not replace linked application data files.");
        }
        var backup = Path.Combine(directory, $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        Create(directory, backup);
        var originals = entries.Keys.ToDictionary(name => name, name => File.Exists(Path.Combine(directory, name)) ? File.ReadAllBytes(Path.Combine(directory, name)) : null);
        var replaced = new List<string>();
        try
        {
            foreach (var (name, bytes) in entries) { Replace(Path.Combine(directory, name), bytes); replaced.Add(name); }
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var name in replaced.AsEnumerable().Reverse())
                try { if (originals[name] is { } old) Replace(Path.Combine(directory, name), old); else File.Delete(Path.Combine(directory, name)); }
                catch (Exception rollback) { failures.Add(rollback); }
            if (failures.Count > 1) throw new AggregateException($"Restore failed. Recover the retained backup at {backup} before reopening the app.", failures);
            throw;
        }
        return backup;
    }

    private static void Replace(string target, byte[] bytes)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static Dictionary<string, byte[]> ReadValidated(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (!Names.Contains(entry.FullName, StringComparer.Ordinal) || result.ContainsKey(entry.FullName))
                throw new InvalidDataException("Backup contains unsupported paths or duplicate entries.");
            if (entry.Length > MaxBytes || (total += entry.Length) > MaxBytes) throw new InvalidDataException("Backup is larger than the 256 MB restore limit.");
            using var source = entry.Open(); using var target = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count = source.Read(buffer)) > 0)
            {
                if (target.Length + count > entry.Length || target.Length + count > MaxBytes) throw new InvalidDataException("Backup entry exceeds its declared size.");
                target.Write(buffer, 0, count);
            }
            var bytes = target.ToArray();
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Backup entries must be JSON objects.");
            if (entry.FullName == "library.json")
            {
                var doc = JsonSerializer.Deserialize<LibraryDocument>(bytes) ?? throw new InvalidDataException("Missing library.");
                if (doc.SchemaVersion != 1 || doc.Items is null || doc.Folders is null || doc.Playlists is null
                    || !json.RootElement.TryGetProperty("Items", out _) || !json.RootElement.TryGetProperty("Playlists", out _)
                    || doc.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Path))
                    || doc.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != doc.Items.Count
                    || doc.Items.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != doc.Items.Count
                    || doc.Folders.Any(f => f is null || string.IsNullOrWhiteSpace(f.Path))
                    || doc.Playlists.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.ItemIds is null))
                    throw new InvalidDataException("Backup library is invalid or uses an unsupported version.");
                foreach (var playlist in doc.Playlists)
                    if (playlist.Rule is { } rule) _ = SmartPlaylists.Evaluate([], rule);
            }
            else if (entry.FullName == "session.json")
            {
                var session = JsonSerializer.Deserialize<ListeningSession>(bytes);
                if (session is null || session.Version != 1 || session.ItemIds is null || session.ItemIds.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Invalid listening session.");
            }
            else _ = JsonSerializer.Deserialize<AppPreferences>(bytes) ?? throw new InvalidDataException("Invalid settings.");
            result.Add(entry.FullName, bytes);
        }
        if (!result.ContainsKey("library.json")) throw new InvalidDataException("Backup must contain library.json.");
        return result;
    }
}
