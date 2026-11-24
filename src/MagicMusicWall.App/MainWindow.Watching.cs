using System.IO;
using System.Windows.Threading;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly List<FileSystemWatcher> _folderWatchers = [];
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _watchedRoots = "";
    private int _watchDirty;
    private DateTime _lastWatchScan;
    private void InitializeFolderWatching()
    {
        _watchTimer.Tick += async (_, _) =>
        {
            if (_closing || !IsEnabled || OwnedWindows.Cast<System.Windows.Window>().Any(w => w.IsVisible && w is SetupWindow or FolderWindow)) return;
            var roots = _settings.WatchFolders ? string.Join('\n', _catalog.Document.Folders.Select(f => f.Path).Where(Directory.Exists).Order()) : "";
            if (roots != _watchedRoots)
            {
                foreach (var watcher in _folderWatchers) watcher.Dispose(); _folderWatchers.Clear(); _watchedRoots = roots;
                Interlocked.Exchange(ref _watchDirty, 1);
                foreach (var root in roots.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                        watcher.Created += WatchChanged; watcher.Deleted += WatchChanged; watcher.Changed += WatchChanged; watcher.Renamed += WatchChanged;
                        watcher.Error += (_, _) => { Interlocked.Exchange(ref _watchDirty, 1); _watchedRoots = ""; };
                        watcher.EnableRaisingEvents = true; _folderWatchers.Add(watcher);
                    }
                    catch (Exception ex) { _watchedRoots = ""; Status("Folder monitoring unavailable: " + ex.Message); }
                }
            }
            if (!_settings.WatchFolders || _scanning || _metadataWriting || _queueChanging || DateTime.UtcNow - _lastWatchScan < TimeSpan.FromSeconds(15)) return;
            if (Interlocked.Exchange(ref _watchDirty, 0) == 0) return;
            _lastWatchScan = DateTime.UtcNow;
            try { await RescanLibrary(); } catch (Exception ex) { Status("Watched-folder rescan failed: " + ex.Message); }
        };
        _watchTimer.Start();
        Closed += (_, _) => { _watchTimer.Stop(); foreach (var watcher in _folderWatchers) watcher.Dispose(); _folderWatchers.Clear(); };
    }
    private void WatchChanged(object sender, FileSystemEventArgs e) => Interlocked.Exchange(ref _watchDirty, 1);
}
