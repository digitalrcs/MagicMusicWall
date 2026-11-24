using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly RendererRegistry<RendererItem> _castRegistry = new();
    private readonly ObservableCollection<CastChoice> _renderers = [new(null)];
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private RendererDiscoverer? _discoverer;
    private CastChoice _cast = null!;
    private bool _changingCastSelection;

    private void InitializeCasting()
    {
        _cast = _renderers[0];
        CastDevicesBox.ItemsSource = _renderers;
        CastDevicesBox.SelectedItem = _cast;
    }

    private bool CastReadyForNewPlayback()
    {
        if (_cast.IsAvailable) return true;
        Status($"{_cast.Name}. Keep the receiver on and choose Find / refresh Chromecasts, or choose This PC.");
        return false;
    }

    private async void FindCast_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try { await RestartCastingDiscoveryAsync(); }
        catch (Exception ex) { if (!_closing) Error(ex); }
        finally { if (button is not null && !_closing) button.IsEnabled = true; }
    }

    private async Task RestartCastingDiscoveryAsync()
    {
        await _discoveryGate.WaitAsync();
        try
        {
            if (_closing) return;
            var generation = _castRegistry.BeginDiscovery();
            Status("Refreshing Chromecast discovery. Existing playback and your selected output are kept.");
            var previous = _discoverer; _discoverer = null;
            if (previous is not null) await Task.Run(() => StopAndDisposeDiscovery(previous));
            if (_closing) return;
            var discovery = new RendererDiscoverer(_vlc);
            discovery.ItemAdded += (_, args) => QueueRendererEvent(args.RendererItem, item => AddDiscoveredRenderer(generation, item));
            discovery.ItemDeleted += (_, args) => QueueRendererEvent(args.RendererItem, item =>
            {
                using (item)
                {
                    var row = _castRegistry.Remove(generation, item.NativeReference.ToInt64());
                    if (row is not null)
                        Status($"{row.Name} is no longer being advertised. Its row and your selected output are kept; refresh discovery to look again.");
                }
            });
            _discoverer = discovery;
            if (!await Task.Run(discovery.Start))
            {
                _discoverer = null;
                await Task.Run(discovery.Dispose);
                if (!_closing) Status("VLC could not start Chromecast discovery. Check the network and try Find / refresh Chromecasts again.");
            }
            else if (!_closing) Status("Listening for Chromecasts. Unavailable receivers stay listed; refresh discovery to look again.");
        }
        finally { _discoveryGate.Release(); }
    }

    private void QueueRendererEvent(RendererItem item, Action<RendererItem> action)
    {
        // Both LibVLCSharp event wrappers already hold one native reference. An
        // aborted UI dispatch must release it too, including during app shutdown.
        if (_closing || Dispatcher.HasShutdownStarted) { item.Dispose(); return; }
        try
        {
            var operation = Dispatcher.InvokeAsync(() =>
            {
                if (_closing) { item.Dispose(); return; }
                try { action(item); }
                catch (Exception ex) { Status($"Chromecast discovery update failed: {ex.Message}"); }
            });
            _ = operation.Task.ContinueWith(_ => item.Dispose(), CancellationToken.None,
                TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default);
        }
        catch { item.Dispose(); }
    }

    private void AddDiscoveredRenderer(int generation, RendererItem item)
    {
        var transferred = false;
        try
        {
            var descriptor = new RendererDescriptor(item.Name, item.Type, item.IconUri);
            var nativeId = item.NativeReference.ToInt64();
            transferred = true;
            var row = _castRegistry.Add(generation, nativeId, descriptor, item);
            if (row is null) return;
            RefreshCastRows();
            Status($"Found {_castRegistry.Entries.Count(e => e.IsAvailable)} available casting device(s). Your playback destination is unchanged.");
        }
        finally { if (!transferred) item.Dispose(); }
    }

    private void RefreshCastRows()
    {
        foreach (var choice in _renderers.Where(c => c.Entry is not null && !_castRegistry.Entries.Contains(c.Entry)).ToArray())
        { _renderers.Remove(choice); choice.Dispose(); }
        foreach (var row in _castRegistry.Entries)
            if (!_renderers.Any(c => ReferenceEquals(c.Entry, row))) _renderers.Add(new(row));
    }

    private async void CastDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _closing || _changingCastSelection || CastDevicesBox.SelectedItem is not CastChoice choice || ReferenceEquals(choice, _cast)) return;
        if (!choice.IsAvailable || _queueChanging)
        {
            _changingCastSelection = true;
            try { CastDevicesBox.SelectedItem = _cast; }
            finally { _changingCastSelection = false; }
            Status(!choice.IsAvailable ? $"{choice.Name}. Turn it on and choose Find / refresh Chromecasts to reconnect." : "The queue is changing. Select the output again in a moment.");
            return;
        }
        try { await SelectCast(choice); }
        catch (Exception ex) { if (!_closing) Error(ex); }
    }

    private async Task SelectCast(CastChoice choice)
    {
        if (choice.Entry is null && !LocalAudioReady(forceLocal: true))
        {
            _changingCastSelection = true;
            try { CastDevicesBox.SelectedItem = _cast; }
            finally { _changingCastSelection = false; }
            return;
        }
        // Acquire before any await: discovery can retire or replace this wrapper
        // while the command is waiting for the serialized playback gate.
        using var rendererLease = choice.Acquire();
        var renderer = rendererLease?.Value;
        var time = Math.Max(0, _player.Time); var paused = _player.State == VLCState.Paused;
        var active = _player.State is VLCState.Playing or VLCState.Paused or VLCState.Opening or VLCState.Buffering;
        _cast = choice; _castRegistry.Select(choice.Entry?.Id);
        ResetAudioRouteCheck();
        if (_current is not null && active && !_queueChanging) await PlayIndex(_queueIndex, time, paused, recordPlay: false);
        else await Playback(() => { if (!_player.SetRenderer(renderer)) throw new InvalidOperationException("Could not select this renderer."); return true; });
        if (!_closing) Status($"Playback destination: {choice.Name}.");
    }

    private async Task StopCastingDiscoveryAsync()
    {
        await _discoveryGate.WaitAsync();
        try
        {
            var discovery = _discoverer; _discoverer = null;
            if (discovery is not null) await Task.Run(() => StopAndDisposeDiscovery(discovery));
        }
        finally { _discoveryGate.Release(); }
    }

    private static void StopAndDisposeDiscovery(RendererDiscoverer discovery)
    {
        try { discovery.Stop(); }
        finally { discovery.Dispose(); }
    }

    private sealed class CastChoice : INotifyPropertyChanged, IDisposable
    {
        public CastChoice(RendererEntry<RendererItem>? entry)
        { Entry = entry; if (entry is not null) entry.PropertyChanged += EntryChanged; }
        public RendererEntry<RendererItem>? Entry { get; }
        public string Name => Entry?.DisplayName ?? "This PC";
        public bool IsAvailable => Entry?.IsAvailable ?? true;
        public RendererItem? Renderer => Entry?.CurrentResource;
        public RendererLease<RendererItem>? Acquire() => Entry?.Acquire();
        public event PropertyChangedEventHandler? PropertyChanged;
        private void EntryChanged(object? sender, PropertyChangedEventArgs e)
        { PropertyChanged?.Invoke(this, new(nameof(Name))); PropertyChanged?.Invoke(this, new(nameof(IsAvailable))); }
        public void Dispose() { if (Entry is not null) Entry.PropertyChanged -= EntryChanged; }
    }
}
