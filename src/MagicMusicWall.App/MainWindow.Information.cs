using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly MusicLookupService _musicLookup = new();
    private readonly SongFactsService _songFacts = new();

    private void Info_Click(object sender, RoutedEventArgs e) => ShowSongInfo(MediaGrid.SelectedItem as MediaItem ?? _current);
    private void NowInfo_Click(object sender, RoutedEventArgs e) => ShowSongInfo(_current);
    private void QueueList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(QueueList, source) as ListBoxItem : null;
        if (row is not null && row.DataContext is MediaItem)
        {
            row.IsSelected = true;
            row.Focus();
        }
        else QueueList.SelectedIndex = -1;
    }
    private void QueueList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var visibility = QueueList.SelectedItem is MediaItem ? Visibility.Visible : Visibility.Collapsed;
        QueueInfoMenuItem.Visibility = visibility;
        QueueMetadataMenuItem.Visibility = visibility;
        QueueInfoSeparator.Visibility = visibility;
    }
    private void QueueInfo_Click(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is MediaItem item) ShowSongInfo(item);
    }
    private void ShowSongInfo(MediaItem? item)
    {
        if (item is null) { Status("Select a song, or start playing one, to view its information."); return; }
        if (!CanEdit()) return;
        if (item.IsVideo) { ShowMetadata(item); return; }
        new SongInfoWindow(item, _musicLookup, _songFacts, match =>
        {
            if (!CanEdit()) throw new InvalidOperationException("Wait for the library scan to finish.");
            if (match is null) _catalog.ClearAlbumOverride(item.Id);
            else _catalog.SetAlbumOverride(item.Id, match.Album, match.SourceUrl);
            RefreshView();
        }, owner => ShowMetadata(item, owner)) { Owner = this }.ShowDialog();
    }
}
