namespace MagicMusicWall.Core;

public sealed record FolderEntry(string Path, string Name, int Count)
{
    public string Detail => $"{Count:N0} {(Count == 1 ? "item" : "items")}";
}
public sealed record FolderPage(string? Path, string Title, string? Parent, IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<MediaItem> Files, IReadOnlyList<MediaItem> Descendants);

/// <summary>Browse only indexed media; an offline source still keeps its folder structure.</summary>
public static class FolderBrowser
{
    public const string LooseFiles = "::loose-files::";
    public static FolderPage Browse(IEnumerable<LibraryFolder> sources, IEnumerable<MediaItem> media, string? path)
    {
        var roots = sources.Select(f => Normalize(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = media.ToList();
        var loose = items.Where(i => !roots.Any(r => IsInside(i.Path, r))).ToList();
        if (path is null)
        {
            var folders = roots.Select(r => new FolderEntry(r, Name(r), items.Count(i => IsInside(i.Path, r)))).ToList();
            if (loose.Count > 0) folders.Add(new(LooseFiles, "Added files", loose.Count));
            return new(null, "Your folders", null, folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), [], items);
        }
        if (path == LooseFiles) return new(path, "Added files", null, [], loose, loose);
        var current = Normalize(path);
        if (!roots.Any(r => Same(r, current) || IsInside(current, r)))
            throw new ArgumentException("Choose a folder in the library.", nameof(path));
        var descendants = items.Where(i => IsInside(i.Path, current)).ToList();
        var direct = new List<MediaItem>();
        var children = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in descendants)
        {
            var relative = System.IO.Path.GetRelativePath(current, item.Path);
            var separator = relative.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
            if (separator < 0) direct.Add(item);
            else
            {
                var child = System.IO.Path.Combine(current, relative[..separator]);
                children[child] = children.GetValueOrDefault(child) + 1;
            }
        }
        var parent = roots.Any(r => Same(r, current)) ? null : System.IO.Path.GetDirectoryName(current);
        return new(current, Name(current), parent,
            children.Select(c => new FolderEntry(c.Key, Name(c.Key), c.Value)).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), direct, descendants);
    }
    private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool IsInside(string candidate, string directory) => Normalize(candidate).StartsWith(
        System.IO.Path.EndsInDirectorySeparator(directory) ? directory : directory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Name(string path) => System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path;
}
