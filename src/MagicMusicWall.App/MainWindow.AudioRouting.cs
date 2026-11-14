using System.Windows;
using LibVLCSharp.Shared;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private int _audioRouteGeneration;
    private bool _checkingAudioRoute, _audioRouteVerified;
    private DateTimeOffset _audioRouteGraceUntil;
    private string? _audioRouteWarning;

    private static bool SameOutput(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    private string PreferredAudioName => string.IsNullOrWhiteSpace(_settings.AudioDeviceName) ? "selected audio device" : _settings.AudioDeviceName;
    private string AudioOutputLabel => _audioRouteWarning is not null ? $"Output changed — paused · requested {PreferredAudioName}"
        : string.IsNullOrEmpty(_requestedAudioDevice) ? "Windows default output"
        : _audioRouteVerified ? PreferredAudioName : $"Requested: {PreferredAudioName}";

    private void ResetAudioRouteCheck()
    {
        ++_audioRouteGeneration;
        _audioRouteVerified = false;
        _audioRouteWarning = null;
        // A live device request is asynchronous inside VLC. This grace applies
        // only to an intentional selection/start, never to a later disconnect.
        _audioRouteGraceUntil = DateTimeOffset.UtcNow.AddMilliseconds(800);
        UpdateAudioRouteLabel();
    }

    private void UpdateAudioRouteLabel()
    {
        AudioRouteText.Text = _audioRouteWarning is not null ? "Output changed — playback paused. Reconnect and refresh, or choose another output." : (_cast?.Renderer is not null ? "Audio is playing on the casting receiver."
            : _audioRouteVerified ? "Selected output verified." : string.IsNullOrEmpty(_requestedAudioDevice)
                ? "Using Windows default audio output." : "Selected output will be verified during playback.");
        AudioRouteText.ToolTip = AudioOutputLabel;
    }

    private bool LocalAudioReady(bool forceLocal = false)
    {
        if ((!forceLocal && _cast.Renderer is not null) || string.IsNullOrEmpty(_requestedAudioDevice)) return true;
        try
        {
            if (_vlc.AudioOutputDevices("mmdevice").Any(d => SameOutput(d.DeviceIdentifier, _requestedAudioDevice))) return true;
            if (_cast.Renderer is not null)
                Status($"{PreferredAudioName} is unavailable. Choose a connected PC audio output before leaving the casting receiver.");
            else
            {
                WarnAudioRoute();
                var generation = _audioRouteGeneration;
                _ = Playback(() => { if (!_closing && generation == Volatile.Read(ref _audioRouteGeneration) && _player.IsPlaying) _player.SetPause(true); return true; });
            }
        }
        catch (Exception ex) { Status($"Could not check the selected audio output: {ex.Message}"); }
        return false;
    }

    private void WarnAudioRoute()
    {
        _audioRouteVerified = false;
        if (_audioRouteWarning is not null) return;
        _audioRouteWarning = $"{PreferredAudioName} is unavailable or the output changed. Playback is paused. Reconnect, refresh outputs, then press Play; or choose another output.";
        UpdateAudioRouteLabel();
        Status(_audioRouteWarning);
    }

    private void Player_AudioDevice(object? sender, MediaPlayerAudioDeviceEventArgs e)
    {
        // Native event callbacks must not call back into VLC (its audio lock can
        // still be held). Read the latest route later, not this possibly stale event.
        if (_closing || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(async () => await CheckAudioRoute());
    }

    private async Task CheckAudioRoute()
    {
        if (_closing || !_ready || _current is null || _checkingAudioRoute || _cast.Renderer is not null || string.IsNullOrEmpty(_requestedAudioDevice)) return;
        _checkingAudioRoute = true;
        var generation = _audioRouteGeneration;
        var requested = Volatile.Read(ref _requestedAudioDevice);
        var wasVerified = _audioRouteVerified;
        var graceUntil = _audioRouteGraceUntil;
        try
        {
            var result = await Playback<(bool Checked, bool Mismatch)>(() =>
            {
                if (_closing || generation != Volatile.Read(ref _audioRouteGeneration) || !SameOutput(requested, Volatile.Read(ref _requestedAudioDevice))) return (Checked: false, Mismatch: false);
                var playing = _player.State == VLCState.Playing;
                if ((!playing && _player.State != VLCState.Paused) || _player.AudioTrack < 0) return (false, false);
                var actual = _player.OutputDevice;
                var mismatch = requested.Length > 0 && !SameOutput(requested, actual);
                if (mismatch && !wasVerified && DateTimeOffset.UtcNow < graceUntil) return (false, false);
                if (mismatch && playing) _player.SetPause(true);
                return (true, mismatch);
            });
            if (_closing || generation != _audioRouteGeneration || !result.Checked) return;
            if (result.Mismatch) WarnAudioRoute();
            else if (_audioRouteWarning is null) { _audioRouteVerified = true; UpdateAudioRouteLabel(); }
        }
        catch (Exception ex) { if (!_closing) Status($"Audio output could not be verified: {ex.Message}"); }
        finally { _checkingAudioRoute = false; }
    }

    private async Task<bool> PrepareAudioResume()
    {
        if (_cast.Renderer is not null) return true;
        if (!LocalAudioReady()) return false;
        ResetAudioRouteCheck();
        await Playback(() => { ApplyRequestedAudio(_player.Mute, moveActive: !SameOutput(_player.OutputDevice, Volatile.Read(ref _requestedAudioDevice))); return true; });
        return true;
    }
}
