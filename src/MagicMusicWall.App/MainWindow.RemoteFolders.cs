using System.Windows.Threading;
using MagicMusicWall.Core;
using MagicMusicWall.Streaming;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private FolderPage RemoteFolderSelection(string? folderId) => RemoteFolderCatalog.Browse(
        _catalog.Document.Folders, _catalog.Document.Items, folderId)
        ?? throw new KeyNotFoundException("This folder is no longer in the library.");

    private sealed partial class RemoteBridge : IRemoteFolders
    {
        public Task<RemoteFolderPage?> GetFoldersAsync(string? folderId, CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync(() =>
        {
            if (main._closing || main._scanning) throw new InvalidOperationException("Library is busy. Try again shortly.");
            var page = RemoteFolderCatalog.Browse(main._catalog.Document.Folders, main._catalog.Document.Items, folderId);
            return page is null ? null : RemoteFolderCatalog.ToRemote(page);
        }, DispatcherPriority.Background, cancellationToken).Task;
    }
}
