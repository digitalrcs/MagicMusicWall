using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MagicMusicWall.App;

public sealed class PlaybackSeekSlider : Slider
{
    private readonly ToolTip _preview = new() { Placement = PlacementMode.Relative, StaysOpen = true };
    private bool _dragging;
    private double _original;
    public event EventHandler? SeekStarted;
    public event EventHandler? SeekCommitted;
    public event EventHandler? SeekCancelled;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled || Maximum <= Minimum) return;
        Focus();
        _original = Value;
        if (!CaptureMouse()) return;
        _dragging = true;
        SeekStarted?.Invoke(this, EventArgs.Empty);
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) MoveTo(e.GetPosition(this));
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        MoveTo(e.GetPosition(this));
        _dragging = false;
        _preview.IsOpen = false;
        ReleaseMouseCapture();
        SeekCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelSeek();
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape) { CancelSeek(); e.Handled = true; }
        else base.OnPreviewKeyDown(e);
    }
    public void CancelSeek()
    {
        if (!_dragging) return;
        _dragging = false;
        _preview.IsOpen = false;
        // Keep the owner's seek guard active while restoring the preview value.
        Value = Math.Clamp(_original, Minimum, Maximum);
        ReleaseMouseCapture();
        SeekCancelled?.Invoke(this, EventArgs.Empty);
    }
    private void MoveTo(Point point)
    {
        if (Template.FindName("PART_Track", this) is not Track track) return;
        var value = track.ValueFromPoint(TranslatePoint(point, track));
        if (!double.IsFinite(value)) return;
        Value = Math.Clamp(value, Minimum, Maximum);
        _preview.Content = $"{TimeLabel(Value)} / {TimeLabel(Maximum)}";
        _preview.PlacementTarget = this;
        _preview.HorizontalOffset = Math.Clamp(point.X, 0, ActualWidth);
        _preview.VerticalOffset = -32;
        _preview.IsOpen = true;
    }
    internal static string TimeLabel(double milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
