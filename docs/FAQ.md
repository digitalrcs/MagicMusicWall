# Common questions

Applies to Magic Music Wall **0.9.12**.

[All guides](README.md) · [First-use walkthrough](GETTING-STARTED.md)

## Do I need a subscription, Alexa or an AI model?

No. The Windows player uses your own media files. Alexa, Matter, ESP32 hardware, AgentChat and LM Studio are not required to use it. Optional movie metadata lookup requires your own TMDB token; MusicBrainz lookup does not require an API key.

## Does adding a folder copy or upload my songs?

No. It catalogs the files where they are. Online metadata lookup sends search text or provider identifiers, not audio files. Browser listening and TV file sharing do send the selected media to the receiving device. [Metadata and privacy](METADATA.md).

## What is the difference between a library, queue and playlist?

| Name | Meaning |
| --- | --- |
| Library | The files and folders you added to the app |
| Up next / queue | The playback order you are using now |
| Playlist | A saved named list of references to songs |

Clearing the queue does not delete playlists or media. Removing a playlist does not delete its songs. [Queue and playlist controls](USER-GUIDE.md).

## Will the constellation recommend artists I do not own?

No. It maps indexed music in your library, including entries whose drives are temporarily disconnected. Only accessible files can play. It is not the Music-Map/Gnod recommendation service. The network shows a selected item and nearby matches; search and paging reach the rest of the indexed collection.

## Can I search for mellow music, violin or acoustic guitar?

Constellation search uses saved names, genres, MusicBrainz tags and available instrument credits. Try those terms after lookup, but results depend on what contributors supplied. Missing credits do not mean an instrument is absent. Sound analysis estimates musical features; it does not reliably label instruments or moods. The main player search and constellation search have different fields. [Map search and evidence](MUSIC-MAP.md).

## Where are the downloaded information and sound calculations saved?

MusicBrainz information is saved per song in **library.json**. Sound features are in **music-audio-features.json**. Both are in the active app data folder shown in **Setup → TV & data**. They are separate from the original media tags. The separate **Edit file tags** workflow writes only reviewed, checked fields and creates an original-file backup. [Metadata guide](METADATA.md).

## Does the phone play exactly what the desktop is playing?

**Control PC** changes the desktop queue and playback. **Listen / watch here** plays independently in that browser after you press Play. It is not synchronized desktop audio/video mirroring. Browser and TV sharing use original files, so codec support matters. [Web remote](WEB-REMOTE.md).

## Will a Fire TV appear in the Chromecast list?

Do not assume it will. **Find / refresh Chromecasts** discovers compatible receivers. For Fire TV, use **Share file to Fire TV / VLC** and open the temporary URL in a compatible network player. [TV and video steps](USER-GUIDE.md#play-a-video-or-use-a-tv).

## Can I move my library to another PC?

The installer alone does not contain your library or music. Portable mode can carry its Data folder and media stored inside the portable folder. External drives and network shares still need reachable paths, and each PC needs its own audio connection. [USB and migration instructions](WINDOWS-INSTALLER.md).

## How do I update, read help offline or report a problem?

Close the app and run the newer installer at the same install location. Back up your active data folder first. Read offline help from the **Magic Music Wall documentation** Start menu shortcut or **OfflineDocs/index.html** in the package.

For help, read [Troubleshooting](TROUBLESHOOTING.md), then report the version, exact steps, expected/actual result and visible error in [GitHub Issues](https://github.com/digitalrcs/MagicMusicWall/issues). Do not attach private music, a complete library/settings file, access URLs, QR codes or tokens.
