# Build and package Magic Music Wall

Applies to Magic Music Wall **0.9.11**.

[Documentation home](README.md) · [Contributing](../CONTRIBUTING.md)

These instructions are for developers. To use the application, download the [Windows installer or portable package](WINDOWS-INSTALLER.md) instead.

## Requirements

- Windows with the .NET 10 SDK. `global.json` selects the 10.0 SDK feature band and permits the configured roll-forward behavior.
- Git for source checkout.
- Internet access for the initial NuGet restore.
- Inno Setup 6 for the Windows installer.
- PowerShell 7 (`pwsh`) for release publishing and offline-document generation.
- Any asset-generation tools required by the installer build script, if regenerating its branding assets.

The app targets `net10.0-windows` and x64, uses WPF, and bundles LibVLC through NuGet. Runtime installations do not need the SDK or a separately installed VLC player.

## Build and run

From the repository root:

```powershell
dotnet restore MagicMusicWall.sln
dotnet build MagicMusicWall.sln -c Release
dotnet run --project src\MagicMusicWall.App\MagicMusicWall.App.csproj -c Release -- --data-dir "$env:TEMP\MagicMusicWall-Development"
```

Use a separate `--data-dir` when developing so a new build does not alter your everyday library. Only one app instance may use a particular data directory at a time.

The normal application data is not a source-code asset. Do not commit `library.json`, `settings.json`, logs, access links, provider tokens, private media paths or a portable Data folder.

## Tests and focused checks

Run automated tests when changing the code they cover:

```powershell
dotnet test MagicMusicWall.sln -c Release
```

A targeted metadata check, for example:

```powershell
dotnet test tests\MagicMusicWall.Core.Tests\MagicMusicWall.Core.Tests.csproj -c Release --filter "FullyQualifiedName~MediaTagServiceTests|FullyQualifiedName~MovieLookupServiceTests|FullyQualifiedName~WrittenMetadataCatalogTests"
```

Use synthetic media or disposable copies for file-write tests. Preserve both success and failure cases: selected tags, untouched metadata, backups, locked/read-only files and unsupported formats.

For constellation grouping, synthetic audio features and sound ranking:

```powershell
dotnet test tests/MagicMusicWall.Core.Tests -c Release --filter "FullyQualifiedName~MusicMapIndexTests|FullyQualifiedName~AudioAnalysisTests"
dotnet run --project tests/MagicMusicWall.MusicMap.Checks -c Release -- src/MagicMusicWall.App/bin/Release/net10.0-windows/libvlc/win-x64
```

Build the App first for the bundled VLC path. The second command generates a temporary WAV and checks silent decoding, cache reuse, changed-file invalidation, cancellation, unavailable media and corrupt-cache fallback. Neither command scans your library or tests perceived recommendation quality. The analysis cache and scan report are private runtime data, excluded from public packages.

`scripts\Smoke-Test.ps1` performs a separate synthetic, zero-volume playback check. Its audio/discovery results do not prove that sound reaches a physical Bluetooth speaker or that a TV can play a cast. Discovery can trigger a Windows Firewall prompt.

For native playback volume checks on Windows, run the opt-in console project against the VLC directory of an existing published payload:

```powershell
dotnet run --project tests/MagicMusicWall.Playback.Checks -c Release -- --native-dir artifacts/release-staging/0.9.11/app/libvlc/win-x64
```

Publish the payload first, or substitute another existing `libvlc/win-x64` directory. The check generates a temporary silent WAV and verifies VLC volume and mute state across track changes. It is separate from `dotnet test` and does not verify audible output quality or Bluetooth hardware.

For remote sign-in and command validation:

```powershell
dotnet test tests/MagicMusicWall.Streaming.Tests/MagicMusicWall.Streaming.Tests.csproj -c Release --filter "FullyQualifiedName~RemoteSignInTests|FullyQualifiedName~RemoteControlServerTests"
```

`RemotePassphrase` saves a salted PBKDF2-HMAC-SHA256 verifier with 600,000 iterations using the [.NET one-shot PBKDF2 API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0), consistent with [OWASP's PBKDF2 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2). It does not save the passphrase. `RemoteSignIn` issues expiring signed HttpOnly/SameSite cookies bound to the verifier and access key, limits login attempts, and retains the server's Host/Origin checks. The listener still uses HTTP on a trusted LAN; these controls do not provide transport encryption.

For the local music constellation index:

```powershell
dotnet test tests/MagicMusicWall.Core.Tests/MagicMusicWall.Core.Tests.csproj -c Release --filter FullyQualifiedName~MusicMapIndexTests
```

`MusicMapIndex` snapshots indexed non-video entries into artist, album and song entities. Stable IDs and raw `TrackIds` preserve queue integration. Related results are computed locally on demand for one selected entity using library metadata, optional saved MusicBrainz genres/labels/instrument credits, and cached sound features. The index does not construct an all-pairs graph. `TaggedTrackCount` counts tracks with usable genre information, not complete metadata. The WPF window pages browse results and draws a bounded neighborhood around its selection.

MusicBrainz enrichment is a separate, explicitly started API workflow. `MediaItem.MusicBrainz` persists in `library.json` alongside original tag values, so a lookup does not rewrite a media file. Search requests use artist/title and optional album text; detail requests use MusicBrainz IDs, with duration checks performed locally. Requests share the existing MusicBrainz rate gate. Opening or rebuilding the map only reads saved information. Saved MusicBrainz rows, source links and search/scan logs are personal runtime data and must stay out of release fixtures and packages. No additional runtime dependency is required for enrichment.

## Project layout

| Directory | Responsibility |
| --- | --- |
| `src/MagicMusicWall.App` | WPF player, Setup, metadata UI, playback/casting integration |
| `src/MagicMusicWall.Core` | Library, playlists, settings models, lookup services and file-tag writing |
| `src/MagicMusicWall.Streaming` | Home-network remote, browser page and file streaming |
| `tests` | Core and streaming checks |
| `installer` | Inno Setup definition and installer branding |
| `scripts` | Build, publish, packaging and validation helpers |
| `docs` | User/developer guides and demonstration screenshots |
| `licenses` | Component license texts and source references |
| `artifacts` | Ignored generated outputs; never a source publication input |

## Publish a self-contained application

`scripts\Publish.ps1` publishes a Windows x64 payload with its .NET runtime and VLC files. Keep the whole payload together; this is not a single-EXE distribution.

Build release packages from a fresh, dedicated staging directory. Do not overwrite the folder of a running app and do not stage user data or personal media. These examples use the versioned staging directory `artifacts\release-staging\0.9.11\app`.

Publish the staged payload with:

```powershell
pwsh -File scripts/Publish.ps1 -OutputDirectory artifacts/release-staging/0.9.11/app -SkipArchive
```

The publish script assembles the application, required runtime/native libraries, third-party notices/license texts, and **OfflineDocs**. It renders the Markdown documentation using PowerShell 7's `ConvertFrom-Markdown`. `-SkipArchive` leaves archive creation to the release-packaging script.

## Compile the installer and package the release

Once the staged payload is ready:

```powershell
.\scripts\Build-Installer.ps1 -PayloadDirectory .\artifacts\release-staging\0.9.11\app -OutputDirectory .\artifacts\release\0.9.11 -Version 0.9.11
.\scripts\Package-ThirdPartySources.ps1 -OutputDirectory .\artifacts\release\0.9.11 -Version 0.9.11
.\scripts\Package-Release.ps1 -PayloadDirectory .\artifacts\release-staging\0.9.11\app -OutputDirectory .\artifacts\release\0.9.11 -Version 0.9.11 -AdditionalAsset .\artifacts\release\0.9.11\MagicMusicWall-0.9.11-ThirdParty-Sources.zip
```

`Build-Installer.ps1` also accepts `-IsccPath` when the Inno compiler is installed in a nonstandard location. `Package-Release.ps1` accepts `-InstallerPath` for an already built installer and `-AdditionalAsset` for reviewed release companion files. It packages prepared inputs; it does not rebuild the application.

Expected release files (publish the six files other than the optional individual `.sha256` sidecars):

```text
artifacts/release/0.9.11/
  MagicMusicWall-0.9.11-Setup-x64.exe
  MagicMusicWall-0.9.11-Setup-x64.exe.sha256
  MagicMusicWall-0.9.11-Portable-x64.zip
  MagicMusicWall-0.9.11-ThirdParty-Sources.zip
  MagicMusicWall-0.9.11-ThirdParty-Sources.zip.sha256
  MagicMusicWall-0.9.11-USB-Kit.zip
  START-HERE.txt
  SHA256SUMS.txt
```

The USB kit includes the installer, portable ZIP, `START-HERE.txt`, checksums and any explicitly supplied companion assets. The portable ZIP has the app, `Start-MagicMusicWall.cmd` and OfflineDocs at its root. The launcher passes `--portable`, placing Data beside the app; paths under the previous portable root are rebased when the folder moves. Other media paths remain unchanged. The installer excludes the portable launcher, portable marker and Data.

## Before public publication

Verify the staged payload and final archives rather than assuming that a successful build makes them release-ready:

- Confirm the displayed app/package version and expected filenames.
- Check that all required runtime files, notices and offline guides are present.
- Check that library/settings files, local credentials, personal media, logs and development fixtures are absent.
- Inspect the installer wizard, branding and documented user path. Test installation/uninstallation in an appropriate disposable environment when available.
- Verify download sizes and SHA-256 values after upload, and keep validation limits explicit in release notes.
- Retain the component notices and any corresponding-source/relink materials required for the binaries being distributed.

Do not claim a clean Windows install, ARM support, Bluetooth sound or physical TV playback based only on compilation, unit tests or screenshots. The current installer is unsigned; signing requires an appropriate publisher certificate and a separate release decision.

## Focused visualizer checks

Run `dotnet run --project tests/MagicMusicWall.Visuals.Checks -c Release` on Windows. This checks frequency mapping, quiet/loud mouth dynamics, silence, power limits, and all ten render paths at three intensity levels. Rendered previews are written to `artifacts/visuals-check`. These synthetic checks do not replace device listening checks.
