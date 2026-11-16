using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

/// <summary>Selection is restricted to indexed music; no disk enumeration or folder import.</summary>
internal sealed class AiLibraryPicker : Window
{
    private readonly LibraryFolder[] _folders;
    private readonly MediaItem[] _songs;
    private readonly HashSet<string> _selected;
    private readonly List<(CheckBox Check, string[] Ids)> _checks = [];
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _location = Ui.Text("Your library folders", 18);
    private readonly TextBlock _count = Ui.Text("", 13, true);
    private string? _path;
    public IReadOnlyList<string> SelectedIds => _songs.Where(x => _selected.Contains(x.Id)).Select(x => x.Id).ToArray();

    public AiLibraryPicker(IEnumerable<LibraryFolder> folders, MediaItem[] songs, IReadOnlyList<string> selected)
    {
        Ui.Theme(this);
        _folders = folders.ToArray(); _songs = songs;
        _selected = selected.Intersect(songs.Select(x => x.Id)).ToHashSet();
        Title = "Choose library songs and folders"; Width = 820; Height = 700; MinWidth = 620; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(22) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Ui.Text("Choose songs or entire folders", 25));
        top.Children.Add(Ui.Text("Only music already added to your library appears here. Checking a folder includes its subfolders. Selections stay checked as you browse.", 13, true));
        top.Children.Add(Ui.Text("Search all indexed songs by title, artist or album", 13, true));
        top.Children.Add(_search);
        var navigation = new WrapPanel(); top.Children.Add(navigation);
        navigation.Children.Add(Ui.Button("Your folders", (_, _) => { _path = null; _search.Clear(); Refresh(); }));
        navigation.Children.Add(Ui.Button("Up", (_, _) => { _path = FolderBrowser.Browse(_folders, _songs, _path).Parent; _search.Clear(); Refresh(); }));
        navigation.Children.Add(Ui.Button("Clear selection", (_, _) => { _selected.Clear(); Refresh(); }));
        top.Children.Add(_location); top.Children.Add(_count);
        var footer = new WrapPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(Ui.Button("Use selected songs", (_, _) => { DialogResult = true; }));
        footer.Children.Add(Ui.Button("Cancel", (_, _) => { DialogResult = false; }));
        root.Children.Add(_list);
        _search.TextChanged += (_, _) => Refresh();
        Refresh();
    }
    private void Refresh()
    {
        _list.Items.Clear();
        _checks.Clear();
        var query = _search.Text.Trim();
        var page = FolderBrowser.Browse(_folders, _songs, _path);
        _location.Text = query.Length > 0 ? "Search results across your library" : page.Title;
        if (query.Length == 0)
            foreach (var folder in page.Folders)
            {
                var ids = FolderBrowser.Browse(_folders, _songs, folder.Path).Descendants.Select(x => x.Id).ToArray();
                AddRow("▸ " + folder.Name + $"  ({ids.Length:N0} songs)", ids, folder.Path);
            }
        var files = query.Length == 0 ? page.Files : _songs.Where(x =>
            (x.Title + " " + x.Artist + " " + x.Album).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var song in files) AddRow(song.Title + " — " + song.Artist, [song.Id], null);
        UpdateCount();
    }
    private void AddRow(string name, string[] ids, string? folder)
    {
        var row = new DockPanel { LastChildFill = true };
        if (folder is not null)
        {
            var open = Ui.Button("Open", (_, _) => { _path = folder; Refresh(); });
            DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
        }
        var count = ids.Count(_selected.Contains);
        var check = new CheckBox
        {
            Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap },
            IsChecked = count == 0 ? false : count == ids.Length ? true : null,
            IsThreeState = false, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 7, 8, 7)
        };
        check.Click += (_, _) =>
        {
            foreach (var id in ids) { if (check.IsChecked == true) _selected.Add(id); else _selected.Remove(id); }
            // Keep the scroll position while updating overlapping imported roots.
            foreach (var (control, included) in _checks)
            {
                var selected = included.Count(_selected.Contains);
                control.IsChecked = selected == 0 ? false : selected == included.Length ? true : null;
            }
            UpdateCount();
        };
        _checks.Add((check, ids));
        row.Children.Add(check); _list.Items.Add(row);
    }
    private void UpdateCount() => _count.Text = $"{_selected.Count:N0} of {_songs.Length:N0} library songs selected";
}
