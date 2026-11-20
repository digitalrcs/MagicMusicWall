using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _visualTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private VisualizerCapture? _visualCapture;
    private string? _visualRoute;
    private bool _visualsInitialized;

    private void InitializeVisuals()
    {
        LibraryTools.IsExpanded = _settings.LibraryToolsExpanded;
        VisualStyle.ItemsSource = PlaybackVisualizer.Modes;
        VisualStyle.SelectedIndex = Math.Clamp(_settings.VisualizationMode, 0, PlaybackVisualizer.Modes.Length - 1);
        _visualTimer.Tick += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                MusicVisual.Advance(_visualCapture, _player.IsPlaying && _cast.Renderer is null);
        };
        _visualsInitialized = true;
    }
    private void ToolsExpansion_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.LibraryToolsExpanded = LibraryTools.IsExpanded;
        ScheduleSettingsSave();
    }
    private void CenterMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_visualsInitialized) return;
        bool visible = VisualMode.IsChecked == true;
        CollectionPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        VisualizerPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) { _visualTimer.Start(); RefreshVisualizerCapture(); }
        else StopVisuals();
    }
    private void VisualStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MusicVisual is null || VisualStyle.SelectedIndex < 0) return;
        MusicVisual.Mode = VisualStyle.SelectedIndex;
        if (!_ready) return;
        _settings.VisualizationMode = VisualStyle.SelectedIndex;
        ScheduleSettingsSave();
    }
    private void RefreshVisualizerCapture()
    {
        if (!_visualsInitialized || VisualMode.IsChecked != true || _closing) return;
        bool localPlaying = _player.IsPlaying && _cast.Renderer is null && WindowState != WindowState.Minimized;
        if (!localPlaying)
        {
            ReleaseVisualCapture();
            VisualStatus.Text = _cast.Renderer is not null ? "Visuals require playback on a PC audio output; casting audio is on the receiver." : "Play a song on this PC to animate the visuals.";
            return;
        }
        var route = _requestedAudioDevice;
        try { if (string.IsNullOrEmpty(route)) route = VisualizerCapture.DefaultOutputId(); }
        catch (Exception ex)
        {
            ReleaseVisualCapture();
            VisualStatus.Text = "No Windows output available: " + ex.Message;
            return;
        }
        if (_visualRoute != route)
        {
            ReleaseVisualCapture();
            _visualRoute = route;
            try { _visualCapture = new VisualizerCapture(route); }
            catch (Exception ex) { VisualStatus.Text = "Visuals unavailable: " + ex.Message + " Try Reconnect visuals. Playback is unaffected."; }
        }
        if (_visualCapture is not null)
            VisualStatus.Text = _visualCapture.Error is { } error ? "Visuals unavailable: " + error
                : $"Live output: {_visualCapture.Name} · includes other apps on this output. No microphone or recording.";
    }
    private void ReconnectVisuals_Click(object sender, RoutedEventArgs e) { ReleaseVisualCapture(); RefreshVisualizerCapture(); }
    private void ReleaseVisualCapture()
    {
        try { _visualCapture?.Dispose(); }
        catch (Exception ex) { LogCleanup(ex); }
        _visualCapture = null; _visualRoute = null;
    }
    private void StopVisuals() { _visualTimer.Stop(); ReleaseVisualCapture(); }
}
