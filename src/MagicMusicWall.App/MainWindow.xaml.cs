using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MagicMusicWall.Core;
using Microsoft.Win32;

namespace MagicMusicWall.App;

public partial class MainWindow : Window
{
    private readonly LibraryCatalog _catalog;
    private readonly LibVLC _vlc;
    private readonly MediaPlayer _player;
    private readonly ObservableCollection<MediaItem> _queue = [];
    private readonly SemaphoreSlim _playGate = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private VideoWindow? _videoWindow;
    private ShareWindow? _shareWindow;
    private CancellationTokenSource? _scanCancel;
    private AppPreferences _settings;
    private bool _ready, _scanning, _updatingDevices, _closing, _closed, _seeking, _updatingTime, _queueChanging;
    private string _view = "music";
    private string? _playlistId;
    private int _queueIndex = -1, _playVersion, _activeVersion, _requestedVolume = 65;
    private string _requestedAudioDevice = "";
    private long _resumeTime;
    private bool _resumePaused;
    private MediaItem? _current;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppPreferences.Load(App.DataDirectory);
        _catalog = new LibraryCatalog(App.DataDirectory);
        _catalog.Load();
        string? portableMessage = null;
        if (App.IsPortable)
        {
            try
            {
                var changed = _catalog.PreparePortableLibrary(AppContext.BaseDirectory);
                portableMessage = changed > 0 ? "Portable library paths updated for this folder." : "Portable mode: library and settings are saved in the Data folder beside the app.";
            }
            catch (Exception ex) { portableMessage = $"Portable library could not update its folder paths: {ex.Message}"; }
        }
        LibVLCSharp.Shared.Core.Initialize();
        _vlc = PlaybackEngine.Create(_settings.Volume);
        _player = new MediaPlayer(_vlc);
        _player.SetAudioOutput("mmdevice");
        _player.Mute = false;
        _player.EnableHardwareDecoding = true;
        _player.EndReached += Player_Ended;
        _player.EncounteredError += Player_Error;
        _player.Playing += Player_Playing;
        _player.AudioDevice += Player_AudioDevice;
        QueueList.ItemsSource = _queue;
        _queue.CollectionChanged += (_, _) => QueueTitle.Text = $"UP NEXT · {_queue.Count}";
        InitializeCasting();
        InitializePreferences();
        _ready = true;
        InitializeVisuals();
        Volatile.Write(ref _requestedVolume, _settings.Volume);
        VolumeSlider.Value = _settings.Volume;
        _player.Volume = _settings.Volume;
        RefreshPlaylists();
        RefreshView();
        RefreshAudio();
        _timer.Tick += (_, _) => UpdatePlayback();
        _timer.Start();
        if (!string.IsNullOrEmpty(_catalog.LoadWarning)) Status(_catalog.LoadWarning);
        if (!string.IsNullOrEmpty(_settings.LoadWarning)) Status(_settings.LoadWarning);
        if (portableMessage is not null) Status(portableMessage);
        Loaded += async (_, _) =>
        {
            await ConfigureRemote();
            var i = Array.IndexOf(App.Arguments, "--import");
            if (i >= 0 && i + 1 < App.Arguments.Length) await ImportPaths([App.Arguments[i + 1]]);
            else if (_settings.RescanOnStartup) await RescanLibrary();
        };
    }

    private void Status(string text) { StatusText.Text = text; StatusText.ToolTip = text; }
    private void Error(Exception ex) { Status(ex.Message); MessageBox.Show(this, ex.Message, "Magic Music Wall", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private static string FormatTime(long milliseconds) { var t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
    private List<MediaItem> Selection() => MediaGrid.SelectedItems.OfType<MediaItem>().ToList();
    private List<MediaItem> _viewResults = [];
    private List<object> _browserItems = [];
    private bool _updatingPlaylistControls;
    private MusicPlaylist? CurrentPlaylist => _catalog.Document.Playlists.FirstOrDefault(p => p.Id == _playlistId);
    private bool CanEdit() { if (!_scanning && !_metadataWriting) return true; Status("Please wait for the library operation to finish."); return false; }

    private void RefreshPlaylists(string? preferredDestinationId = null)
    {
        var dest = preferredDestinationId ?? (PlaylistDestinationBox.SelectedItem as MusicPlaylist)?.Id;
        _updatingPlaylistControls = true;
        try
        {
            PlaylistsList.ItemsSource = _catalog.Document.Playlists.ToList();
            PlaylistDestinationBox.ItemsSource = _catalog.Document.Playlists.ToList();
            PlaylistDestinationBox.SelectedItem = _catalog.Document.Playlists.FirstOrDefault(p => p.Id == dest) ?? _catalog.Document.Playlists.FirstOrDefault();
            PlaylistsList.SelectedItem = CurrentPlaylist;
        }
        finally { _updatingPlaylistControls = false; }
    }

    private void RefreshView()
    {
        if (!_ready || _scanning) return;
        IEnumerable<MediaItem> items = _catalog.Document.Items;
        if (CurrentPlaylist is { } playlist)
        {
            var lookup = items.ToDictionary(x => x.Id);
            items = playlist.ItemIds.Where(lookup.ContainsKey).Select(id => lookup[id]);
            ViewTitle.Text = playlist.Name;
        }
        else
        {
            items = items.Where(i => _view == "all" || i.IsVideo == (_view == "video"))
                .OrderBy(i => i.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber).ThenBy(i => i.Title);
            ViewTitle.Text = _view switch { "video" => "Movie night", "all" => "All your media", _ => "Your collection" };
        }
        items = ConfigureFolders(items);
        if (_folderPage is not null) ViewTitle.Text = _folderPage.Title;
        var results = LibraryCatalog.Search(items, SearchBox.Text).ToList();
        _viewResults = results;
        QueuePlaylistButton.Visibility = CurrentPlaylist is null ? Visibility.Collapsed : Visibility.Visible;
        QueuePlaylistButton.IsEnabled = results.Count > 0;
        QueuePlaylistButton.Content = string.IsNullOrWhiteSpace(SearchBox.Text) ? "+ Queue playlist" : "+ Queue matches";
        _browserItems = (_folderPage?.Folders.Cast<object>() ?? []).Concat(results).ToList();
        MediaGrid.ItemsSource = _browserItems;
        if (MediaGrid.Items.Count > 0) MediaGrid.ScrollIntoView(MediaGrid.Items[0]);
        LibrarySummary.Text = $"{results.Count:N0} {(results.Count == 1 ? "item" : "items")}  ·  {_catalog.Document.Folders.Count} source folders  ·  {_catalog.Document.Items.Count:N0} in your library";
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_folderPage is not null) LibrarySummary.Text = $"{_folderPage.Folders.Count:N0} folders · {_folderPage.Descendants.Count:N0} items here · {_catalog.Document.Items.Count:N0} in your library";
        EmptyPanel.Visibility = results.Count == 0 && (_folderPage?.Folders.Count ?? 0) == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _catalog.Document.Items.Count == 0 ? "Make room for your music" : CurrentPlaylist is not null && SearchBox.Text.Length == 0 ? "This playlist is empty" : "No matches yet";
        EmptyDescription.Text = _catalog.Document.Items.Count == 0 ? "Add a folder from your PC, USB drive, or network share.\nYour original files stay right where they are." : CurrentPlaylist is not null && SearchBox.Text.Length == 0 ? "Browse your library and select songs. Choose this playlist\nin the dropdown on the right, then Add selected to playlist." : "Try a different artist, album, title, or folder name.";
        EmptyLibraryButton.Content = _catalog.Document.Items.Count == 0 ? "Choose a music folder" : "Browse your library";
        RefreshMusicMapIfChanged();
    }

    private void ViewsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingPlaylistControls || ViewsList.SelectedItem is not ListBoxItem item || _scanning) return;
        _view = (string)item.Tag;
        _playlistId = null;
        PlaylistsList.SelectedItem = null;
        RefreshView();
    }
    private void PlaylistsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _scanning || _updatingPlaylistControls || PlaylistsList.SelectedItem is not MusicPlaylist p) return;
        OpenPlaylist(p);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (!_updatingPlaylistControls) RefreshView(); }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new OpenFolderDialog { Title = "Add a music or video folder", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await ImportPaths(dialog.FolderNames);
    }
    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new OpenFileDialog { Title = "Add music or video files", Multiselect = true, Filter = "Media files|*.mp3;*.flac;*.m4a;*.aac;*.wav;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.ts|All files|*.*" };
        if (dialog.ShowDialog(this) == true) await ImportPaths(dialog.FileNames);
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!_scanning && e.Data.GetData(DataFormats.FileDrop) is string[] files) await ImportPaths(files);
    }
    private async Task ImportPaths(IEnumerable<string> paths, bool addToCurrentPlaylist = true)
    {
        var destination = addToCurrentPlaylist ? CurrentPlaylist : null;
        var all = paths.Select(Path.GetFullPath).ToArray();
        await Scan(async (progress, ct) =>
        {
            var reports = new List<ImportResult>();
            foreach (var dir in all.Where(Directory.Exists)) reports.Add(await _catalog.AddFolderAsync(dir, progress, ct));
            var files = all.Where(File.Exists).ToArray();
            if (files.Length > 0) reports.Add(await _catalog.AddFilesAsync(files, progress, ct));
            if (destination is not null)
            {
                var roots = all.Where(Directory.Exists).Select(p => Path.TrimEndingDirectorySeparator(p) + Path.DirectorySeparatorChar).ToArray();
                var ids = _catalog.Document.Items.Where(i => File.Exists(i.Path) && (files.Contains(i.Path, StringComparer.OrdinalIgnoreCase) || roots.Any(r => i.Path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))).Select(i => i.Id);
                _catalog.AddToPlaylist(destination.Id, ids);
            }
            return $"Added {reports.Sum(r => r.Added):N0} · updated {reports.Sum(r => r.Updated):N0} · skipped {reports.Sum(r => r.Skipped):N0}." + WarningSummary(reports.SelectMany(r => r.Warnings));
        });
    }
    private static string WarningSummary(IEnumerable<string> warnings) { var w = warnings.ToList(); return w.Count == 0 ? "" : $" {w.Count} notes: {w[0]}"; }
    private async void Rescan_Click(object sender, RoutedEventArgs e) => await RescanLibrary();
    private async Task Scan(Func<IProgress<string>, CancellationToken, Task<string>> action)
    {
        if (!CanEdit()) return;
        _scanning = true;
        _scanCancel = new();
        AddFolderButton.IsEnabled = AddFilesButton.IsEnabled = RescanButton.IsEnabled = false;
        ViewsList.IsEnabled = PlaylistsList.IsEnabled = false;
        CancelImportButton.Visibility = Visibility.Visible;
        try { Status(await action(new Progress<string>(Status), _scanCancel.Token)); }
        catch (OperationCanceledException) { Status("Scan cancelled. Your existing library is preserved."); }
        catch (Exception ex) { Error(ex); }
        finally
        {
            _scanning = false;
            _scanCancel.Dispose(); _scanCancel = null;
            AddFolderButton.IsEnabled = AddFilesButton.IsEnabled = RescanButton.IsEnabled = true;
            ViewsList.IsEnabled = PlaylistsList.IsEnabled = true;
            CancelImportButton.Visibility = Visibility.Collapsed;
            RefreshPlaylists();
            RefreshView();
        }
    }
    private void CancelScan_Click(object sender, RoutedEventArgs e) => _scanCancel?.Cancel();
    private void ManageFolders_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        new FolderWindow(_catalog) { Owner = this }.ShowDialog();
        RefreshPlaylists();
        RefreshView();
    }
    private void CreatePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var selected = Selection().DistinctBy(i => i.Id).ToList();
        var queued = _queue.DistinctBy(i => i.Id).ToList();
        var draft = CreatePlaylistWindow.Show(this, selected.Count, queued.Count);
        if (draft is null) return;
        var items = draft.Source switch { PlaylistSeed.Selected => selected, PlaylistSeed.Queue => queued, _ => [] };
        try
        {
            var p = _catalog.CreatePlaylist(draft.Name);
            if (items.Count > 0) _catalog.AddToPlaylist(p.Id, items.Select(i => i.Id));
            RefreshPlaylists(p.Id); OpenPlaylist(p);
            Status($"Created {p.Name} with {p.ItemIds.Count} saved songs.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || CurrentPlaylist is not { } p) return;
        var name = TextPrompt.Show(this, "Rename playlist", "Playlist name", p.Name);
        if (name is null) return;
        try { _catalog.RenamePlaylist(p.Id, name); RefreshPlaylists(); RefreshView(); } catch (Exception ex) { Error(ex); }
    }
    private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || CurrentPlaylist is not { } p) return;
        if (MessageBox.Show(this, $"Delete playlist '{p.Name}'? Your media files will stay on disk.", "Delete playlist", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _catalog.DeletePlaylist(p.Id); _playlistId = null; RefreshPlaylists(); RefreshView(); } catch (Exception ex) { Error(ex); }
    }
    private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var selection = Selection();
        if (PlaylistDestinationBox.SelectedItem is not MusicPlaylist p || selection.Count == 0) { Status("Select one or more tracks and a destination playlist first."); return; }
        try { _catalog.AddToPlaylist(p.Id, selection.Select(i => i.Id)); RefreshPlaylists(p.Id); RefreshView(); Status($"Saved selection to {p.Name}. {p.ItemIds.Count} songs in this playlist."); } catch (Exception ex) { Error(ex); }
    }
    private void RemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || CurrentPlaylist is not { } p) return;
        try { _catalog.RemoveFromPlaylist(p.Id, Selection().Select(i => i.Id)); RefreshPlaylists(); RefreshView(); } catch (Exception ex) { Error(ex); }
    }
    private void PlaylistUp_Click(object sender, RoutedEventArgs e) => MovePlaylist(-1);
    private void PlaylistDown_Click(object sender, RoutedEventArgs e) => MovePlaylist(1);
    private void MovePlaylist(int offset)
    {
        if (!CanEdit() || CurrentPlaylist is not { } p || MediaGrid.SelectedItem is not MediaItem item) return;
        var index = p.ItemIds.IndexOf(item.Id); var target = index + offset;
        if (index < 0 || target < 0 || target >= p.ItemIds.Count) return;
        (p.ItemIds[index], p.ItemIds[target]) = (p.ItemIds[target], p.ItemIds[index]);
        try
        {
            _catalog.Save(); RefreshView();
            var visibleIndex = _browserItems.IndexOf(item);
            if (visibleIndex >= 0)
            {
                MediaGrid.SelectedItem = item;
                MediaGrid.ScrollIntoView(item);
            }
        }
        catch (Exception ex) { Error(ex); }
    }
    private void RemoveFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var ids = Selection().Select(i => i.Id).ToList();
        if (ids.Count == 0) return;
        if (MessageBox.Show(this, $"Remove {ids.Count} items from the library and playlists? Files stay on disk. A later folder rescan can import them again.", "Remove library entries", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { _catalog.RemoveItems(ids); RefreshPlaylists(); RefreshView(); Status("Removed library entries. Files were kept."); } catch (Exception ex) { Error(ex); }
    }
    private void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if (MediaGrid.SelectedItem is not MediaItem i) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", i.Path }, UseShellExecute = true }); } catch (Exception ex) { Error(ex); }
    }

    private async void PlayResults_Click(object sender, RoutedEventArgs e) => await StartQueue((_folderPlaybackItems ?? _viewResults).ToList());
    private async void PlaySelected_Click(object sender, RoutedEventArgs e) => await StartQueue(Selection());
    private void MediaGrid_KeyDown(object sender, KeyEventArgs e)
    {
        // Buttons handle their own Enter/Space actions. Only handle the focused row here.
        if (e.Key != Key.Enter || e.OriginalSource is not ListBoxItem && !ReferenceEquals(e.OriginalSource, MediaGrid)) return;
        e.Handled = true;
        if (MediaGrid.SelectedItem is FolderEntry folder) OpenFolder(folder.Path);
        else if (MediaGrid.SelectedItem is MediaItem item && !_queueChanging && !_closing)
        { _queue.Add(item); Status($"Added {item.Title} to the queue."); }
    }
    private void MediaGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = e.OriginalSource is DependencyObject source ? ItemsControl.ContainerFromElement(MediaGrid, source) as ListBoxItem : null;
        if (row?.DataContext is MediaItem)
        {
            if (!row.IsSelected) MediaGrid.SelectedItems.Clear();
            row.IsSelected = true;
            row.Focus();
        }
        else MediaGrid.SelectedItems.Clear();
    }
    private void MediaGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    { if (Selection().Count == 0) e.Handled = true; }
    private async Task StartQueue(List<MediaItem> items, int start = 0)
    {
        if (_queueChanging || _closing) return;
        if (!CastReadyForNewPlayback() || !LocalAudioReady()) return;
        if (items.Count == 0) { Status("Choose some music or a video first."); return; }
        if (ShuffleBox.IsChecked == true && start == 0) Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items));
        start = Math.Clamp(start, 0, items.Count - 1);
        if (!File.Exists(items[start].Path)) { Status($"File unavailable: {items[start].Path}. The existing queue has been kept."); return; }
        _queue.Clear(); foreach (var item in items) _queue.Add(item);
        await PlayIndex(Math.Max(0, start));
    }
    private void QueueSelected_Click(object sender, RoutedEventArgs e) { if (_queueChanging) return; foreach (var item in Selection()) _queue.Add(item); Status($"Queue contains {_queue.Count} items."); }
    private async Task PlayIndex(int index, long resume = 0, bool paused = false)
    {
        if (_closing || index < 0 || index >= _queue.Count) return;
        if (!CastReadyForNewPlayback() || !LocalAudioReady()) return;
        using var rendererLease = _cast.Acquire();
        var renderer = rendererLease?.Value;
        var item = _queue[index];
        if (!File.Exists(item.Path))
        {
            ++_playVersion; _queueIndex = index; _current = null;
            await Playback(() => { _player.Stop(); return true; });
            NowTitle.Text = "File unavailable"; NowArtist.Text = item.Title;
            Status($"File unavailable: {item.Path}. Reconnect the source or skip to the next item."); return;
        }
        _queueIndex = index; _current = item;
        QueueList.SelectedIndex = index; QueueList.ScrollIntoView(item);
        NowTitle.Text = item.Title; NowArtist.Text = string.IsNullOrWhiteSpace(item.Artist) ? Path.GetFileName(item.Path) : item.Artist;
        var version = ++_playVersion;
        ResetAudioRouteCheck();
        _resumeTime = resume; _resumePaused = paused;
        if (item.IsVideo && renderer is null) EnsureVideoWindow();
        Status($"Opening {item.Title}…");
        try
        {
            var played = await Playback(() =>
            {
                if (version != _playVersion) return false;
                var muted = _player.Mute;
                _player.Stop();
                if (version != _playVersion || _closing) return false;
                if (!_player.SetRenderer(renderer)) throw new InvalidOperationException("VLC could not select that playback destination.");
                ApplyRequestedAudio(muted, recreate: renderer is null);
                using var media = new Media(_vlc, new Uri(item.Path));
                Volatile.Write(ref _activeVersion, version);
                return _player.Play(media);
            });
            if (version == _playVersion && !played) Status("The media could not be opened. Check its format and file location.");
        }
        catch (Exception ex) { Error(ex); }
    }
    private async Task<T> Playback<T>(Func<T> action)
    {
        await _playGate.WaitAsync();
        try { return await Task.Run(action); }
        finally { _playGate.Release(); }
    }
    private void Player_Ended(object? sender, EventArgs e)
    {
        var version = Volatile.Read(ref _activeVersion);
        Dispatcher.BeginInvoke(async () =>
        {
            if (_closing || _queueChanging || version != _playVersion || _player.State != VLCState.Ended) return;
            if (_queueIndex + 1 < _queue.Count) await PlayIndex(_queueIndex + 1);
            else if (RepeatBox.IsChecked == true && _queue.Count > 0) await PlayIndex(0);
            else { PauseButton.Content = "▶"; Status("Queue finished."); }
        });
    }
    private void Player_Error(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!_closing) Status("Playback failed. Check the file, audio device, or casting connection."); });
    private void Player_Playing(object? sender, EventArgs e)
    {
        var version = Volatile.Read(ref _activeVersion);
        Dispatcher.BeginInvoke(async () =>
        {
            if (_closing || version != _playVersion) return;
            var time = _resumeTime; var paused = _resumePaused;
            _resumeTime = 0; _resumePaused = false;
            if (time > 0 || paused) await Playback(() => { if (version != _playVersion) return false; if (time > 0 && _player.IsSeekable) _player.Time = time; if (paused) _player.SetPause(true); return true; });
            if (!_closing && version == _playVersion && _audioRouteWarning is null) Status($"Playing {_current?.Title} · {_cast.Name}");
            await CheckAudioRoute();
        });
    }
    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging || _closing) return;
        if (_current is null || _player.State is VLCState.Ended or VLCState.Stopped or VLCState.NothingSpecial)
        {
            if (_queue.Count > 0) await PlayIndex(Math.Max(0, _queueIndex));
            else if (CurrentPlaylist is not null) await StartQueue(_viewResults.ToList());
            else Status("Add a song to the queue first.");
            return;
        }
        if (!_player.IsPlaying && !await PrepareAudioResume()) return;
        await Playback(() => { _player.Pause(); return true; });
    }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (_queueChanging || _closing) return; if (_queueIndex + 1 < _queue.Count) await PlayIndex(_queueIndex + 1); else if (_queue.Count > 0 && RepeatBox.IsChecked == true) await PlayIndex(0); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (_queueChanging || _closing) return; if (_player.Time > 3000 && _player.IsSeekable) _player.Time = 0; else if (_queue.Count > 0) await PlayIndex(Math.Max(0, _queueIndex - 1)); }
    private async void Queue_DoubleClick(object sender, MouseButtonEventArgs e) { if (!_queueChanging && !_closing && QueueList.SelectedIndex >= 0) await PlayIndex(QueueList.SelectedIndex); }
    private async void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging) return;
        _queueChanging = true;
        try
        {
            ++_playVersion;
            await Playback(() => { _player.Stop(); return true; });
            _queue.Clear(); _queueIndex = -1; _current = null;
            NowTitle.Text = "Nothing playing yet"; NowArtist.Text = "Choose something you love";
            Status(CurrentPlaylist is { } playlist ? $"Queue cleared. Play playlist loads {playlist.Name} again." : "Queue cleared. Saved playlists are unchanged.");
        }
        finally { _queueChanging = false; }
    }
    private void QueueUp_Click(object sender, RoutedEventArgs e) => MoveQueue(-1);
    private void QueueDown_Click(object sender, RoutedEventArgs e) => MoveQueue(1);
    private void MoveQueue(int offset)
    {
        if (_queueChanging) return;
        var i = QueueList.SelectedIndex; var target = i + offset;
        if (i < 0 || target < 0 || target >= _queue.Count) return;
        _queue.Move(i, target);
        if (_queueIndex == i) _queueIndex = target; else if (_queueIndex == target) _queueIndex = i;
        QueueList.SelectedIndex = target;
    }
    private async void QueueRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_queueChanging || _closing) return;
        var index = QueueList.SelectedIndex;
        if (index < 0 || index >= _queue.Count) return;
        try { await RemoveQueueItem(index, _queue[index].Id); }
        catch (Exception ex) { Error(ex); }
    }

    private async Task RemoveQueueItem(int index, string? expectedItemId)
    {
        if (_queueChanging || _closing) throw new InvalidOperationException("The player is busy. Try again shortly.");
        // A remote's queue may be stale. Match both the position and song before
        // changing anything, including stopping the currently playing item.
        if (index < 0 || index >= _queue.Count || _queue[index].Id != expectedItemId)
            throw new ArgumentException("The queue changed. Refresh the remote and choose the track again.");

        _queueChanging = true;
        try
        {
            var removingCurrent = index == _queueIndex;
            if (removingCurrent)
            {
                ++_playVersion;
                await Playback(() => { _player.Stop(); return true; });
                _current = null;
            }
            _queue.RemoveAt(index);
            if (index < _queueIndex) _queueIndex--;
            else if (removingCurrent) _queueIndex = -1;

            if (removingCurrent || _queue.Count == 0)
            {
                NowTitle.Text = "Nothing playing yet";
                NowArtist.Text = _queue.Count == 0 ? "Queue is empty" : "Choose a queued track to play";
                if (_queue.Count == 0) { _queueIndex = -1; _current = null; }
                else await PlayIndex(Math.Min(index, _queue.Count - 1));
            }
        }
        finally { _queueChanging = false; }
    }
    private void SaveQueue_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit() || _queue.Count == 0) return;
        var queuedIds = _queue.Select(i => i.Id).Distinct().ToList();
        var name = TextPrompt.Show(this, "Save queue", "New playlist name", "");
        if (name is null) return;
        try { var p = _catalog.CreatePlaylist(name); _catalog.AddToPlaylist(p.Id, queuedIds); RefreshPlaylists(p.Id); OpenPlaylist(p); Status($"Saved queue as {p.Name} with {p.ItemIds.Count} songs."); } catch (Exception ex) { Error(ex); }
    }
    private void UpdatePlayback()
    {
        if (_closing) return;
        _updatingTime = true;
        var length = Math.Max(1, _player.Length);
        SeekSlider.Maximum = length;
        if (!_seeking) SeekSlider.Value = Math.Max(0, _player.Time);
        SeekSlider.IsEnabled = _player.IsSeekable;
        ElapsedText.Text = FormatTime(_player.Time); DurationText.Text = FormatTime(_player.Length);
        PauseButton.Content = _player.IsPlaying ? "Ⅱ" : "▶";
        VolumeText.Text = _player.Mute ? "Muted" : $"{_settings.Volume}%";
        _updatingTime = false;
        _ = CheckAudioRoute();
        RefreshVisualizerCapture();
    }
    private void Seek_Begin(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void Seek_End(object sender, MouseButtonEventArgs e) { _seeking = false; if (_player.IsSeekable) _player.Time = (long)SeekSlider.Value; }
    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready && !_updatingTime && !_seeking && SeekSlider.IsKeyboardFocusWithin && _player.IsSeekable) _player.Time = (long)e.NewValue; }
    private async void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        Volatile.Write(ref _requestedVolume, (int)e.NewValue); _settings.Volume = (int)e.NewValue; VolumeText.Text = $"{(int)e.NewValue}%";
        ScheduleSettingsSave();
        await Playback(() => { _player.Volume = Volatile.Read(ref _requestedVolume); _player.Mute = false; return true; });
    }
    private void ApplyRequestedAudio(bool muted = false, bool moveActive = false, bool recreate = false)
    {
        PlaybackEngine.ConfigureOutput(_player, Volatile.Read(ref _requestedAudioDevice), moveActive, recreate);
        _player.Volume = Volatile.Read(ref _requestedVolume);
        _player.Mute = muted;
    }
    private void RefreshAudio()
    {
        _updatingDevices = true;
        try
        {
            var choices = new List<AudioChoice> { new("", "Windows default output") };
            var devices = _vlc.AudioOutputDevices("mmdevice");
            foreach (var d in devices) if (!string.IsNullOrEmpty(d.DeviceIdentifier) && !choices.Any(c => c.Id == d.DeviceIdentifier)) choices.Add(new(d.DeviceIdentifier, d.Description ?? "Audio output"));
            if (!choices.Any(c => SameOutput(c.Id, _settings.AudioDeviceId)))
                choices.Add(new(_settings.AudioDeviceId, $"{PreferredAudioName} · disconnected"));
            AudioDevicesBox.ItemsSource = choices;
            AudioDevicesBox.SelectedItem = choices.First(c => SameOutput(c.Id, _settings.AudioDeviceId));
            Volatile.Write(ref _requestedAudioDevice, ((AudioChoice)AudioDevicesBox.SelectedItem).Id);
            ResetAudioRouteCheck();
            _ = Playback(() => { ApplyRequestedAudio(_player.Mute, moveActive: !SameOutput(_player.OutputDevice, Volatile.Read(ref _requestedAudioDevice))); return true; });
        }
        catch (Exception ex) { Status($"Audio devices could not be refreshed: {ex.Message}"); }
        finally { _updatingDevices = false; }
    }
    private void RefreshAudio_Click(object sender, RoutedEventArgs e) => RefreshAudio();
    private async void AudioDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingDevices || AudioDevicesBox.SelectedItem is not AudioChoice device) return;
        _settings.AudioDeviceId = device.Id;
        _settings.AudioDeviceName = device.Name;
        ScheduleSettingsSave();
        Volatile.Write(ref _requestedAudioDevice, device.Id);
        ResetAudioRouteCheck();
        await Playback(() => { ApplyRequestedAudio(moveActive: true); return true; });
        Status($"Audio output requested: {device.Name}. Bluetooth devices must be connected in Windows.");
    }
    private void Bluetooth_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); } catch (Exception ex) { Error(ex); } }
    private void EnsureVideoWindow()
    {
        if (_videoWindow is null) { _videoWindow = new VideoWindow(_player); _videoWindow.Owner = this; }
        _videoWindow.Show(); _videoWindow.Activate();
    }
    private void ShowVideo_Click(object sender, RoutedEventArgs e) { if (_current?.IsVideo == true) EnsureVideoWindow(); else Status("Play a video to open the video window."); }
    private void Share_Click(object sender, RoutedEventArgs e)
    {
        var selected = MediaGrid.SelectedItem as MediaItem ?? _current;
        if (selected is null) { Status("Select a movie or song to share first."); return; }
        if (!File.Exists(selected.Path)) { Status("The selected file is currently unavailable."); return; }
        _shareWindow ??= new ShareWindow(() => _settings.Copy()) { Owner = this };
        _shareWindow.SetFile(selected.Path);
        _shareWindow.Show(); _shareWindow.Activate();
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        if (_metadataWriting) { Status("Please wait for the file tags to finish saving."); return; }
        if (_scanning) { _scanCancel?.Cancel(); Status("Cancelling the scan. Close again after it finishes."); return; }
        StopVisuals();
        _closing = true; _timer.Stop(); _saveTimer.Stop(); ++_playVersion; IsEnabled = false;
        try
        {
            await StopMusicMapAnalysis();
            try { _settings.Save(App.DataDirectory); } catch (Exception ex) { LogCleanup(ex); }
            try { await StopCastingDiscoveryAsync(); } catch (Exception ex) { LogCleanup(ex); }
            try { await _remote.DisposeAsync(); } catch (Exception ex) { LogCleanup(ex); }
            _musicLookup.Dispose();
            _movieLookup.Dispose();
            try { if (_shareWindow is not null) await _shareWindow.Shutdown(); } catch (Exception ex) { LogCleanup(ex); }
            try { _videoWindow?.Shutdown(); } catch (Exception ex) { LogCleanup(ex); }
            await Playback(() =>
            {
                _player.EndReached -= Player_Ended; _player.EncounteredError -= Player_Error; _player.Playing -= Player_Playing;
                _player.AudioDevice -= Player_AudioDevice;
                try { _player.Stop(); } catch (Exception ex) { LogCleanup(ex); }
                try { _player.Dispose(); } catch (Exception ex) { LogCleanup(ex); }
                try { _castRegistry.Dispose(); } catch (Exception ex) { LogCleanup(ex); }
                try { _vlc.Dispose(); } catch (Exception ex) { LogCleanup(ex); }
                return true;
            });
        }
        finally { _closed = true; Close(); }
    }
    private static void LogCleanup(Exception ex) { try { File.AppendAllText(Path.Combine(App.DataDirectory, "errors.log"), $"{DateTimeOffset.Now:u} Shutdown: {ex}\n"); } catch { } }
}
