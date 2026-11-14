namespace MagicMusicWall.Streaming;

public interface IRemoteFolders
{
    /// <summary>Null selects the collection home. Return null for an unknown opaque folder ID.</summary>
    Task<RemoteFolderPage?> GetFoldersAsync(string? folderId, CancellationToken cancellationToken);
}

public sealed record RemoteFolder(string Id, string Name, int Count);
public sealed record RemoteFolderPage(string Title, string? Id, string? ParentId,
    IReadOnlyList<RemoteFolder> Folders, IReadOnlyList<RemoteItem> Items, int TotalItems, int DescendantCount);
