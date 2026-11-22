using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MagicMusicWall.Core;
using MagicMusicWall.Streaming;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MagicMusicWall.App;

internal static class Ui
{
    public static void Theme(Window window) => window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
    public static TextBlock Text(string text, double size = 14, bool muted = false) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(muted ? "#A3A9BD" : "#F4F3F8")) };
    public static Button Button(string text, RoutedEventHandler action) { var button = new Button { Content = text, Margin = new Thickness(0, 6, 8, 6) }; button.Click += action; return button; }
}

internal sealed class TextPrompt : Window
{
    private readonly TextBox _input;
    private string? Result { get; set; }
    private TextPrompt(string title, string label, string initial)
    {
        Ui.Theme(this);
        Title = title; Width = 420; Height = 230; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Ui.Text(label, 18));
        _input = new TextBox { Text = initial, MaxLength = 100 };
        panel.Children.Add(_input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = Ui.Button("Cancel", (_, _) => Close()); cancel.IsCancel = true;
        var save = Ui.Button("Save", (_, _) => { if (string.IsNullOrWhiteSpace(_input.Text)) return; Result = _input.Text.Trim(); DialogResult = true; }); save.IsDefault = true;
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }
    public static string? Show(Window owner, string title, string label, string initial)
    {
        var prompt = new TextPrompt(title, label, initial) { Owner = owner };
        prompt.ShowDialog(); return prompt.Result;
    }
}

internal sealed class FolderWindow : Window
{
    public FolderWindow(LibraryCatalog catalog, Func<bool> canRemove, Func<Task> libraryChanged)
    {
        Ui.Theme(this);
        Title = "Library folders"; Width = 730; Height = 500; MinWidth = 590; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = Ui.Text("Your library sources", 24); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var help = Ui.Text("Removing a source also removes its tracks from the library, playlists and PC queue. Tracks covered by another source stay. Files on disk are never deleted.", 13, true); DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        var list = new ListBox { DisplayMemberPath = "Path" };
        var actions = new StackPanel();
        var added = Ui.Text("", 13, true);
        var busy = false;
        Button clear = null!;
        async Task Change(Action change)
        {
            if (busy || !canRemove()) return;
            busy = true; actions.IsEnabled = false;
            try { change(); await libraryChanged(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Library folders"); }
            finally { busy = false; actions.IsEnabled = true; Refresh(); }
        }
        var remove = Ui.Button("Remove selected source", async (_, _) =>
        {
            if (list.SelectedItem is not LibraryFolder folder || !canRemove()) return;
            if (MessageBox.Show(this, "Remove this source and its tracks from the library, playlists and PC queue? Files stay on disk. Tracks covered by another source are kept.", "Remove library source", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await Change(() => catalog.RemoveFolder(folder.Id));
        });
        clear = Ui.Button("Clear added files", async (_, _) =>
        {
            if (!canRemove()) return;
            var count = FolderBrowser.Browse(catalog.Document.Folders, catalog.Document.Items, FolderBrowser.LooseFiles).Files.Count;
            if (count == 0 || MessageBox.Show(this, $"Remove all {count:N0} added files from the library, playlists and PC queue? Files stay on disk. Managed folder tracks are kept.", "Clear added files", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await Change(catalog.ClearAddedFiles);
        });
        void Refresh()
        {
            list.ItemsSource = catalog.Document.Folders.ToList();
            var count = FolderBrowser.Browse(catalog.Document.Folders, catalog.Document.Items, FolderBrowser.LooseFiles).Files.Count;
            added.Text = $"Added files: {count:N0} tracks outside your managed folders, including entries kept by older versions.";
            clear.IsEnabled = count > 0;
        }
        actions.Children.Add(remove); actions.Children.Add(added); actions.Children.Add(clear);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(list); Content = panel;
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Refresh();
    }
}

internal sealed class VideoWindow : Window
{
    private readonly VideoView _view;
    private bool _shutdown;
    public VideoWindow(MediaPlayer player)
    {
        Ui.Theme(this);
        Title = "Magic Music Wall · Video"; Width = 960; Height = 600; MinWidth = 480; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        _view = new VideoView { MediaPlayer = player, Background = Brushes.Black };
        Content = _view;
        Closing += (_, e) => { if (!_shutdown) { e.Cancel = true; Hide(); } };
    }
    public void Shutdown() { _shutdown = true; _view.MediaPlayer = null; _view.Dispose(); Close(); }
}

internal sealed class ShareWindow : Window
{
    private readonly Func<AppPreferences> _preferences;
    private readonly LanMediaServer _server = new();
    private readonly TextBlock _filename = Ui.Text("", 18);
    private readonly ComboBox _addresses = new();
    private readonly TextBox _url = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 68 };
    private readonly TextBlock _status = Ui.Text("Sharing is off.", 13, true);
    private readonly Button _start;
    private string? _path;
    private bool _shutdown;
    public ShareWindow(Func<AppPreferences> preferences)
    {
        _preferences = preferences;
        Ui.Theme(this);
        Title = "Share to Fire TV or a network player"; Width = 650; Height = 620; ResizeMode = ResizeMode.CanMinimize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(Ui.Text("Take movie night to the TV", 24));
        panel.Children.Add(Ui.Text("On Fire TV, open VLC (or another player that accepts network URLs), choose its network stream option, and enter the link below. Both devices must be on the same home network.", 14, true));
        panel.Children.Add(_filename);
        panel.Children.Add(Ui.Text("PC network address", 12, true));
        _addresses.DisplayMemberPath = "Label";
        RefreshAddress();
        panel.Children.Add(_addresses);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        _start = Ui.Button("Start sharing this file", async (_, _) => await Start());
        buttons.Children.Add(_start);
        buttons.Children.Add(Ui.Button("Stop sharing", async (_, _) => { await _server.StopAsync(); _url.Clear(); _status.Text = "Sharing stopped. Previous links no longer work."; _addresses.IsEnabled = true; }));
        panel.Children.Add(buttons); panel.Children.Add(_url);
        panel.Children.Add(Ui.Button("Copy link", (_, _) => { if (_url.Text.Length > 0) { Clipboard.SetText(_url.Text); _status.Text = "Link copied."; } }));
        panel.Children.Add(_status);
        panel.Children.Add(Ui.Text("The link grants access to this one file while sharing is running. Keep the app open. If Windows asks about firewall access, allow it on your private home network. The receiver must support the file's codecs; this link does not convert video.", 12, true));
        Content = panel;
        Closing += (_, e) => { if (!_shutdown) { e.Cancel = true; Hide(); } };
    }
    private void RefreshAddress()
    {
        var saved = _preferences().SharingAddress;
        _addresses.ItemsSource = NetworkAddresses.Choices(saved);
        _addresses.SelectedItem = _addresses.Items.Cast<NetworkChoice>().First(c => c.Address == saved);
    }
    public void SetFile(string path) { _path = path; _filename.Text = Path.GetFileName(path); if (_server.IsRunning) { _server.ClearShares(); _url.Clear(); _status.Text = "File changed. Start sharing to create its link."; } else RefreshAddress(); }
    private async Task Start()
    {
        if (_path is null || _addresses.SelectedItem is not NetworkChoice choice) { _status.Text = "Choose a connected network address first."; return; }
        _start.IsEnabled = false;
        try
        {
            if (!_server.IsRunning) await _server.StartAsync(NetworkAddresses.Resolve(choice.Address), _preferences().SharingPort);
            _server.ClearShares();
            _url.Text = _server.ShareFile(_path);
            _addresses.IsEnabled = false;
            _status.Text = "Sharing is on. Open this URL on your TV or network player.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _start.IsEnabled = true; }
    }
    public async Task Shutdown() { _shutdown = true; await _server.DisposeAsync(); Close(); }
}
