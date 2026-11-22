using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private void OpenPlaylist(MusicPlaylist playlist)
    {
        _updatingPlaylistControls = true;
        try
        {
            _playlistId = playlist.Id;
            ViewsList.SelectedItem = null;
            PlaylistsList.SelectedItem = playlist;
            PlaylistDestinationBox.SelectedItem = playlist;
            SearchBox.Clear();
        }
        finally { _updatingPlaylistControls = false; }
        RefreshView();
    }

    private async void PlaylistsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_ready || _scanning || _closing || _queueChanging || e.ChangedButton != MouseButton.Left) return;
        var row = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(PlaylistsList, source) as ListBoxItem : null;
        if (row?.DataContext is not MusicPlaylist playlist) return;
        OpenPlaylist(playlist);
        await StartQueue(_viewResults.ToList());
    }

    private void QueuePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || _queueChanging || _scanning || CurrentPlaylist is null) return;
        foreach (var item in _viewResults) _queue.Add(item);
        Status($"Added {_viewResults.Count} songs from {CurrentPlaylist.Name} to the queue.");
    }

    private void EmptyLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _scanning) return;
        if (_catalog.Document.Items.Count == 0) { ManageFolders_Click(sender, e); return; }
        _updatingPlaylistControls = true;
        try
        {
            _playlistId = null;
            PlaylistsList.SelectedItem = null;
            ViewsList.SelectedItem = ViewsList.Items.OfType<ListBoxItem>().First(i => (string)i.Tag == _view);
            SearchBox.Clear();
        }
        finally { _updatingPlaylistControls = false; }
        RefreshView();
    }
}
