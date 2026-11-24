# ReplayGain digital-output checks

This Windows test needs .NET 10, the app's bundled VLC, and a local FFmpeg executable. FFmpeg is only a test fixture generator, not an application dependency.

```powershell
dotnet run --project tests/MagicMusicWall.ReplayGain.Checks -c Release -- --native-dir "src/MagicMusicWall.App/bin/Release/net10.0-windows/libvlc/win-x64" --ffmpeg "C:/path/to/ffmpeg.exe"
```

The test creates three short FLACs from a synthetic sine wave: untagged, -6 dB track gain, and +12 dB track gain with a declared peak of 0.95. It uses the production `PlaybackEngine` with audio-memory callbacks, never an audio device. Assertions compare decoded sample peaks: attenuation approximately 0.501, clipping protection capped near 1/0.95, and no change to untagged audio. Test-created files are removed afterward.

VLC 3 requires local metadata parsing before first playback for ReplayGain tags to take effect. Production playback performs that bounded parse on the playback worker. The app's setting applies after restart, uses track gain with 0 dB preamp/default, and enables peak protection. It does not analyze or write loudness tags. These checks verify tagged FLAC digital output; they do not establish identical behavior for every codec or external casting receiver, gapless playback, or crossfade.

The implementation follows [VideoLAN's VLC 3 gain selection](https://github.com/videolan/vlc/blob/3.0.x/src/audio_output/volume.c).
