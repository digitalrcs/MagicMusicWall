# Native playback volume check

This opt-in Windows x64 console check uses the application's actual `PlaybackEngine.cs` and native VLC. It plays generated silent WAV data, never user media. It exercises Windows default output and one available endpoint selected explicitly, without changing the system default device or its master volume. It reuses the current endpoint when VLC reports its ID; otherwise it selects an explicit endpoint from VLC's inventory.

Supply an existing native VLC directory explicitly:

```powershell
dotnet run --project tests/MagicMusicWall.Playback.Checks -c Release -- --native-dir artifacts/release-staging/app/libvlc/win-x64
```

The JSON result records initial playback, slider changes, repeated transitions, zero volume, intentional mute and slider unmute. Success exits with code 0. A unique temporary fixture is removed when the run ends. An available Windows audio endpoint is required; this is not an ordinary unit test or a physical listening test.

Append `--legacy` to omit output recreation after Stop. That path reproduces the old transition order and is expected to fail when the first transition loses the changed volume.
