using System.Windows;
using System.Windows.Media;

namespace MagicMusicWall.App;

public sealed partial class PlaybackVisualizer
{
    // Adapted from DigitalRCS Franky's original scripts/generate_frog.py (MIT).
    // Preserve the character's geometry/palette; animate the singing expression.
    // See licenses/Franky-Art-LICENSE.txt and docs/PLAYBACK-VISUALS.md.
    private static Brush FrogBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze(); return brush;
    }
    private static readonly Brush FrogGreen = FrogBrush("#87c979"), FrogLight = FrogBrush("#d6efac"),
        FrogDark = FrogBrush("#163c30"), FrogCream = FrogBrush("#fff6d9"), FrogPink = FrogBrush("#ed9d83");

    private void DrawFranky(DrawingContext dc, double w, double h)
    {
        double scale = Math.Min(w / 440, h / 260);
        dc.PushTransform(new TranslateTransform(w / 2 - 160 * scale, h / 2 - 100 * scale));
        dc.PushTransform(new ScaleTransform(scale, scale));
        void Ellipse(double x, double y, double rx, double ry, Brush brush) => dc.DrawEllipse(brush, null, new Point(x, y), rx, ry);
        Ellipse(160, 192, 113 - _bounce * 12, 9, FrogDark);
        // Floating notes only appear with audio energy; no pretend singing in silence.
        if (_mouth > 0.04)
        {
            for (int i = 0; i < 6; i++)
            {
                double phase = (_time * 0.45 + i / 6.0) % 1;
                double x = i % 2 == 0 ? 24 - phase * 28 : 296 + phase * 28;
                double y = 144 - phase * 135;
                dc.PushOpacity((1 - phase) * _mouth);
                Ellipse(x, y, 5, 3.5, _colors[i * 10]);
                dc.DrawLine(new Pen(_colors[i * 10], 2), new Point(x + 4, y), new Point(x + 4, y - 19));
                dc.DrawLine(new Pen(_colors[i * 10], 2), new Point(x + 4, y - 19), new Point(x + 11, y - 15));
                dc.Pop();
            }
        }
        dc.PushTransform(new TranslateTransform(0, -_bounce * 14));
        dc.PushTransform(new RotateTransform(Math.Sin(_time * 3) * _mouth * 3, 160, 170));
        Ellipse(160, 116, 107, 68, FrogDark); Ellipse(160, 112, 103, 65, FrogGreen);
        foreach (double x in new[] { 107.0, 213.0 })
        {
            Ellipse(x, 63, 42, 43, FrogDark); Ellipse(x, 60, 38, 40, FrogGreen);
        }
        Ellipse(160, 135, 77, 35, FrogLight);
        bool blink = _time % 4.7 > 4.55;
        foreach (double x in new[] { 107.0, 213.0 })
        {
            if (blink) Line(dc, Enumerable.Range(-20, 41).Select(k => new Point(x + k, 64 + k * k / 100.0)), FrogDark, 4);
            else
            {
                Ellipse(x, 61, 28, 30, FrogCream);
                Ellipse(x, 65 - _mouth * 3, 12, 17, FrogDark);
                Ellipse(x - 4, 58 - _mouth * 3, 4, 5, Brushes.White);
                Ellipse(x + 5, 70 - _mouth * 3, 2, 2, FrogLight);
            }
        }
        Ellipse(82, 121, 15, 8, FrogPink); Ellipse(238, 121, 15, 8, FrogPink);
        Ellipse(149, 112, 3, 2, FrogDark); Ellipse(171, 112, 3, 2, FrogDark);
        if (_mouth < 0.04)
            Line(dc, Enumerable.Range(-34, 69).Select(k => new Point(160 + k, 138 + 13 * (1 - Math.Pow(k / 34.0, 2)))), FrogDark, 4);
        else
        {
            double opening = 2 + _mouth * 28;
            Ellipse(160, 145, 25 + _mouth * 14, opening, FrogDark);
            Ellipse(160, 145 + opening * 0.52, 16 + _mouth * 3, opening * 0.28, FrogPink);
        }
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
}
