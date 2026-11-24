using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim _sessionSaveGate = new(1, 1);
    private ListeningSessionStore? _sessionStore;
    private DateTime _nextSessionCheckpoint = DateTime.MinValue;
    private bool _sessionInitialized;
    private long? _pendingSessionPosition;

    private ListeningSession CaptureSession() => new()
    {
        ItemIds = _queue.Select(i => i.Id).ToList(), CurrentIndex = _queueIndex,
        PositionMilliseconds = Math.Max(0, _pendingSessionPosition ?? (_resumeTime > 0 ? _resumeTime : _player.Time)),
        WasPlaying = _pendingSessionPosition is null && _player.IsPlaying && !_resumePaused
    };

    private async Task RestoreListeningSessionAsync()
    {
        _sessionStore = new(App.DataDirectory);
        var saved = await Task.Run(_sessionStore.Load);
        if (_closing) return;
        if (_settings.RestoreSession && saved is not null)
        {
            var session = saved.Resolve(_catalog.Document.Items.Select(i => i.Id));
            var lookup = _catalog.Document.Items.ToDictionary(i => i.Id);
            foreach (var id in session.ItemIds) _queue.Add(lookup[id]);
            _queueIndex = session.CurrentIndex;
            if (_queueIndex >= 0)
            {
                _current = _queue[_queueIndex];
                _pendingSessionPosition = session.PositionMilliseconds;
                NowTitle.Text = _current.Title; NowArtist.Text = _current.Artist;
                QueueList.SelectedIndex = _queueIndex;
                ScrollQueueIndexIntoView(_queueIndex);
                if (_settings.ResumePlayback && session.WasPlaying) await PlayIndex(_queueIndex);
            }
            if (_queue.Count > 0) Status(_player.IsPlaying ? "Listening session restored." : "Listening session restored. Press Play when ready.");
        }
        _sessionInitialized = true;
        if (_sessionStore.LoadWarning is { } warning) Status(warning);
    }

    private async Task SaveListeningSessionAsync()
    {
        if (!_sessionInitialized || _sessionStore is null) return;
        var snapshot = CaptureSession();
        await _sessionSaveGate.WaitAsync();
        try { await Task.Run(() => _sessionStore.Save(snapshot)); }
        catch (Exception ex) { LogCleanup(ex); if (!_closing) Status("Listening session could not be saved. Check access to the app data folder."); }
        finally { _sessionSaveGate.Release(); }
    }

    private void CheckpointListeningSession()
    {
        if (!_sessionInitialized || _queueChanging || DateTime.UtcNow < _nextSessionCheckpoint) return;
        _nextSessionCheckpoint = DateTime.UtcNow.AddSeconds(5);
        _ = SaveListeningSessionAsync();
    }
}
