using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed class ListeningSession
{
    public const long MaximumPositionMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;
    public int Version { get; set; } = 1;
    public List<string> ItemIds { get; set; } = [];
    public int CurrentIndex { get; set; } = -1;
    public long PositionMilliseconds { get; set; }
    public bool WasPlaying { get; set; }

    public ListeningSession Resolve(IEnumerable<string> catalogIds)
    {
        var known = catalogIds.ToHashSet(StringComparer.Ordinal);
        var surviving = ItemIds.Select((id, index) => (id, index)).Where(x => known.Contains(x.id)).ToList();
        var validCurrent = CurrentIndex >= 0 && CurrentIndex < ItemIds.Count;
        var current = validCurrent ? surviving.FindIndex(x => x.index == CurrentIndex) : -1;
        var same = current >= 0;
        if (validCurrent && current < 0 && surviving.Count > 0)
        {
            current = surviving.FindIndex(x => x.index > CurrentIndex);
            if (current < 0) current = surviving.Count - 1;
        }
        return new()
        {
            ItemIds = surviving.Select(x => x.id).ToList(), CurrentIndex = current,
            PositionMilliseconds = same ? Math.Clamp(PositionMilliseconds, 0, MaximumPositionMilliseconds) : 0,
            WasPlaying = same && WasPlaying
        };
    }
}

/// <summary>Atomic local session checkpoints. Queue entries reference catalog IDs, never resurrect removed files.</summary>
public sealed class ListeningSessionStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "session.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string? LoadWarning { get; private set; }

    public ListeningSession? Load()
    {
        LoadWarning = null;
        try
        {
            var main = Read(_path);
            if (main is not null) return main;
            var backup = Read(_path + ".bak");
            if (backup is not null) LoadWarning = "The last listening session was recovered from its backup.";
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                var backup = Read(_path + ".bak");
                LoadWarning = backup is not null ? "The last listening session was recovered from its backup."
                    : "The listening session could not be read. Your library is unchanged.";
                return backup;
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException)
            { LoadWarning = "The listening session could not be read. Your library is unchanged."; return null; }
        }
    }

    private static ListeningSession? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var session = JsonSerializer.Deserialize<ListeningSession>(File.ReadAllText(path), Options);
        if (session is null || session.Version != 1 || session.ItemIds is null || session.ItemIds.Any(string.IsNullOrWhiteSpace))
            throw new JsonException("Invalid listening session.");
        return session;
    }

    public void Save(ListeningSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, session, Options); stream.Flush(true); }
            if (File.Exists(_path))
            {
                try { Read(_path); File.Copy(_path, _path + ".bak", true); }
                catch (JsonException) { File.Copy(_path, _path + ".corrupt." + Guid.NewGuid().ToString("N"), false); }
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
