using System.IO;
using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;
using Microsoft.Win32;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private void ToggleSelectedFavorites()
    {
        if (!CanEdit()) return;
        var selected = Selection(); if (selected.Count == 0) { Status("Select songs first."); return; }
        try { var favorite = selected.Any(i => !i.Favorite); foreach (var item in selected) _catalog.SetFavorite(item.Id, favorite); RefreshView(); Status(favorite ? "Selected songs marked as favorites." : "Favorites removed from selected songs."); }
        catch (Exception ex) { Error(ex); }
    }
    private void CreateSmartPlaylist()
    {
        if (!CanEdit()) return;
        var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 340 };
        panel.Children.Add(Ui.Text("Playlist name")); var name = new TextBox { Text = "My smart playlist" }; panel.Children.Add(name);
        var favorites = new CheckBox { Content = "Favorites only", Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(favorites);
        var unplayed = new CheckBox { Content = "Unplayed only", Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(unplayed);
        var recent = new CheckBox { Content = "Played in the last 30 days", Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(recent);
        panel.Children.Add(Ui.Text("Genre (blank for any)")); var genre = new TextBox(); panel.Children.Add(genre);
        panel.Children.Add(Ui.Text("All selected rules must match. Results update when opened."));
        var dialog = new Window { Title = "Create smart playlist", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40), Owner = this, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Theme(dialog);
        panel.Children.Add(Ui.Button("Create playlist", (_, _) =>
        {
            try { var playlist = _catalog.CreateSmartPlaylist(name.Text, new SmartPlaylistRule { FavoritesOnly = favorites.IsChecked == true, UnplayedOnly = unplayed.IsChecked == true, PlayedWithinDays = recent.IsChecked == true ? 30 : null, Genre = genre.Text }); RefreshPlaylists(playlist.Id); OpenPlaylist(playlist); dialog.DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Could not create playlist"); }
        }));
        dialog.ShowDialog();
    }
    private async void ImportPlaylistFile_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var picker = new OpenFileDialog { Title = "Import local playlist", Filter = "M3U playlists|*.m3u;*.m3u8" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var paths = PlaylistFile.ReadPaths(picker.FileName);
            await ImportPaths(paths.Distinct(StringComparer.OrdinalIgnoreCase), false);
            var lookup = _catalog.Document.Items.ToDictionary(i => Path.GetFullPath(i.Path), StringComparer.OrdinalIgnoreCase);
            var ids = paths.Where(lookup.ContainsKey).Select(p => lookup[p].Id).ToArray();
            var playlist = _catalog.CreatePlaylist(Path.GetFileNameWithoutExtension(picker.FileName));
            _catalog.SetPlaylistItems(playlist.Id, ids);
            RefreshPlaylists(playlist.Id); OpenPlaylist(playlist);
            Status($"Imported {ids.Length} playlist entries; {paths.Count - ids.Length} unavailable entries skipped.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private void ExportPlaylistFile_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlaylist is not { } playlist) { Status("Select a saved playlist to export."); return; }
        var picker = new SaveFileDialog { Title = "Export playlist", Filter = "UTF-8 playlist|*.m3u8", FileName = "playlist.m3u8" };
        if (picker.ShowDialog(this) != true) return;
        try { PlaylistFile.Write(picker.FileName, _catalog.PlaylistItems(playlist.Id)); Status("Playlist exported. Original media files were not copied or changed."); }
        catch (Exception ex) { Error(ex); }
    }
    private void ShowLibraryHealth()
    {
        if (!CanEdit()) return;
        var report = _catalog.InspectHealth();
        var panel = new DockPanel { Margin = new Thickness(20) };
        var summary = Ui.Text($"{report.Unavailable.Count} unavailable · {report.MissingMetadata.Count} missing metadata · {report.PossibleDuplicates.Count} possible duplicate groups");
        summary.TextWrapping = TextWrapping.Wrap; DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var list = new ListBox { ItemsSource = report.Unavailable, DisplayMemberPath = "Path", MinHeight = 220 }; panel.Children.Add(list);
        var dialog = new Window { Title = "Library health · original files are never deleted", Content = panel, Width = 850, Height = 480, MinWidth = 500, MinHeight = 330, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Theme(dialog);
        actions.Children.Add(Ui.Button("Unavailable files", (_, _) => list.ItemsSource = report.Unavailable));
        actions.Children.Add(Ui.Button("Missing metadata", (_, _) => list.ItemsSource = report.MissingMetadata));
        actions.Children.Add(Ui.Button("Possible duplicates", (_, _) => { list.ItemsSource = report.PossibleDuplicates.SelectMany(g => g).ToList(); summary.Text = "Similar title, artist and duration — review before removing. These are not verified identical audio files."; }));
        actions.Children.Add(Ui.Button("Relink selected missing file…", (_, _) =>
        {
            if (list.SelectedItem is not MediaItem item || File.Exists(item.Path)) return;
            var picker = new OpenFileDialog { Title = "Choose the moved media file", Filter = "Media file|*.*" };
            if (picker.ShowDialog(dialog) != true) return;
            if (MessageBox.Show(dialog, $"Update this library entry?\n\n{item.Path}\n→ {picker.FileName}\n\nPlaylists keep the same entry. Tags will not be written.", "Confirm relink", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { _catalog.RelinkItem(item.Id, picker.FileName); RefreshView(); dialog.Close(); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Could not relink"); }
        }));
        dialog.ShowDialog();
    }
}
