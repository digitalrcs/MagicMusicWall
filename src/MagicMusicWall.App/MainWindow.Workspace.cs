using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private string _libraryFilter = "All", _librarySort = "Artist", _libraryGroup = "None", _genreFilter = "";
    private uint _yearFilter;
    private string? _artworkPath;
    private bool _fullscreen, _motionPaused;
    private WindowState _beforeFullscreen;
    private Window? _miniPlayer;
    private TextBlock? _miniTitle;
    private readonly System.Windows.Threading.DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private void InitializeWorkspace()
    {
        Width = Math.Min(_settings.WindowWidth, SystemParameters.WorkArea.Width);
        Height = Math.Min(_settings.WindowHeight, SystemParameters.WorkArea.Height);
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        ApplyWorkspacePreferences();
        InitializeFolderWatching();
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); if (!_closing) RefreshView(); };
        Closed += (_, _) => _searchTimer.Stop();
        Loaded += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(MediaKeyMessage);
            MediaGrid.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(SortHeader_Click));
        };
        PreviewKeyDown += Workspace_KeyDown;
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            var bounds = RestoreBounds;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.WindowMaximized = WindowState == WindowState.Maximized && !_fullscreen;
            try { _settings.Save(App.DataDirectory); } catch (Exception ex) { Status(ex.Message); }
            _miniPlayer?.Close();
        };
    }

    private void ApplyWorkspacePreferences()
    {
        var available = Math.Max(0, (ActualWidth > 0 ? ActualWidth : Width) - 450);
        var left = _settings.LibraryPanelVisible ? Math.Min(_settings.LibraryPanelWidth, available * .45) : 0;
        var right = _settings.QueuePanelVisible ? Math.Min(_settings.QueuePanelWidth, available - left) : 0;
        LibraryColumn.Width = new GridLength(left);
        QueueColumn.Width = new GridLength(right);
        LibrarySidebar.Visibility = _settings.LibraryPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        QueueSidebar.Visibility = _settings.QueuePanelVisible ? Visibility.Visible : Visibility.Collapsed;
        CollectionWorkspace.Margin = _settings.CompactLayout ? new Thickness(12) : new Thickness(20, 22, 20, 18);
        MediaGrid.FontSize = _settings.CompactLayout ? 12 : 14;
    }
    private void ScheduleLibrarySearch()
    { if (!_ready || _closing) return; _searchTimer.Stop(); _searchTimer.Start(); }

    private void PanelResize_Completed(object sender, DragCompletedEventArgs e)
    {
        if (!_ready) return;
        if (_settings.LibraryPanelVisible) _settings.LibraryPanelWidth = Math.Clamp(LibraryColumn.ActualWidth, 180, 420);
        if (_settings.QueuePanelVisible) _settings.QueuePanelWidth = Math.Clamp(QueueColumn.ActualWidth, 240, 600);
        ApplyWorkspacePreferences(); ScheduleSettingsSave();
    }

    private void ViewOptions_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Library panel · Ctrl+L", () => { _settings.LibraryPanelVisible = !_settings.LibraryPanelVisible; ApplyWorkspacePreferences(); ScheduleSettingsSave(); });
        Add("Queue panel · Ctrl+Q", () => { _settings.QueuePanelVisible = !_settings.QueuePanelVisible; ApplyWorkspacePreferences(); ScheduleSettingsSave(); });
        Add(_settings.CompactLayout ? "Comfortable spacing" : "Compact spacing", () => { _settings.CompactLayout = !_settings.CompactLayout; ApplyWorkspacePreferences(); ScheduleSettingsSave(); });
        Add("Filter and sort library…", ShowLibraryFilters);
        Add("Library health…", ShowLibraryHealth);
        Add("Toggle favorite for selected songs", ToggleSelectedFavorites);
        Add("Create smart playlist…", CreateSmartPlaylist);
        Add(_settings.RepeatOne ? "Repeat current track: on" : "Repeat current track: off", () => { _settings.RepeatOne = !_settings.RepeatOne; ScheduleSettingsSave(); });
        Add("Import M3U playlist…", () => ImportPlaylistFile_Click(this, new RoutedEventArgs()));
        Add("Mini player", ShowMiniPlayer);
        Add("Fullscreen listening · F11", ToggleFullscreen);
        Add(_motionPaused ? "Resume visual motion" : "Pause visual motion", () => _motionPaused = !_motionPaused);
        Add("Refresh audio outputs", RefreshAudio);
        Add("Bluetooth settings ↗", () => OpenExternal("ms-settings:bluetooth"));
        menu.PlacementTarget = sender as UIElement; menu.IsOpen = true;
    }

    private MusicPlaylist? ChoosePlaylistDestination()
    {
        var box = new ComboBox { ItemsSource = _catalog.Document.Playlists.Where(p => p.Rule is null).ToList(), DisplayMemberPath = "Name", MinWidth = 300, Margin = new Thickness(0, 10, 0, 16) };
        box.SelectedItem = (CurrentPlaylist?.Rule is null ? CurrentPlaylist : null) ?? box.Items.Cast<MusicPlaylist>().FirstOrDefault();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Add selected songs to", FontWeight = FontWeights.SemiBold }); panel.Children.Add(box);
        var dialog = new Window { Title = "Choose playlist", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        Ui.Theme(dialog);
        var button = Ui.Button("Add to playlist", (_, _) => { if (box.SelectedItem is not null) dialog.DialogResult = true; }); panel.Children.Add(button);
        return dialog.ShowDialog() == true ? box.SelectedItem as MusicPlaylist : null;
    }

    private void ShowLibraryFilters()
    {
        var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 330 };
        ComboBox Pick(string label, string[] choices, string value)
        { panel.Children.Add(Ui.Text(label)); var box = new ComboBox { ItemsSource = choices, SelectedItem = value, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(box); return box; }
        var filter = Pick("Show", ["All", "Favorites", "Unplayed", "Recently played", "Missing metadata", "Unavailable"], _libraryFilter);
        var sort = Pick("Sort by", ["Artist", "Album", "Title", "Year", "Duration"], _librarySort);
        var group = Pick("Group songs", ["None", "Artist", "Album"], _libraryGroup);
        panel.Children.Add(Ui.Text("Genre contains")); var genre = new TextBox { Text = _genreFilter }; panel.Children.Add(genre);
        panel.Children.Add(Ui.Text("Year (blank for all)")); var year = new TextBox { Text = _yearFilter == 0 ? "" : _yearFilter.ToString() }; panel.Children.Add(year);
        var dialog = new Window { Title = "Library view", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40), Owner = this, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.Theme(dialog);
        var validation = Ui.Text("", 12, true); panel.Children.Add(validation);
        panel.Children.Add(Ui.Button("Apply", (_, _) => { if (year.Text.Length > 0 && (!uint.TryParse(year.Text, out var y) || y > 9999)) { validation.Text = "Enter a year from 0 to 9999, or leave it blank."; year.Focus(); return; } dialog.DialogResult = true; }));
        if (dialog.ShowDialog() != true) return;
        _libraryFilter = (string)filter.SelectedItem; _librarySort = (string)sort.SelectedItem; _libraryGroup = (string)group.SelectedItem;
        _genreFilter = genre.Text.Trim(); uint.TryParse(year.Text, out _yearFilter);
        GridView_Click(this, new RoutedEventArgs()); RefreshView(); ApplyLibraryGrouping();
    }

    private IEnumerable<MediaItem> ApplyLibraryFilters(IEnumerable<MediaItem> items)
    {
        items = items.Where(i => (_libraryFilter != "Favorites" || i.Favorite) && (_libraryFilter != "Unplayed" || i.PlayCount == 0) && (_libraryFilter != "Recently played" || i.LastPlayedUtc >= DateTimeOffset.UtcNow.AddDays(-30)) && (_libraryFilter != "Unavailable" || !i.IsAvailable) &&
            (_libraryFilter != "Missing metadata" || string.IsNullOrWhiteSpace(i.Artist) || string.IsNullOrWhiteSpace(i.Album) || string.IsNullOrWhiteSpace(i.Title)) &&
            (_genreFilter.Length == 0 || i.Genre.Contains(_genreFilter, StringComparison.CurrentCultureIgnoreCase)) && (_yearFilter == 0 || i.Year == _yearFilter));
        if (CurrentPlaylist is not null) return items;
        return _librarySort switch { "Title" => items.OrderBy(i => i.Title), "Album" => items.OrderBy(i => i.Album).ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber), "Year" => items.OrderBy(i => i.Year), "Duration" => items.OrderBy(i => i.DurationSeconds), _ => items.OrderBy(i => i.Artist).ThenBy(i => i.Album).ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber) };
    }

    private void ApplyLibraryGrouping()
    {
        if (MediaGrid.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(MediaGrid.ItemsSource);
        if (view?.CanGroup != true) return;
        view.GroupDescriptions.Clear();
        if (_libraryGroup != "None") view.GroupDescriptions.Add(new PropertyGroupDescription(_libraryGroup));
    }
    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Role == GridViewColumnHeaderRole.Padding) return;
        var name = header.Content?.ToString() ?? "";
        _librarySort = name.Contains("Artist") ? "Artist" : name.Contains("Album") ? "Album" : name.Contains("Time") ? "Duration" : "Title";
        RefreshView(); ApplyLibraryGrouping();
    }

    private async void Library_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _closing || _scanning || _queueChanging) return;
        var source = e.OriginalSource as DependencyObject;
        for (var node = source; node is not null && node != MediaGrid; node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase or ScrollBar) return;
        if (source is null || ItemsControl.ContainerFromElement(MediaGrid, source) is not ListBoxItem row) return;
        e.Handled = true;
        await ActivateLibraryItem(row.DataContext);
    }

    private void Workspace_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; return; }
        if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.L or Key.Q)
        { if (e.Key == Key.L) _settings.LibraryPanelVisible = !_settings.LibraryPanelVisible; else _settings.QueuePanelVisible = !_settings.QueuePanelVisible; ApplyWorkspacePreferences(); ScheduleSettingsSave(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox) return;
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None) { Pause_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }
    private nint MediaKeyMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != 0x0319) return 0;
        switch (((long)lParam >> 16) & 0xFFF)
        { case 14: Pause_Click(this, new RoutedEventArgs()); break; case 11: Next_Click(this, new RoutedEventArgs()); break; case 12: Previous_Click(this, new RoutedEventArgs()); break; default: return 0; }
        handled = true; return 1;
    }
    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen) { _beforeFullscreen = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; LibraryColumn.Width = new GridLength(0); QueueColumn.Width = new GridLength(0); VisualMode.IsChecked = true; }
        else { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = _beforeFullscreen; ApplyWorkspacePreferences(); }
    }
    private void MiniPlayer_Click(object sender, RoutedEventArgs e) => ShowMiniPlayer();
    private void ShowMiniPlayer()
    {
        if (_miniPlayer is not null)
        {
            if (_miniPlayer.WindowState == WindowState.Minimized) _miniPlayer.WindowState = WindowState.Normal;
            _miniPlayer.Activate();
            return;
        }
        var panel = new StackPanel { Margin = new Thickness(16) }; _miniTitle = Ui.Text(NowTitle.Text); panel.Children.Add(_miniTitle);
        var buttons = new WrapPanel(); buttons.Children.Add(Ui.Button("Previous", Previous_Click)); buttons.Children.Add(Ui.Button("Play / pause", Pause_Click)); buttons.Children.Add(Ui.Button("Next", Next_Click)); panel.Children.Add(buttons);
        // An owned window is minimized with its owner. Keep the mini player independent;
        // the main window's closing handler explicitly closes it during shutdown.
        _miniPlayer = new Window { Title = "Magic Music Wall · Mini player", Width = 390, Height = 150, MinWidth = 340, MinHeight = 150, Content = panel, Topmost = true };
        Ui.Theme(_miniPlayer);
        _miniPlayer.Closed += (_, _) => { _miniPlayer = null; _miniTitle = null; }; _miniPlayer.Show();
    }
    private async void UpdateWorkspacePlayback()
    {
        if (_miniTitle is not null) _miniTitle.Text = NowTitle.Text + " · " + NowArtist.Text;
        var seconds = _queue.Skip(Math.Max(0, _queueIndex)).Sum(i => double.IsFinite(i.DurationSeconds) ? Math.Clamp(i.DurationSeconds, 0, 864000) : 0);
        seconds = Math.Max(0, seconds - Math.Max(0, _pendingSessionPosition ?? _player.Time) / 1000d);
        QueueRemainingText.Text = $"{seconds / 60:0} min remaining · {_queue.Count:N0} entries";
        var path = _current?.Path; if (_artworkPath == path) return; _artworkPath = path; NowArtwork.Source = null;
        if (path is null) return;
        var artwork = await Task.Run(() =>
        {
            try { using var file = TagLib.File.Create(path, TagLib.ReadStyle.None); var data = file.Tag.Pictures.FirstOrDefault()?.Data.Data; if (data is null || data.Length > 20_000_000) return null; using var stream = new MemoryStream(data); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 150; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap; }
            catch { return null; }
        });
        if (!_closed && _artworkPath == path) NowArtwork.Source = artwork;
    }
}
