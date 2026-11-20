using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private int _seekVersion;
    private long _markedDuration = -1;
    private double _markedWidth = -1;

    private void Seek_Begin(object? sender, EventArgs e)
    {
        _seekVersion = _playVersion;
        _seeking = true;
    }
    private void Seek_End(object? sender, EventArgs e)
    {
        _seeking = false;
        if (_seekVersion == _playVersion && _player.IsSeekable) _player.Time = (long)SeekSlider.Value;
    }
    private void Seek_Cancel(object? sender, EventArgs e) => _seeking = false;
    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingTime) return;
        if (_seeking) ElapsedText.Text = FormatTime((long)e.NewValue);
        else if (SeekSlider.IsKeyboardFocusWithin && _player.IsSeekable) _player.Time = (long)e.NewValue;
    }
    private void SeekMarks_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSeekMarks();
    private void UpdateSeekMarks()
    {
        if (!_ready || SeekMarks is null) return;
        long duration = Math.Max(0, _player.Length);
        double width = SeekMarks.ActualWidth;
        if (duration == _markedDuration && Math.Abs(width - _markedWidth) < 1) return;
        _markedDuration = duration; _markedWidth = width;
        SeekMarks.Children.Clear();
        if (duration <= 0 || width < 60) return;
        int intervals = width >= 360 ? 4 : 2;
        var brush = (Brush)FindResource("MutedBrush");
        for (int i = 0; i <= intervals; i++)
        {
            double x = width * i / intervals;
            var tick = new Line { X1 = x, X2 = x, Y1 = 0, Y2 = 4, Stroke = brush };
            SeekMarks.Children.Add(tick);
            var label = new TextBlock { Text = FormatTime(duration * i / intervals), FontSize = 10, Foreground = brush };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, 0, Math.Max(0, width - label.DesiredSize.Width)));
            Canvas.SetTop(label, 6);
            SeekMarks.Children.Add(label);
        }
    }
}
