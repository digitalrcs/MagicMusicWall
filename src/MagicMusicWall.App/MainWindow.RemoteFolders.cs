using System.Windows.Threading;
using MagicMusicWall.Core;
using MagicMusicWall.Streaming;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private FolderPage RemoteFolderSelection(string? folderId) => RemoteFolderCatalog.Browse(
        _catalog.Document.Folders, _catalog.Document.Items, folderId)
        ?? throw new KeyNotFoundException("This folder is no longer in the library.");

    private List<MediaItem> ResolveQueueSelection(RemoteCommand command)
    {
        if (command.Action is "queue-folder" or "resolve-folder")
            return RemoteFolderSelection(command.FolderId).Descendants.DistinctBy(item => item.Id).ToList();
        var items = new List<MediaItem>();
        foreach (var id in command.FolderIds ?? []) items.AddRange(RemoteFolderSelection(id).Descendants);
        var lookup = _catalog.Document.Items.ToDictionary(item => item.Id);
        foreach (var id in command.ItemIds ?? [])
            items.Add(lookup.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("Selected item is no longer in the library."));
        return items.DistinctBy(item => item.Id).ToList();
    }

    private sealed partial class RemoteBridge : IRemoteFolders, IRemoteQueueSelection
    {
        public Task<IReadOnlyList<RemoteItem>> ResolveQueueItemsAsync(RemoteCommand command, CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync<IReadOnlyList<RemoteItem>>(() =>
        {
            if (main._closing || main._scanning) throw new InvalidOperationException("Library is busy. Try again shortly.");
            return main.ResolveQueueSelection(command).Select(ToRemote).ToList();
        }, DispatcherPriority.Background, cancellationToken).Task;

        public Task<RemoteFolderPage?> GetFoldersAsync(string? folderId, CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync(() =>
        {
            if (main._closing || main._scanning) throw new InvalidOperationException("Library is busy. Try again shortly.");
            var page = RemoteFolderCatalog.Browse(main._catalog.Document.Folders, main._catalog.Document.Items, folderId);
            return page is null ? null : RemoteFolderCatalog.ToRemote(page);
        }, DispatcherPriority.Background, cancellationToken).Task;
    }
}
