using System.IO;
using System.Text.Json;
using System.Windows;
using LibVLCSharp.Shared;

namespace MagicMusicWall.App;

internal static class SmokeTest
{
    public static async Task RunAsync()
    {
        var checks = new List<string>();
        var success = false;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            using var vlc = PlaybackEngine.Create(0, headless: true);
            using var player = new MediaPlayer(vlc);
            checks.Add($"LibVLC {vlc.Version}");
            if (!player.SetAudioOutput("mmdevice")) throw new Exception("Windows audio output module unavailable");
            var devices = vlc.AudioOutputDevices("mmdevice");
            checks.Add($"Audio devices enumerated: {devices.Length}");
            player.SetOutputDevice("");
            player.Volume = 0;
            player.Mute = false;
            var file = Path.Combine(App.DataDirectory, "smoke.wav");
            WriteTone(file);
            using var media = new Media(vlc, new Uri(file));
            if (!player.Play(media)) throw new Exception("Play rejected synthetic WAV");
            var until = DateTime.UtcNow.AddSeconds(12);
            while (!player.IsPlaying && DateTime.UtcNow < until) await Task.Delay(100);
            if (!player.IsPlaying) throw new Exception($"Playback did not start: {player.State}");
            if (player.Volume != 0 || player.Mute) throw new Exception("Silent diagnostic session was not initialized independently");
            checks.Add("WAV playback started (independent session, volume zero)");
            await Task.Delay(600);
            if (player.Time <= 0) throw new Exception("Playback clock did not advance");
            checks.Add("Playback clock advanced");
            player.SetPause(true); await Task.Delay(150);
            if (player.State != VLCState.Paused) throw new Exception("Pause failed");
            checks.Add("Pause verified");
            player.Time = 2000;
            player.SetPause(false); await Task.Delay(300);
            if (player.Time < 1500) throw new Exception("Seek/resume failed");
            checks.Add("Seek/resume verified");
            var videoIndex = Array.IndexOf(App.Arguments, "--video-test");
            if (videoIndex >= 0 && videoIndex + 1 < App.Arguments.Length)
            {
                player.Stop();
                using var video = new Media(vlc, new Uri(Path.GetFullPath(App.Arguments[videoIndex + 1])));
                if (!player.Play(video)) throw new Exception("Video open failed");
                await Task.Delay(1800);
                if (video.Statistics.DecodedVideo <= 0) throw new Exception("No video frames decoded");
                checks.Add($"Video decoded: {video.Statistics.DecodedVideo} frames (headless output)");
            }
            if (!App.Arguments.Contains("--skip-discovery"))
            {
                using var discovery = new RendererDiscoverer(vlc);
                if (!discovery.Start()) throw new Exception("Chromecast discovery could not start");
                checks.Add("Chromecast discovery module started (receiver playback not tested)");
                discovery.Stop();
            }
            player.Stop();
            checks.Add("Playback stopped cleanly");
            success = true;
        }
        catch (Exception ex) { checks.Add(ex.ToString()); }
        File.WriteAllText(Path.Combine(App.DataDirectory, "smoke-result.json"), JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(success ? 0 : 1);
    }
    private static void WriteTone(string path)
    {
        const int rate = 44100, seconds = 6;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + rate * seconds * 2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(rate * seconds * 2);
        for (var i = 0; i < rate * seconds; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 1200));
    }
}
