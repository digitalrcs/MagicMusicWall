using System.Text;

namespace MagicMusicWall.Core;

/// <summary>Local M3U/M3U8 interchange; occurrence order is preserved and network streams are not imported.</summary>
public static class PlaylistFile
{
    public static IReadOnlyList<string> ReadPaths(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Playlist is larger than 16 MB.");
        var result = new List<string>();
        foreach (var entry in File.ReadLines(path, Encoding.UTF8))
        {
            var line = entry.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (result.Count >= 100_000) throw new InvalidDataException("Playlist has more than 100,000 entries.");
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) throw new InvalidDataException("This playlist contains an online stream. Only local media files are supported.");
                line = uri.LocalPath;
            }
            var fullPath = Path.GetFullPath(line, directory);
            if (!LibraryCatalog.IsSupported(fullPath)) throw new InvalidDataException($"Unsupported media entry: {Path.GetFileName(fullPath)}");
            result.Add(fullPath);
        }
        return result;
    }

    public static void Write(string path, IEnumerable<MediaItem> items, bool relativePaths = true)
    {
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        var lines = new List<string> { "#EXTM3U" };
        foreach (var item in items)
        {
            var mediaPath = Path.GetFullPath(item.Path);
            if (mediaPath.Contains('\r') || mediaPath.Contains('\n')) throw new InvalidDataException("A media path contains a line break.");
            var seconds = double.IsFinite(item.DurationSeconds) && item.DurationSeconds >= 0 && item.DurationSeconds < int.MaxValue ? (int)item.DurationSeconds : -1;
            var caption = $"{item.Artist} - {item.Title}".Replace('\r', ' ').Replace('\n', ' ');
            lines.Add($"#EXTINF:{seconds},{caption}");
            var output = relativePaths ? Path.GetRelativePath(directory, mediaPath) : mediaPath;
            // A leading # denotes a comment in M3U, so make it explicitly relative.
            lines.Add(output.StartsWith('#') ? "." + Path.DirectorySeparatorChar + output : output);
        }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllLines(temporary, lines, new UTF8Encoding(false)); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
