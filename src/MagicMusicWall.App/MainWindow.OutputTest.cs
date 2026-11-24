using System.IO;
using LibVLCSharp.Shared;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    internal async Task TestAudioOutput(string deviceId, int volume)
    {
        if (_closing) return;
        // A separate short player does not replace or seek the listening queue.
        var path = Path.Combine(App.DataDirectory, "output-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 44100, samples = rate;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
                for (var n = 0; n < samples; n++) { var fade = Math.Min(1d, Math.Min(n, samples - n) / 2205d); writer.Write((short)(Math.Sin(n * 2 * Math.PI * 440 / rate) * 4000 * fade)); }
            }
            using var player = new MediaPlayer(_vlc); player.SetAudioOutput("mmdevice");
            PlaybackEngine.ConfigureOutput(player, deviceId, moveActive: false); player.Volume = Math.Clamp(volume, 0, 30);
            using var media = new Media(_vlc, path, FromType.FromPath);
            if (!player.Play(media)) throw new IOException("The selected output could not play the test tone.");
            await Task.Delay(1400); await Task.Run(player.Stop);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
