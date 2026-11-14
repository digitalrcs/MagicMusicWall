using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private string? _folderPath;
    private bool _folderHomeExplicit;
    private FolderPage? _folderPage;
    private IReadOnlyList<MediaItem>? _folderPlaybackItems;

    private IEnumerable<MediaItem> ConfigureFolders(IEnumerable<MediaItem> source)
    {
        _folderPage = null; _folderPlaybackItems = null;
        FolderNavigation.Visibility = CurrentPlaylist is null ? Visibility.Visible : Visibility.Collapsed;
        PlayResultsButton.Content = CurrentPlaylist is null ? "▶  Play results"
            : string.IsNullOrWhiteSpace(SearchBox.Text) ? "▶  Play playlist" : "▶  Play matches";
        var items = source.ToList();
        FolderBreadcrumb.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "All tracks" : "Searching all folders";
        FolderBreadcrumb.ToolTip = FolderBreadcrumb.Text;
        FolderUpButton.IsEnabled = false;
        FoldersViewButton.Style = (Style)FindResource(_settings.CollectionGridView ? typeof(Button) : "AccentButton");
        GridViewButton.Style = (Style)FindResource(_settings.CollectionGridView ? "AccentButton" : typeof(Button));
        if (CurrentPlaylist is not null || !string.IsNullOrWhiteSpace(SearchBox.Text) || _settings.CollectionGridView) return items;
        if (_folderPath is null && !_folderHomeExplicit)
        {
            var home = FolderBrowser.Browse(_catalog.Document.Folders, items, null);
            if (home.Folders.Count == 1 && home.Folders[0].Path != FolderBrowser.LooseFiles) _folderPath = home.Folders[0].Path;
        }
        try { _folderPage = FolderBrowser.Browse(_catalog.Document.Folders, items, _folderPath); }
        catch (ArgumentException) { _folderPath = null; _folderHomeExplicit = true; _folderPage = FolderBrowser.Browse(_catalog.Document.Folders, items, null); }
        _folderPlaybackItems = _folderPage.Descendants;
        FolderBreadcrumb.Text = _folderPath == FolderBrowser.LooseFiles ? "Added files" : _folderPath ?? "Library folders";
        FolderBreadcrumb.ToolTip = FolderBreadcrumb.Text;
        FolderUpButton.IsEnabled = _folderPath is not null;
        PlayResultsButton.Content = _folderPath is null ? "▶  Play collection" : "▶  Play folder";
        return _folderPage.Files;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) OpenFolder(path);
    }
    private void OpenFolder(string path)
    {
        if (!_ready || _scanning) return;
        _folderPath = path; _folderHomeExplicit = true; SetCollectionView(false);
    }
    private void FolderHome_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) return;
        _folderPath = null; _folderHomeExplicit = true; SetCollectionView(false);
    }
    private void FolderUp_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _scanning) return;
        _folderPath = _folderPage?.Parent; _folderHomeExplicit = true; RefreshView();
    }
    private void QueueItem_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging || _closing || sender is not Button { Tag: MediaItem item }) return;
        _queue.Add(item);
        Status($"Added {item.Title} to the queue. {_queue.Count:N0} in queue.");
    }
    private async void PlayItem_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging || _closing || sender is not Button { Tag: MediaItem item }) return;
        await StartQueue([item]);
    }
    private void ItemInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || sender is not Button { Tag: MediaItem item }) return;
        ShowSongInfo(item);
    }
    private void QueueFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging || _closing || sender is not Button { Tag: FolderEntry folder } || !CanEdit()) return;
        var items = _catalog.Document.Items.Where(i => _view == "all" || i.IsVideo == (_view == "video"))
            .OrderBy(i => i.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber).ThenBy(i => i.Title);
        FolderPage page;
        try { page = FolderBrowser.Browse(_catalog.Document.Folders, items, folder.Path); }
        catch (ArgumentException)
        {
            Status("This folder is no longer available in the library. Refresh the collection and try again.");
            return;
        }
        foreach (var item in page.Descendants) _queue.Add(item);
        Status(page.Descendants.Count == 0 ? $"No items to queue in {folder.Name}."
            : $"Added {page.Descendants.Count:N0} items from {folder.Name}. Queue contains {_queue.Count:N0} items.");
    }
    private void FoldersView_Click(object sender, RoutedEventArgs e) => SetCollectionView(false);
    private void GridView_Click(object sender, RoutedEventArgs e) => SetCollectionView(true);
    private void SetCollectionView(bool grid)
    {
        if (!_ready || _scanning) return;
        _settings.CollectionGridView = grid;
        ScheduleSettingsSave();
        if (!grid) SearchBox.Clear();
        RefreshView();
    }
}
