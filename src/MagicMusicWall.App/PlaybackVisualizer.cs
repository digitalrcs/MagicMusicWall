using System.Numerics;
using System.Windows;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MagicMusicWall.App;

// Output-only loopback: never opens a microphone or changes VLC's output callbacks.
internal sealed class VisualizerCapture : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly object _sync = new();
    private readonly float[] _ring = new float[2048];
    private int _position;
    private long _lastData;
    public string Name => _device.FriendlyName;
    public string? Error { get; private set; }
    public static string DefaultOutputId()
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.ID;
    }
    public VisualizerCapture(string deviceId)
    {
        using var devices = new MMDeviceEnumerator();
        _device = string.IsNullOrEmpty(deviceId)
            ? devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : devices.GetDevice(deviceId);
        _capture = new WasapiLoopbackCapture(_device);
        _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) => Error = e.Exception?.Message ?? "Audio capture stopped. Click Reconnect visuals.";
        try { _capture.StartRecording(); }
        catch { _capture.Dispose(); _device.Dispose(); throw; }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            for (var offset = 0; offset + 8 <= e.BytesRecorded; offset += 8)
            {
                var sample = (BitConverter.ToSingle(e.Buffer, offset) + BitConverter.ToSingle(e.Buffer, offset + 4)) / 2;
                _ring[_position] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
                _position = (_position + 1) % _ring.Length;
            }
            _lastData = Environment.TickCount64;
        }
    }
    public void Read(float[] samples)
    {
        lock (_sync)
        {
            if (Environment.TickCount64 - _lastData > 300) { Array.Clear(samples); return; }
            for (var i = 0; i < samples.Length; i++) samples[i] = _ring[(_position + i) % _ring.Length];
        }
    }
    public void Dispose() { _capture.DataAvailable -= OnData; _capture.Dispose(); _device.Dispose(); }
}

public sealed partial class PlaybackVisualizer : FrameworkElement
{
    public static readonly string[] Modes = ["Spectrum", "Mirror bars", "Waveform", "Orbit rings", "Star voyage", "Nebula", "Mountain range", "Light tunnel", "Fine spectrum", "Franky sings"];
    public int Mode { get; set; }
    private readonly float[] _samples = new float[2048];
    private readonly Complex[] _fft = new Complex[2048];
    private readonly double[] _bands = new double[64];
    private readonly double[] _fineBands = new double[192];
    private readonly double[] _mountainBands = new double[64];
    private double _mouth, _bounce;
    // Strictly increasing edges cover bins 1..1023 without repeating bass bins.
    internal static int FineEdge(int edge) => 1 + edge + (int)(831 * Math.Pow(edge / 192.0, 2));
    private readonly Brush[] _colors = Enumerable.Range(0, 64).Select(i =>
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(100 + i * 2), (byte)(210 - i * 2), 255));
        brush.Freeze(); return (Brush)brush;
    }).ToArray();
    private double _time, _energy;
    internal void Advance(VisualizerCapture? capture, bool playing)
    {
        Array.Clear(_samples);
        if (playing) capture?.Read(_samples);
        AnalyzeSamples(playing);
    }
    internal void AnalyzeSamples(bool playing)
    {
        for (int i = 0; i < 2048; i++) _fft[i] = _samples[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / 2047));
        // Iterative radix-2 FFT; the display uses logarithmic frequency buckets.
        for (int i = 1, j = 0; i < 2048; i++)
        {
            int bit = 1024;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (_fft[i], _fft[j]) = (_fft[j], _fft[i]);
        }
        for (int size = 2; size <= 2048; size <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (int start = 0; start < 2048; start += size)
            {
                var phase = Complex.One;
                for (int j = 0; j < size / 2; j++)
                {
                    var even = _fft[start + j]; var odd = _fft[start + j + size / 2] * phase;
                    _fft[start + j] = even + odd; _fft[start + j + size / 2] = even - odd; phase *= step;
                }
            }
        }
        for (int b = 0; b < 64; b++)
        {
            int low = (int)Math.Pow(1024, b / 64.0), high = Math.Min(1024, Math.Max(low + 1, (int)Math.Pow(1024, (b + 1) / 64.0)));
            double peak = 0;
            for (int i = low; i < high; i++) peak = Math.Max(peak, _fft[i].Magnitude / 512);
            var target = Math.Clamp((20 * Math.Log10(Math.Max(1e-6, peak)) + 65) / 65, 0, 1);
            _bands[b] = Math.Max(target, _bands[b] * 0.87);
            _mountainBands[b] = Math.Max(target, _mountainBands[b] * 0.55);
        }
        for (int b = 0; b < _fineBands.Length; b++)
        {
            double peak = 0;
            for (int i = FineEdge(b); i < FineEdge(b + 1); i++) peak = Math.Max(peak, _fft[i].Magnitude / 512);
            var target = Math.Clamp((20 * Math.Log10(Math.Max(1e-6, peak)) + 65) / 65, 0, 1);
            _fineBands[b] = Math.Max(target, _fineBands[b] * 0.8);
        }
        double rms = Math.Sqrt(_samples.Sum(sample => (double)sample * sample) / _samples.Length);
        double mouthTarget = playing && rms >= 0.001 ? Math.Clamp(Math.Sqrt(rms) * 2.6, 0, 1) : 0;
        _mouth += (mouthTarget - _mouth) * (mouthTarget > _mouth ? 0.65 : 0.3);
        _bounce += ((playing ? Math.Clamp(rms * 6, 0, 1) : 0) - _bounce) * 0.5;
        _energy = _bands.Take(20).Average();
        if (playing) _time += 0.033 * (0.25 + _energy * 2);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, cx = w / 2, cy = h / 2;
        if (w < 1 || h < 1) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 11, 24)), null, new Rect(0, 0, w, h));
        if (Mode is 0 or 1)
        {
            for (int i = 0; i < 64; i++)
            {
                double height = Math.Max(2, _bands[i] * h * (Mode == 1 ? 0.44 : 0.85));
                dc.DrawRoundedRectangle(_colors[i], null, new Rect(i * w / 64 + 1, Mode == 1 ? cy - height : h - height - 8, Math.Max(1, w / 64 - 3), Mode == 1 ? height * 2 : height), 2, 2);
            }
        }
        else if (Mode == 2)
        {
            var points = Enumerable.Range(0, 512).Select(i => new Point(i * w / 511, cy + _samples[i * 4] * h * 0.45));
            Line(dc, points, _colors[22], 2);
        }
        else if (Mode == 3)
        {
            for (int ring = 0; ring < 4; ring++)
            {
                int r = ring;
                Line(dc, Enumerable.Range(0, 129).Select(i =>
                {
                    double angle = i * Math.PI / 64 + _time * (r % 2 == 0 ? 0.12 : -0.12);
                    double radius = Math.Min(w, h) * (0.12 + r * 0.07 + _bands[i % 64] * 0.1);
                    return new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);
                }), _colors[ring * 16], 2);
            }
        }
        else if (Mode == 4)
        {
            for (int i = 0; i < 220; i++)
            {
                double z = 0.08 + ((i * 0.61803398875 + _time * 0.12) % 1), angle = i * 2.39996;
                double radius = (0.05 + (i % 17) / 24.0) / z;
                var p = new Point(cx + Math.Cos(angle) * radius * w * 0.35, cy + Math.Sin(angle) * radius * h * 0.35);
                dc.DrawEllipse(_colors[i % 64], null, p, (1 + _energy * 2) / z, (1 + _energy * 2) / z);
            }
        }
        else if (Mode == 5)
        {
            for (int i = 0; i < 100; i++)
            {
                double angle = i * 2.39996 + _time * 0.12, radius = Math.Sqrt(i / 100.0) * Math.Min(w, h) * 0.4;
                double size = 3 + _bands[i % 64] * 22;
                dc.PushOpacity(0.18 + _bands[i % 64] * 0.6);
                dc.DrawEllipse(_colors[i % 64], null, new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius * 0.65), size * 1.8, size);
                dc.Pop();
            }
        }
        else if (Mode == 6)
        {
            for (int row = 0; row < 8; row++)
            {
                int r = row;
                Line(dc, Enumerable.Range(0, 64).Select(i => new Point(i * w / 63, h * (0.46 + r * 0.067) - Math.Pow(_mountainBands[(i + r * 3) % 64], 0.8) * h * 0.4)), _colors[row * 8], 1.5);
            }
        }
        else if (Mode == 8)
        {
            double cell = w / _fineBands.Length, gap = Math.Min(1, cell * 0.2);
            for (int i = 0; i < _fineBands.Length; i++)
            {
                double height = Math.Max(1, _fineBands[i] * (h - 16) * 0.9);
                dc.DrawRectangle(_colors[i * 64 / _fineBands.Length], null,
                    new Rect(i * cell + gap / 2, h - height - 8, cell - gap, height));
            }
        }
        else if (Mode == 9) DrawFranky(dc, w, h);
        else
        {
            for (int ring = 0; ring < 18; ring++)
            {
                double depth = (ring / 18.0 + _time * 0.08) % 1;
                Line(dc, Enumerable.Range(0, 7).Select(i =>
                {
                    double angle = i * Math.PI / 3 + _time * 0.08;
                    double radius = depth * Math.Max(w, h) * (0.5 + _bands[ring * 3] * 0.25);
                    return new Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);
                }), _colors[ring * 3], 1 + depth * 2);
            }
        }
        dc.Pop();
    }
    private static void Line(DrawingContext dc, IEnumerable<Point> points, Brush brush, double width)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            bool first = true;
            foreach (var point in points) { if (first) { context.BeginFigure(point, false, false); first = false; } else context.LineTo(point, true, false); }
        }
        geometry.Freeze(); dc.DrawGeometry(null, new Pen(brush, width), geometry);
    }
}
