# Listening session and queue integration checks

On Windows with the .NET 10 SDK, run from the repository root:

```powershell
dotnet run --project tests/MagicMusicWall.Session.Checks -c Release
```

The harness opens the real WPF application using a newly generated temporary data directory, three silent WAV files, volume zero, and an exact duplicate queue. It verifies restored occurrence and position, paused restoration without loading VLC, actual playback seeking, reorder/undo continuity, clear/remove undo, Play next, external-file append, explicit duplicate removal, checkpoint persistence, and graceful shutdown. It prints the fixture directory for inspection. It never reads the installed library or starts the web remote.

For current documentation screenshots, opt in explicitly:

```powershell
$env:MMW_CAPTURE_SCREENSHOTS = '1'
dotnet run --project tests/MagicMusicWall.Session.Checks -c Release
Remove-Item Env:\MMW_CAPTURE_SCREENSHOTS
```

This overwrites `docs/images/library.png`, `library-compact.png`, `setup-sound.png`, and `manage-folders.png` with rendered screens containing clearly synthetic data. Inspect all images before publication. Do not run concurrent App builds while this harness is building.

Core boundary tests live in `MagicMusicWall.Core.Tests/ListeningSessionTests.cs`. The harness does not verify Bluetooth, TV/casting hardware, or media-key delivery by a physical keyboard.
