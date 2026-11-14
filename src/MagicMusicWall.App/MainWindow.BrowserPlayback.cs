using System.Windows.Threading;
using MagicMusicWall.Streaming;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private sealed partial class RemoteBridge : IRemoteMedia
    {
        public Task<RemoteMediaFile?> GetMediaFileAsync(string id, CancellationToken cancellationToken) =>
            main.Dispatcher.InvokeAsync(() =>
            {
                if (main._closing || !main._settings.BrowserPlaybackEnabled) return null;
                if (main._scanning) throw new InvalidOperationException("The library is busy. Try again shortly.");
                var item = main._catalog.Document.Items.FirstOrDefault(item => item.Id == id);
                return item is null || !item.IsAvailable ? null : new RemoteMediaFile(item.Path);
            }, DispatcherPriority.Background, cancellationToken).Task;
    }
}
