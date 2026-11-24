using System.Text.Json;

namespace MagicMusicWall.Core;

public sealed record LibraryRemovalPreview(int Tracks, int Playlists);

public sealed partial class LibraryCatalog
{
    private string? _removalBefore, _removalAfter;
    public bool CanUndoRemoval { get { lock (_gate) return _removalAfter is not null && Snapshot() == _removalAfter; } }
    private string Snapshot() => JsonSerializer.Serialize(Document, JsonOptions);

    public LibraryRemovalPreview PreviewFolderRemoval(string folderId)
    {
        lock (_gate) return PreviewRemoval(FolderRemovalIds(folderId));
    }

    public LibraryRemovalPreview PreviewAddedFilesRemoval()
    {
        lock (_gate) return PreviewRemoval(FolderBrowser.Browse(Document.Folders, Document.Items, FolderBrowser.LooseFiles).Files.Select(i => i.Id));
    }

    private string[] FolderRemovalIds(string folderId)
    {
        var folder = Document.Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder is null) return [];
        var candidates = FolderBrowser.Browse([folder], Document.Items, folder.Path).Descendants.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        return FolderBrowser.Browse(Document.Folders.Where(f => f.Id != folderId), Document.Items, FolderBrowser.LooseFiles).Files
            .Where(i => candidates.Contains(i.Id)).Select(i => i.Id).ToArray();
    }

    private LibraryRemovalPreview PreviewRemoval(IEnumerable<string> itemIds)
    {
        var ids = itemIds.ToHashSet(StringComparer.Ordinal);
        return new(Document.Items.Count(i => ids.Contains(i.Id)), Document.Playlists.Count(p => p.ItemIds.Any(ids.Contains)));
    }

    private void RemoveWithUndo(IEnumerable<string> itemIds, string? folderId = null)
    {
        var ids = itemIds.ToHashSet(StringComparer.Ordinal);
        var before = Snapshot();
        try
        {
            if (folderId is not null) Document.Folders.RemoveAll(f => f.Id == folderId);
            Document.Items.RemoveAll(i => ids.Contains(i.Id));
            foreach (var playlist in Document.Playlists) playlist.ItemIds.RemoveAll(ids.Contains);
            SaveLocked();
            _removalBefore = before; _removalAfter = Snapshot();
        }
        catch { Document = JsonSerializer.Deserialize<LibraryDocument>(before, JsonOptions)!; throw; }
    }

    /// <summary>Undo the last removal only while no intervening catalog edit would be overwritten.</summary>
    public bool UndoRemoval()
    {
        lock (_gate)
        {
            if (!CanUndoRemoval || _removalBefore is null) return false;
            var current = Document;
            try { Document = JsonSerializer.Deserialize<LibraryDocument>(_removalBefore, JsonOptions)!; SaveLocked(); }
            catch { Document = current; throw; }
            _removalBefore = _removalAfter = null;
            return true;
        }
    }

    /// <summary>Replace playlist contents with exact ordered occurrences of known tracks.</summary>
    public void SetPlaylistItems(string playlistId, IEnumerable<string> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        lock (_gate)
        {
            var playlist = EditablePlaylist(playlistId);
            var known = Document.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var previous = playlist.ItemIds;
            try { playlist.ItemIds = itemIds.Where(known.Contains).ToList(); SaveLocked(); }
            catch { playlist.ItemIds = previous; throw; }
        }
    }
}
