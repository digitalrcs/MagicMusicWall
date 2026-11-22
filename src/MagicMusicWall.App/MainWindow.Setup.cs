using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MagicMusicWall.Core;
using MagicMusicWall.Streaming;
using Microsoft.Win32;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly RemoteControlServer _remote = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool _applyingPreferences;
    private string? _remoteError;

    internal AppPreferences Preferences => _settings.Copy();
    internal string RemoteAddressLink => _remote.IsRunning ? _remote.BaseUrl + "/" : "";
    internal string RemoteLink => _remote.IsRunning ? $"{_remote.BaseUrl}/#key={Uri.EscapeDataString(_settings.RemoteAccessToken)}" : "";
    internal string ListeningLink => _settings.BrowserPlaybackEnabled && RemoteLink.Length > 0 ? RemoteLink + "&mode=listen" : "";
    internal string RemoteStatus => _remote.IsRunning ? "Remote is running. Open this link on your home network." : _remoteError ?? "Remote is off. Enable it and save settings to connect.";
    internal IReadOnlyList<LibraryFolder> LibraryFolders => _catalog.Document.Folders.ToList();

    private void InitializePreferences()
    {
        ShuffleBox.IsChecked = _settings.Shuffle;
        RepeatBox.IsChecked = _settings.RepeatQueue;
        ShuffleBox.Checked += PlaybackPreference_Changed; ShuffleBox.Unchecked += PlaybackPreference_Changed;
        RepeatBox.Checked += PlaybackPreference_Changed; RepeatBox.Unchecked += PlaybackPreference_Changed;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            try { _settings.Save(App.DataDirectory); }
            catch (Exception ex) { Status($"Settings could not be saved: {ex.Message}"); }
        };
    }

    private void ScheduleSettingsSave()
    {
        if (!_ready || _closing || _applyingPreferences) return;
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private void PlaybackPreference_Changed(object sender, RoutedEventArgs e)
    {
        _settings.Shuffle = ShuffleBox.IsChecked == true;
        _settings.RepeatQueue = RepeatBox.IsChecked == true;
        ScheduleSettingsSave();
    }

    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        new SetupWindow(this) { Owner = this }.ShowDialog();
    }

    private void Brand_Click(object sender, RoutedEventArgs e) => OpenExternal("https://digitalrcs.com");
    internal void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Error(ex); }
    }

    internal IReadOnlyList<AudioChoice> SetupAudioChoices(bool refresh = false)
    {
        if (refresh) RefreshAudio();
        var result = AudioDevicesBox.Items.Cast<AudioChoice>().ToList();
        if (result.Count == 0) result.Add(new("", "Windows default output"));
        if (!result.Any(c => c.Id == _settings.AudioDeviceId))
            result.Add(new(_settings.AudioDeviceId, $"{(_settings.AudioDeviceName.Length > 0 ? _settings.AudioDeviceName : "Saved output")} · disconnected"));
        return result;
    }

    internal async Task AddSetupFolder(Window owner)
    {
        if (!CanEdit()) return;
        var picker = new OpenFolderDialog { Title = "Add library folders", Multiselect = true };
        if (picker.ShowDialog(owner) == true) await ImportPaths(picker.FolderNames, false);
    }

    private async Task AddSetupFiles(Window owner)
    {
        if (!CanEdit()) return;
        var picker = new OpenFileDialog { Title = "Add music or video files", Multiselect = true, Filter = "Media files|*.mp3;*.flac;*.m4a;*.aac;*.wav;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.ts|All files|*.*" };
        if (picker.ShowDialog(owner) == true) await ImportPaths(picker.FileNames, false);
    }

    internal void ManageSetupFolders(Window owner)
    {
        if (!CanEdit()) return;
        new FolderWindow(_catalog, CanRemoveLibraryItems, ReconcileRemovedLibraryItemsAsync, AddSetupFolder, AddSetupFiles) { Owner = owner }.ShowDialog();
        RefreshView();
    }

    internal Task RescanLibrary() => Scan(async (p, ct) =>
    {
        var r = await _catalog.RescanAsync(p, ct);
        return $"Rescan complete: {r.Added} added, {r.Updated} updated, {r.Skipped} skipped." + WarningSummary(r.Warnings);
    });

    internal async Task<string> SavePreferences(AppPreferences updated)
    {
        if (_closing) throw new InvalidOperationException("The app is closing.");
        if (updated.RemoteEnabled && updated.SharingPort != 0 && updated.RemotePort == updated.SharingPort)
            throw new InvalidOperationException("Use different ports for the web remote and file sharing.");
        if (updated.RemoteAccessToken.Length < 32) updated.RemoteAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var restart = updated.RemoteEnabled != _settings.RemoteEnabled || updated.RemoteAddress != _settings.RemoteAddress ||
            updated.RemotePort != _settings.RemotePort || updated.RemoteAccessToken != _settings.RemoteAccessToken ||
            updated.RemotePassphraseHash != _settings.RemotePassphraseHash ||
            updated.BrowserPlaybackEnabled != _settings.BrowserPlaybackEnabled;
        updated.Save(App.DataDirectory);
        _saveTimer.Stop();
        _applyingPreferences = true;
        try
        {
            _settings = updated;
            Volatile.Write(ref _requestedVolume, updated.Volume);
            VolumeSlider.Value = updated.Volume;
            VolumeText.Text = $"{updated.Volume}%";
            ShuffleBox.IsChecked = updated.Shuffle;
            RepeatBox.IsChecked = updated.RepeatQueue;
            RefreshAudio();
        }
        finally { _applyingPreferences = false; }
        await Playback(() => { _player.Volume = Volatile.Read(ref _requestedVolume); return true; });
        if (restart || (_settings.RemoteEnabled && !_remote.IsRunning)) await ConfigureRemote();
        var message = _remoteError is null ? "Settings saved." : $"Settings saved. {_remoteError}";
        Status(message);
        return message;
    }

    private async Task ConfigureRemote()
    {
        await _remote.StopAsync();
        _remoteError = null;
        if (!_settings.RemoteEnabled || _closing) return;
        try
        {
            if (_settings.RemoteAccessToken.Length < 32)
            {
                _settings.RemoteAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                _settings.Save(App.DataDirectory);
            }
            await _remote.StartAsync(NetworkAddresses.Resolve(_settings.RemoteAddress), _settings.RemotePort, _settings.RemoteAccessToken, new RemoteBridge(this), _settings.RemotePassphraseHash);
        }
        catch (Exception ex) { _remoteError = $"Web remote could not start: {ex.Message}"; Status(_remoteError); }
    }

    private static RemoteItem ToRemote(MediaItem item) => new(item.Id, item.Title, item.Artist, item.Album, item.DurationText, item.IsVideo);
    private List<MediaItem> RemoteResults(string query, string? playlistId)
    {
        IEnumerable<MediaItem> items = _catalog.Document.Items;
        if (!string.IsNullOrEmpty(playlistId))
        {
            var playlist = _catalog.Document.Playlists.FirstOrDefault(p => p.Id == playlistId) ?? throw new ArgumentException("Playlist not found.");
            var lookup = items.ToDictionary(i => i.Id);
            items = playlist.ItemIds.Where(lookup.ContainsKey).Select(id => lookup[id]);
        }
        else items = items.OrderBy(i => i.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.DiscNumber).ThenBy(i => i.TrackNumber).ThenBy(i => i.Title);
        return LibraryCatalog.Search(items, query).ToList();
    }

    private async Task ExecuteRemote(RemoteCommand command)
    {
        if (_closing || _queueChanging || _scanning) throw new InvalidOperationException("The player is busy. Try again shortly.");
        switch (command.Action)
        {
            case "clear-queue": await ClearQueueAsync(); break;
            case "queue-folder":
            case "queue-selection":
                // Resolve the complete batch before mutating the queue; stale IDs
                // cannot leave behind a partially added selection.
                foreach (var selected in ResolveQueueSelection(command)) _queue.Add(selected);
                break;
            case "play-queue-item":
                var index = command.QueueIndex!.Value;
                if (index >= _queue.Count || _queue[index].Id != command.ItemId)
                    throw new ArgumentException("The queue changed. Refresh the remote and choose the track again.");
                await PlayIndex(index);
                break;
            case "remove-queue-item":
                await RemoveQueueItem(command.QueueIndex!.Value, command.ItemId);
                break;
            case "play-item":
            case "queue-item":
                var item = _catalog.Document.Items.FirstOrDefault(i => i.Id == command.ItemId) ?? throw new ArgumentException("Track not found.");
                if (command.Action == "queue-item") _queue.Add(item); else await StartQueue([item]);
                break;
            case "play-playlist": await StartQueue(RemoteResults("", command.PlaylistId)); break;
            case "play-folder": await StartQueue(RemoteFolderSelection(command.FolderId).Descendants.ToList()); break;
            case "play-results": await StartQueue(RemoteResults(command.Query ?? "", command.PlaylistId)); break;
            case "shuffle": ShuffleBox.IsChecked = command.Enabled!.Value; break;
            case "repeat": RepeatBox.IsChecked = command.Enabled!.Value; break;
            case "refresh-audio": RefreshAudio(); break;
            case "audio-output":
                if (_cast.Renderer is not null) throw new InvalidOperationException("Stop casting in the Windows app before changing PC audio output.");
                var outputId = command.AudioOutputId!;
                if (outputId.Length > 0 && !_vlc.AudioOutputDevices("mmdevice").Any(d => SameOutput(d.DeviceIdentifier, outputId)))
                    throw new ArgumentException("That audio output is unavailable. Connect it in Windows and refresh the outputs.");
                var output = SetupAudioChoices(refresh: true).FirstOrDefault(d => SameOutput(d.Id, outputId))
                    ?? throw new ArgumentException("Audio output unavailable. Refresh outputs and try again.");
                await SelectAudioOutputAsync(output);
                break;
            case "volume":
                VolumeSlider.Value = command.Volume!.Value;
                break;
            case "seek":
                await Playback(() => { if (_player.IsSeekable) _player.Time = Math.Clamp(command.PositionMs!.Value, 0, Math.Max(0, _player.Length)); return true; });
                break;
            case "next":
                await AdvanceQueue();
                break;
            case "previous":
                if (_player.Time > 3000 && _player.IsSeekable) await Playback(() => { _player.Time = 0; return true; });
                else if (_queue.Count > 0) await PlayIndex(Math.Max(0, _queueIndex - 1));
                break;
            case "pause": await Playback(() => { _player.SetPause(true); return true; }); break;
            case "play":
            case "toggle":
                if (_current is null || _player.State is VLCState.Ended or VLCState.Stopped or VLCState.NothingSpecial)
                { if (_queue.Count > 0) await PlayIndex(Math.Max(0, _queueIndex)); }
                else
                {
                    if (!_player.IsPlaying && !await PrepareAudioResume()) break;
                    await Playback(() => { if (command.Action == "toggle") _player.Pause(); else _player.SetPause(false); return true; });
                }
                break;
        }
    }

    private sealed partial class RemoteBridge(MainWindow main) : IRemoteControl, IRemoteSongInformation
    {
        public Task<RemoteState> GetStateAsync(CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync(() =>
        {
            if (main._closing) throw new InvalidOperationException("The player is closing.");
            return new RemoteState(main._current?.Id, main.NowTitle.Text, main.NowArtist.Text, main._player.IsPlaying,
                Math.Max(0, main._player.Time), Math.Max(0, main._player.Length), main._settings.Volume,
                main._cast.Renderer is not null ? main._cast.Name : main.AudioOutputLabel,
                main._queue.Take(500).Select(ToRemote).ToList(), main._catalog.Document.Playlists.Select(p => new RemotePlaylist(p.Id, p.Name, p.ItemIds.Count)).ToList(),
                main._current is null ? -1 : main._queueIndex, main._settings.BrowserPlaybackEnabled,
                main.ShuffleBox.IsChecked == true, main.RepeatBox.IsChecked == true, main._settings.AudioDeviceId,
                main.SetupAudioChoices().Select(d => new RemoteAudioOutput(d.Id, d.Name)).ToList(), main._cast.Renderer is null);
        }, DispatcherPriority.Background, cancellationToken).Task;

        public Task<RemoteLibrary> GetLibraryAsync(string query, string? playlistId, CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync(() =>
        {
            if (main._closing || main._scanning) throw new InvalidOperationException("Library is busy. Try again shortly.");
            var items = main.RemoteResults(query, playlistId);
            return new RemoteLibrary(items.Take(500).Select(ToRemote).ToList(), items.Count);
        }, DispatcherPriority.Background, cancellationToken).Task;

        public Task ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => main.Dispatcher.InvokeAsync(() => main.ExecuteRemote(command), DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }
}
