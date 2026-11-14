using System.Text.Json.Serialization;

namespace MagicMusicWall.Core;

public sealed class MediaItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    // Null distinguishes an older catalog from a file whose album tag is empty.
    public string? TaggedAlbum { get; set; }
    public string AlbumOverride { get; set; } = "";
    public string AlbumLookupSource { get; set; } = "";
    public string Genre { get; set; } = "";
    public uint Year { get; set; }
    public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsVideo { get; set; }
    public bool IsAvailable { get; set; } = true;

    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);

    [JsonIgnore]
    public string DurationText => DurationSeconds <= 0 || !double.IsFinite(DurationSeconds)
        ? "—"
        : DurationSeconds >= 3600
            ? TimeSpan.FromSeconds(DurationSeconds).ToString(@"h\:mm\:ss")
            : TimeSpan.FromSeconds(DurationSeconds).ToString(@"m\:ss");
}

public sealed class LibraryFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
}

public sealed class MusicPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> ItemIds { get; set; } = [];
}

public sealed class LibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string PortableBasePath { get; set; } = "";
    public List<MediaItem> Items { get; set; } = [];
    public List<LibraryFolder> Folders { get; set; } = [];
    public List<MusicPlaylist> Playlists { get; set; } = [];
}

public sealed class ImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; set; } = [];
    internal void Warn(string message)
    {
        if (Warnings.Count < 30) Warnings.Add(message);
        else if (Warnings.Count == 30) Warnings.Add("Additional import warnings were omitted.");
    }
}
