<div align="center">
  <img src="docs/images/digitalrcs-logo.png" alt="DigitalRCS.com logo" width="96">
  <h1>Magic Music Wall</h1>
  <p>Your music. Your folders. Your home stereo.</p>
  <p>A Windows music and video library from <a href="https://digitalrcs.com">DigitalRCS.com</a>.</p>
</div>

Add the music and videos you already own, browse a compact folder/file grid, build playlists, and play through your PC speakers or connected Bluetooth stereo. Open the included web page on a phone to control the PC—or listen and watch directly in that browser.

![Magic Music Wall library with demonstration folders](docs/images/library.png)

## Download version 0.6.0

For **Windows 11 on Intel/AMD x64 PCs**. The .NET runtime and VLC playback engine are included; no separate runtime installation is needed.

| Package | What to do |
| --- | --- |
| [Windows installer](https://github.com/digitalrcs/MagicMusicWall/releases/download/v0.6.0/MagicMusicWall-0.6.0-Setup-x64.exe) | Run it on this PC, or copy it to USB and install on another PC. |
| [Portable ZIP](https://github.com/digitalrcs/MagicMusicWall/releases/download/v0.6.0/MagicMusicWall-0.6.0-Portable-x64.zip) | Extract the whole folder, then open **Start-MagicMusicWall.cmd**. |
| [USB kit](https://github.com/digitalrcs/MagicMusicWall/releases/download/v0.6.0/MagicMusicWall-0.6.0-USB-Kit.zip) | Installer, portable ZIP, offline guides and checksums together. |
| [Checksums](https://github.com/digitalrcs/MagicMusicWall/releases/download/v0.6.0/SHA256SUMS.txt) | Verify the downloaded files. |

[All releases](https://github.com/digitalrcs/MagicMusicWall/releases) · [Install and USB instructions](docs/WINDOWS-INSTALLER.md) · [Full documentation](docs/README.md)

The installer is unsigned. Clean Windows installation and ARM emulation have not been verified. No music, movies, personal library data or API tokens are bundled.

## Start with your music

1. Connect your stereo or headphones in Windows, then choose the device under **Listening on**.
2. Click **Add folder** or **Add files**. You can add local storage, a connected USB drive, or an accessible network share.
3. Open a folder to browse its contents. Click a song name or **+ Queue** to add just that song to **Up next**.
4. Press Play. Save the queue as a playlist, or select songs and add them to Dinner, Jazz, Rock, 80's, or your own playlist.

The directory grid shows the folders and files you added to your library. Search across them by title, artist, album, genre or folder name—for example, `Pink Floyd` or `Dark Side of the Moon`. Clearing the queue leaves saved playlists intact.

![Saved playlist and playback queue](docs/images/playlists.png)

## What you can do

- **Play your own collection:** music and video playback, queue controls, saved playlists, shuffle and repeat.
- **Choose an output:** PC audio or a Windows-connected Bluetooth device, plus Chromecast discovery and VLC casting.
- **Use your phone:** sign in with a passphrase at the plain web address and remember that browser, or scan a QR access link. Read wrapped song names, remove queued items, and listen/watch independently in the browser.
- **Find song details:** MusicBrainz recordings and albums, local or LRCLIB lyrics, and attributed Wikipedia facts.
- **Review file tags:** compare current/proposed title, artist, album and more, then save checked fields with an original-file backup. Optional TMDB movie lookup uses your own token.
- **Share a movie:** give a compatible Fire TV/VLC network player a temporary link to one selected file.
- **Carry the app:** portable mode keeps preferences and playlists beside the app; media inside that folder follows it when the USB drive letter changes.

Online lookups are optional and send search text to the selected provider. Media files are not uploaded. Playback works offline when the media is locally available. Alexa, Matter, an ESP32, and an AI service are not required.

![Review metadata before writing file tags](docs/images/metadata.png)

## A remote for your home

In **Setup → Remote**, enable the remote and choose the reachable PC address. Set and confirm a remote passphrase of 8–128 characters, then save. Use **Copy web address** to open the plain address on another device and sign in; **Remember this browser for 30 days** avoids re-entering the passphrase on later visits. Existing QR access links also work.

**Control PC** operates the Windows queue; **Listen / watch here** plays an independent stream in that browser after you press Play. Each queued item has **Remove**, and long song/folder names wrap onto multiple lines. On phones, artist and album appear below the song name.

![Web remote using demonstration music](docs/images/web-remote.png)

Keep the PC, application and source drives available. Share the passphrase or quick access links only with people you intend to give access. Choose a passphrase unique to this app: the remote uses HTTP on a trusted LAN and is not an internet hosting service. Browser listening and TV sharing send original files, so the receiving device must support their codecs. See the [web remote guide](docs/WEB-REMOTE.md) and [video instructions](docs/USER-GUIDE.md).

## Documentation

| Topic | Guide |
| --- | --- |
| Installing, USB, updating and uninstalling | [Windows installer and USB](docs/WINDOWS-INSTALLER.md) |
| Folders, searching, queue, playlists and video | [User guide](docs/USER-GUIDE.md) |
| Album lookup, lyrics, facts and file tags | [Metadata guide](docs/METADATA.md) |
| Phone control and browser listening | [Web remote](docs/WEB-REMOTE.md) |
| Audio, missing files, network and data recovery | [Troubleshooting](docs/TROUBLESHOOTING.md) |
| Building and packaging the application | [Developer guide](docs/BUILDING.md) |
| Release history | [Changelog](CHANGELOG.md) |

The same guides and screenshots are included in **OfflineDocs/index.html** in the Windows packages. Screenshots use synthetic demonstration content.

## Source and feedback

The application uses .NET 10, WPF, LibVLCSharp, TagLibSharp and QRCoder. The [developer guide](docs/BUILDING.md) separates developer setup from the ready-to-run downloads. Report reproducible problems in [GitHub Issues](https://github.com/digitalrcs/MagicMusicWall/issues); remove access links, tokens and personal paths from reports.

**Copyright © 2026 DigitalRCS.com. All rights reserved.** This repository is public with copyright reserved; no open-source license is granted for the project's source, documentation or branding. See [LICENSE.txt](LICENSE.txt) and [CONTRIBUTING.md](CONTRIBUTING.md). Third-party components retain their own licenses and notices: [third-party notices](THIRD-PARTY-NOTICES.md) and [source provenance](licenses/REDISTRIBUTION.md).
