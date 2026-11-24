# Native details and settings checks

Run on Windows with an interactive desktop:

```powershell
dotnet run --project tests/MagicMusicWall.Details.Checks -c Release
```

The harness creates its own unique temporary data directory before starting the real WPF application. It does not load the installed user's library. It checks that browsing Setup tabs or updating read-only connection text does not mark settings dirty, while changing volume does. It then checks the song-details article reader at normal and small sizes, the resizable divider, page scrolling and reader scrolling. Screenshots use fictional sample music and article text, without online lookups. The console prints the temporary screenshot location; failure exits nonzero.

This verifies native layout and event handling, not physical audio hardware or online provider availability.
