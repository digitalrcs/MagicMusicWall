using System.IO;
using System.Windows;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim _fileOpenGate = new(1, 1);

    internal async Task OpenFilesAsync(string[] paths)
    {
        await _fileOpenGate.WaitAsync();
        try
        {
            if (_closing) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) throw new IOException($"File unavailable: {path}");
                if (!LibraryCatalog.IsSupported(path)) throw new IOException($"Unsupported media file: {path}");
            }
            if (paths.Length == 0) return;
            // Use the normal importer so tags, saved queues and browser playback all
            // refer to catalog items. Never add shell-opened files to the selected playlist.
            while (_scanning || _metadataWriting || _queueChanging)
            {
                if (_closing) return;
                await Task.Delay(100);
            }
            await ImportPaths(paths, addToCurrentPlaylist: false);
            if (_closing) return;
            var items = paths.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => _catalog.Document.Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                .OfType<MediaItem>().ToList();
            if (items.Count == 0) return;
            // Decide after import: the user may have changed the queue or setting
            // while tags were being read. Appending must not alter playback state.
            if (_settings.QueueExternalFiles && _queue.Count > 0)
            {
                foreach (var item in items) _queue.Add(item);
                Status($"Added {items.Count} {(items.Count == 1 ? "file" : "files")} to the queue.");
            }
            else await StartQueue(items);
        }
        catch (Exception ex) { Error(ex); }
        finally { _fileOpenGate.Release(); }
    }
}
