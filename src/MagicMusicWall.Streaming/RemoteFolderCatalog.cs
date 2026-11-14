using System.Security.Cryptography;
using System.Text;
using MagicMusicWall.Core;

namespace MagicMusicWall.Streaming;

/// <summary>Maps opaque remote IDs to indexed folder structure without enumerating the filesystem.</summary>
public static class RemoteFolderCatalog
{
    public static FolderPage? Browse(IEnumerable<LibraryFolder> sources, IEnumerable<MediaItem> media, string? folderId)
    {
        var roots = sources.ToList();
        var items = media.OrderBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DiscNumber).ThenBy(item => item.TrackNumber)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (folderId is null) return FolderBrowser.Browse(roots, items, null);

        var normalizedRoots = roots.Select(root => Normalize(root.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in normalizedRoots) allowed[IdFor(root)] = root;
        var hasLooseFiles = false;
        foreach (var item in items)
        {
            var path = Normalize(item.Path);
            var containingRoots = normalizedRoots.Where(root => IsInside(path, root)).ToArray();
            if (containingRoots.Length == 0) { hasLooseFiles = true; continue; }
            foreach (var root in containingRoots)
            {
                var directory = Path.GetDirectoryName(path);
                while (directory is not null && (Same(directory, root) || IsInside(directory, root)))
                {
                    allowed[IdFor(directory)] = directory;
                    if (Same(directory, root)) break;
                    directory = Path.GetDirectoryName(directory);
                }
            }
        }
        if (hasLooseFiles) allowed[IdFor(FolderBrowser.LooseFiles)] = FolderBrowser.LooseFiles;
        return allowed.TryGetValue(folderId, out var selected) ? FolderBrowser.Browse(roots, items, selected) : null;
    }

    public static RemoteFolderPage ToRemote(FolderPage page) => new(DisplayName(page.Path, page.Title),
        page.Path is null ? null : IdFor(page.Path), page.Parent is null ? null : IdFor(page.Parent),
        page.Folders.Select(folder => new RemoteFolder(IdFor(folder.Path), DisplayName(folder.Path, folder.Name), folder.Count)).ToArray(),
        page.Files.Take(500).Select(item => new RemoteItem(item.Id, item.Title, item.Artist, item.Album, item.DurationText, item.IsVideo)).ToArray(),
        page.Files.Count, page.Descendants.Count);

    public static string IdFor(string path)
    {
        var normalized = path == FolderBrowser.LooseFiles ? path : Normalize(path);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())));
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string DisplayName(string? path, string name) => path is not null && Same(path, name)
        ? path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ? $"Drive {char.ToUpperInvariant(path[0])}" : "Shared folder"
        : name;
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsInside(string path, string root) => path.StartsWith(
        Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
