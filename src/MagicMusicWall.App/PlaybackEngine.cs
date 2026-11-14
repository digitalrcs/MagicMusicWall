using System.Globalization;
using LibVLCSharp.Shared;

namespace MagicMusicWall.App;

internal static class PlaybackEngine
{
    public static void ConfigureOutput(MediaPlayer player, string device, bool moveActive)
    {
        // Stop destroys the current aout. Persist the mmdevice setting so the
        // next aout inherits it; the module-less setter only moves a live aout.
        player.SetOutputDevice(device, "mmdevice");
        if (moveActive) player.SetOutputDevice(device);
    }

    public static LibVLC Create(int initialVolume, bool headless = false)
    {
        // mmdevice's default volume-save uses VLC's persistent session GUID.
        // The app owns its saved volume; use a process-local session so a silent
        // diagnostic run cannot leave the interactive player muted in Windows.
        var options = new List<string>
        {
            "--quiet", "--no-video-title-show", "--no-volume-save",
            "--mmdevice-volume=" + (Math.Clamp(initialVolume, 0, 100) / 100f).ToString(CultureInfo.InvariantCulture)
        };
        if (headless) options.Add("--vout=dummy");
        return new LibVLC(options.ToArray());
    }
}
