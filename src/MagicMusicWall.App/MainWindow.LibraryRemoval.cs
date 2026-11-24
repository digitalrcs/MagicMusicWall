using System.Windows;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private bool CanRemoveLibraryItems()
    {
        if (_closing || _queueChanging) { Status("Please wait for the player operation to finish."); return false; }
        return CanEdit();
    }

    private async Task ReconcileRemovedLibraryItemsAsync()
    {
        _queueChanging = true;
        try
        {
            var known = _catalog.Document.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            if (_current is not null && !known.Contains(_current.Id))
            {
                ++_playVersion;
                await Playback(() => { _player.Stop(); return true; });
                _current = null; _queueIndex = -1; _pendingSessionPosition = null;
                NowTitle.Text = "Nothing playing yet";
                NowArtist.Text = "Choose a queued track to play";
            }
            for (var index = _queue.Count - 1; index >= 0; index--)
            {
                if (known.Contains(_queue[index].Id)) continue;
                _queue.RemoveAt(index);
                if (index < _queueIndex) _queueIndex--;
                else if (index == _queueIndex) _queueIndex = -1;
            }
            RefreshPlaylists(); RefreshView();
            Status("Library entries removed. Files on disk were kept.");
        }
        finally { _queueChanging = false; }
    }
}
