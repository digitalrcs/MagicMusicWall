namespace MagicMusicWall.Core;

public sealed partial class LibraryCatalog
{
    /// <summary>Rebase only files inside a moved portable application folder, preserving IDs and outside paths.</summary>
    public int PreparePortableLibrary(string applicationDirectory)
    {
        var currentBase = NormalizePath(applicationDirectory);
        lock (_gate)
        {
            var previousBase = Document.PortableBasePath;
            if (SamePath(previousBase, currentBase)) return 0;
            if (previousBase.Length > 0 && !System.IO.Path.IsPathFullyQualified(previousBase))
                throw new InvalidDataException("The saved portable folder is invalid. Restore the library backup before moving it again.");
            var normalizedBase = previousBase.Length > 0 ? NormalizePath(previousBase) : "";
            var previousPrefix = normalizedBase.Length == 0 ? "" : System.IO.Path.EndsInDirectorySeparator(normalizedBase)
                ? normalizedBase : normalizedBase + System.IO.Path.DirectorySeparatorChar;
            string Map(string path)
            {
                if (previousPrefix.Length == 0) return path;
                var normalized = NormalizePath(path);
                if (SamePath(normalized, normalizedBase)) return currentBase;
                return normalized.StartsWith(previousPrefix, StringComparison.OrdinalIgnoreCase)
                    ? System.IO.Path.Combine(currentBase, normalized[previousPrefix.Length..]) : path;
            }
            var itemChanges = Document.Items.Select(item => (Item: item, OldPath: item.Path, OldAvailable: item.IsAvailable, NewPath: Map(item.Path))).ToList();
            var folderChanges = Document.Folders.Select(folder => (Folder: folder, OldPath: folder.Path, NewPath: Map(folder.Path))).ToList();
            if (itemChanges.Select(c => c.NewPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != itemChanges.Count
                || folderChanges.Select(c => c.NewPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != folderChanges.Count)
                throw new IOException("Moving this portable library would create duplicate file or folder paths. The saved library was kept unchanged.");
            try
            {
                foreach (var change in itemChanges) { change.Item.Path = change.NewPath; change.Item.IsAvailable = File.Exists(change.NewPath); }
                foreach (var change in folderChanges) change.Folder.Path = change.NewPath;
                Document.PortableBasePath = currentBase;
                SaveLocked();
            }
            catch
            {
                foreach (var change in itemChanges) { change.Item.Path = change.OldPath; change.Item.IsAvailable = change.OldAvailable; }
                foreach (var change in folderChanges) change.Folder.Path = change.OldPath;
                Document.PortableBasePath = previousBase;
                throw;
            }
            return itemChanges.Count(c => !SamePath(c.OldPath, c.NewPath)) + folderChanges.Count(c => !SamePath(c.OldPath, c.NewPath));
        }
    }
}
