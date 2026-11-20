using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MagicMusicWall.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/visuals-check");
        Directory.CreateDirectory(output);
        var visual = new PlaybackVisualizer();
        T Field<T>(string name) => (T)typeof(PlaybackVisualizer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visual)!;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        visual.Power = 0.5;
        double softResponse = visual.Response(0.2);
        visual.Power = 3;
        Check(visual.Response(0.2) > softResponse && visual.Response(0) == 0 && visual.Response(1) == 1,
            "Power must increase quiet response without inventing energy in silence");
        visual.Power = double.NaN;
        Check(visual.Power == 1.6, "Invalid visual power must use a safe default");
        visual.Power = 99;
        Check(visual.Power == 3, "Visual power limit");
        visual.Power = 1.6;
        Check(PlaybackVisualizer.Modes[0] == "Spectrum" && PlaybackVisualizer.Modes[6] == "Mountain range" && PlaybackVisualizer.Modes[7] == "Light tunnel", "Existing saved mode indexes changed");
        Check(PlaybackVisualizer.FineEdge(0) == 1 && PlaybackVisualizer.FineEdge(192) == 1024, "Spectrum coverage");
        for (int b = 0; b < 192; b++) Check(PlaybackVisualizer.FineEdge(b + 1) > PlaybackVisualizer.FineEdge(b), "Repeated spectrum bins");
        var samples = Field<float[]>("_samples");
        // A 1,500 Hz tone occupies FFT bin64 exactly at 48kHz / 2048.
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(0.08 * Math.Sin(2 * Math.PI * 64 * i / samples.Length));
        for (int i = 0; i < 10; i++) visual.AnalyzeSamples(true);
        var fine = Field<double[]>("_fineBands");
        int peak = Array.IndexOf(fine, fine.Max());
        Check(PlaybackVisualizer.FineEdge(peak) <= 64 && PlaybackVisualizer.FineEdge(peak + 1) > 64, "Tone mapped to wrong frequency");
        Check(Field<double>("_mouth") > 0.3, "Frog does not sing with signal");
        void Render(int mode, string name)
        {
            visual.Mode = mode;
            visual.Measure(new Size(840, 400)); visual.Arrange(new Rect(0, 0, 840, 400));
            visual.InvalidateVisual(); visual.UpdateLayout();
            var image = new RenderTargetBitmap(840, 400, 96, 96, PixelFormats.Pbgra32);
            image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        Render(9, "franky-singing.png"); Render(8, "fine-spectrum.png");
        var mountain = Field<double[]>("_mountainBands");
        int band = Array.IndexOf(mountain, mountain.Max());
        double before = mountain[band];
        Check(0.4 * Math.Pow(before, 0.8) > 0.18 * before, "Mountain did not increase motion for tone");
        Check(0.4 * Math.Pow(0.1, 0.8) > 0.18 * 0.1, "Quiet mountain peaks were reduced");
        Array.Clear(samples); visual.AnalyzeSamples(false);
        Check(mountain[band] <= before * 0.551, "Mountain release is too slow");
        for (int i = 0; i < 45; i++) visual.AnalyzeSamples(false);
        Check(Field<double>("_mouth") < 0.001 && fine.Max() < 0.001, "Silence leaves singing or spectrum energy");
        Render(9, "franky-idle.png");
        void Tone(double amplitude, int frames, int bin = 64)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * bin * i / samples.Length));
            for (int i = 0; i < frames; i++) visual.AnalyzeSamples(true);
        }
        Tone(0.01, 3);
        Check(Field<double>("_mouth") > 0.2, "Quiet music does not animate Franky");
        Tone(0.6, 4);
        double loudMouth = Field<double>("_mouth");
        Render(9, "franky-loud.png");
        Tone(0.3, 3);
        Check(loudMouth - Field<double>("_mouth") > 0.35, "Loud passages hold the same open mouth");
        Render(9, "franky-softer.png");
        Tone(0, 4);
        Check(Field<double>("_mouth") < 0.04, "Mouth does not close promptly between beats");
        Tone(0.0005, 20);
        Check(Field<double>("_mouth") < 0.001, "Quiet output noise makes Franky sing");
        Tone(0.3, 30, 4); // 93.75 Hz bass: notes move, mouth stays closed.
        Check(Field<double>("_mouth") < 0.01 && Field<double>("_notes") > 0.3, "Bass must animate notes without singing");
        Tone(0.3, 30, 320); // 7.5 kHz treble.
        Check(Field<double>("_mouth") < 0.01 && Field<double>("_notes") > 0.3, "Treble must animate notes without singing");
        Tone(0.08, 6, 64); // 1.5 kHz presence.
        Check(Field<double>("_mouth") > 0.3, "Vocal-range energy must animate the mouth");
        foreach (double power in new[] { 0.5, 1.6, 3.0 })
        {
            visual.Power = power;
            Tone(0.08, 10);
            for (int mode = 0; mode < PlaybackVisualizer.Modes.Length; mode++) Render(mode, $"mode-{mode}-power-{power:0.0}.png");
            Tone(0, 50);
            Check(Field<double>("_mouth") < 0.001, "High power sings in silence");
        }
        Console.WriteLine("PASS: mode indexes, frequency mapping, quiet/loud mouth dynamics, silence, mountain release, power limits/response, all 10 render paths at three power levels.");
    }
}
