using System.Windows;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private MusicMapWindow? _musicMap;
    private int _musicMapStamp;
    private CancellationTokenSource? _musicAudioCancel;
    private Task<MusicMapScanResult>? _musicAudioTask;

    private void MusicMap_Click(object sender, RoutedEventArgs e)
    {
        if (_closing || !CanEdit()) return;
        if (_musicMap is not null) { FocusMusicMap(); _musicMap.Activate(); return; }
        _musicMapStamp = MusicMapStamp();
        _musicMap = new MusicMapWindow(BuildMusicMap, QueueFromMusicMap,
            id => ShowSongInfo(_catalog.Document.Items.FirstOrDefault(item => item.Id == id)), AnalyzeMusicMap,
            ids => { ShowMusicBrainz(ids, _musicMap ?? (Window)this); return Task.CompletedTask; }) { Owner = this };
        _musicMap.Closed += (_, _) => _musicMap = null;
        FocusMusicMap();
        _musicMap.Show();
    }

    private void FocusMusicMap()
    {
        bool Usable(MediaItem? item) => item is { IsVideo: false } && _catalog.Document.Items.Any(x => x.Id == item.Id && !x.IsVideo);
        var track = new[] { _current, QueueList.SelectedItem as MediaItem, MediaGrid.SelectedItem as MediaItem }.FirstOrDefault(Usable);
        if (track is not null) { _musicMap?.FocusTracks([track.Id]); return; }
        var folder = (MediaGrid.SelectedItem as FolderEntry)?.Path ?? _folderPath;
        if (folder is not null)
        {
            try
            {
                var page = FolderBrowser.Browse(_catalog.Document.Folders, _catalog.Document.Items.Where(x => !x.IsVideo), folder);
                if (page.Descendants.Count > 0) { _musicMap?.FocusTracks(page.Descendants.Select(x => x.Id).ToArray(), page.Title); return; }
            }
            catch (ArgumentException) { /* A removed folder can fall back to the queue. */ }
        }
        track = _queue.FirstOrDefault(Usable);
        if (track is not null) _musicMap?.FocusTracks([track.Id]);
    }

    private async Task<MusicMapIndex> BuildMusicMap(bool rescan)
    {
        if (_closing || _scanning || _metadataWriting)
            throw new InvalidOperationException("Wait for the current library operation to finish, then rebuild the map.");
        if (rescan) await RescanLibrary();
        if (_closing) throw new OperationCanceledException();
        // Copy on the dispatcher before building off-thread; catalog edits never race
        // enumeration or mutate a map that is already being displayed.
        var snapshot = _catalog.Document.Items.Select(item => new MediaItem
        {
            Id = item.Id, Path = item.Path, Title = item.Title, Artist = item.Artist,
            Album = item.Album, Genre = item.Genre, Year = item.Year,
            TrackNumber = item.TrackNumber, DiscNumber = item.DiscNumber,
            IsVideo = item.IsVideo, IsAvailable = item.IsAvailable, MusicBrainz = item.MusicBrainz
        }).ToArray();
        _musicMapStamp = MusicMapStamp();
        return await Task.Run(() => MusicMapIndex.Build(snapshot, MusicMapAudioScanner.LoadFeatures(App.DataDirectory, snapshot)));
    }

    private async Task<MusicMapScanResult> AnalyzeMusicMap(IProgress<MusicMapScanProgress> progress, CancellationToken cancellationToken)
    {
        if (_closing || _scanning || _metadataWriting || _musicAudioTask is not null)
            throw new InvalidOperationException("Wait for the current library operation to finish, then analyze sound.");
        var snapshot = _catalog.Document.Items.Where(x => !x.IsVideo).Select(x => new MediaItem
        { Id = x.Id, Path = x.Path, Title = x.Title }).ToArray();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _musicAudioCancel = linked;
        try
        {
            _musicAudioTask = Task.Run(() => MusicMapAudioScanner.ScanAsync(App.DataDirectory, snapshot, progress, linked.Token));
            return await _musicAudioTask;
        }
        finally { _musicAudioTask = null; _musicAudioCancel = null; }
    }

    private async Task StopMusicMapAnalysis()
    {
        if (_musicBrainzWindow is { } window) await window.ShutdownAsync();
        _musicAudioCancel?.Cancel();
        if (_musicAudioTask is { } task)
            try { await task; } catch (Exception ex) { LogCleanup(ex); }
    }

    private Task QueueFromMusicMap(IReadOnlyList<string> trackIds)
    {
        if (_closing || _queueChanging || _scanning || _metadataWriting)
            throw new InvalidOperationException("The player is busy. Try adding these songs again shortly.");
        var lookup = _catalog.Document.Items.Where(item => !item.IsVideo).ToDictionary(item => item.Id);
        var ids = trackIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Any(id => !lookup.ContainsKey(id)))
            throw new InvalidOperationException("The library changed. Rebuild the map before adding this selection.");
        foreach (var id in ids) _queue.Add(lookup[id]);
        Status($"Added {ids.Length:N0} song(s) from the music map. Queue contains {_queue.Count:N0} items.");
        return Task.CompletedTask;
    }

    private void RefreshMusicMapIfChanged()
    {
        if (_musicMap is null || _closing || _scanning || _metadataWriting) return;
        var stamp = MusicMapStamp();
        if (stamp == _musicMapStamp) return;
        _musicMapStamp = stamp;
        _musicMap.NotifyLibraryChanged();
    }

    private int MusicMapStamp()
    {
        var hash = new HashCode();
        foreach (var item in _catalog.Document.Items)
        {
            hash.Add(item.Id); hash.Add(item.Path); hash.Add(item.Title); hash.Add(item.Artist);
            hash.Add(item.Album); hash.Add(item.Genre); hash.Add(item.Year); hash.Add(item.IsVideo);
            hash.Add(item.TrackNumber); hash.Add(item.DiscNumber); hash.Add(item.IsAvailable);
            hash.Add(item.MusicBrainz);
        }
        return hash.ToHashCode();
    }
}
