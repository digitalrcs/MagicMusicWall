# Changes

## 0.8.1

- Focus the constellation on the current music, selected queue/library item or indexed folder, with the first queued song as a fallback. Reopening refocuses the map; changing Artist/Album/Song view preserves the musical selection.
- Keep pending selection while the map builds, without changing playback or the queue.

## 0.8.0

- Add optional MusicBrainz library enrichment from the constellation's **MusicBrainz info** button, **MusicBrainz for this selection**, and **MusicBrainz library info…** in the song-information viewer.
- Look up missing information in a batch, review uncertain recordings with editable artist/title/optional album searches and **Use selected match**, refresh already-saved recording/release IDs, or remove saved information. Completed saves remain when a run is cancelled or paused by a provider error.
- Automatically save only a unique recording with matching artist/title, known durations within three seconds, and matching album when one is known. Ambiguous, partial or truncated results require review; a missing local album does not trigger an arbitrary album choice.
- Keep MusicBrainz details separately in the app's library without rewriting media-file tags. Saved genres, community labels and instrument/performance credits contribute to constellation search and Metadata/Blended connections; local Sound analysis is unchanged.
- Clear stale saved matches when a song's artist/title identity changes, or when its album changes and the match included a selected release. Recording-only matches survive an album change.
- Start online requests only through explicit lookup actions, share a one-request-per-second MusicBrainz limit across the app, and retain source attribution and data-license information. Music files and local paths are not uploaded.

## 0.7.1

- Replace the fixed constellation ring with an animated spring network: stronger similarity links pull music closer, and connections between neighbors reveal related groups.
- Show up to 18 neighbors around the selected music. Drag a name and release it to let the network settle; click without dragging to explore that selection.
- Add **Pause motion** and **Resume motion**. The map starts paused when Windows animation effects are disabled; the library list, related-item buttons and Back navigation also support keyboard exploration.
- Explain that connection strengths measure relevance, not probability, and that displayed distances are approximate because other links and label spacing also affect positions.
- Reuse existing metadata and cached sound analysis for the new layout. No new sound scan is needed to use it, and the audio-analysis algorithm is unchanged.

## 0.7.0

- Explore a Music constellation in a separate window: choose an artist, album or song and follow labeled connections through your indexed music collection.
- Search and page the full music index, filter by genre, explore an artist's albums or songs, and append a whole selection or individual songs to the queue.
- Choose Metadata, Sound or Blended similarity. Optional local analysis samples up to the first 60 seconds of each music file, caches unchanged results, reports progress and failures, and preserves completed work when cancelled.
- Explain metadata and sound relationships, show analyzed song coverage and estimated BPM, and label artist/album sound summaries as aggregates. No online recommendation database or AI service is used.
- Refresh the open constellation after library changes, and rescan configured folders with **Rescan library & rebuild map** when files change outside the app. Sound analysis remains a separate optional action.

## 0.6.0

- Sign in at the plain web remote address using a passphrase chosen in Setup. Remembered browsers stay signed in for 30 days, including across app restarts; QR access links remain available.
- Change or remove the passphrase to revoke remembered browser access. Replacing the access link also revokes old links and remembered browsers.
- Remove individual items from the PC or browser queue using touch-friendly Remove buttons. Saved playlists and media files are unchanged.
- Wrap long song and folder names in the web remote. Phone layouts place artist and album below each song name and keep actions easy to tap.

## 0.5.2

- Keep the selected volume when switching queued tracks, including Next, Previous and automatic advancement. The Windows audio output is recreated before volume and mute settings are applied.
- Retain intentional mute and the selected output during local track changes.

## 0.5.1

- Branded Windows 11 installer, portable distribution and USB transfer kit.
- Public documentation with screenshots, examples, separate user/developer paths and offline guides.
- Portable launcher/data support for carrying a separate library with the application, including updated paths for media inside a moved portable folder.
- Release checksums and packaging scripts.

## 0.5.0

- Reviewed file-tag editing with current/proposed values and individual field selection.
- MusicBrainz music suggestions and optional TMDB movie lookup configured in Setup.
- Verified staged tag writes with a retained original-file backup.
- Metadata entry points in the collection, queue and song-information window.
- Library refresh after tagging preserves item IDs and playlist references.

## 0.4.3

- Playlist creation can start from selected songs, the current queue, or an empty list.
- Opening saved playlists clears an earlier search that could hide their songs.
- Play/Queue playlist controls and replay after clearing the playback queue.
- Saved song counts and clearer playlist destination controls.

## 0.4.2

- Compact directory rows for the added library's folders and files on desktop and web.
- Folder navigation and individual-file queue/play actions.

## Earlier development

- Windows music/video playback, library indexing, search, playlists and output selection.
- DigitalRCS.com branding and persistent Setup preferences.
- Home-network web remote, QR links and independent browser listening/watching.
- Song information, lyrics and sourced song facts.
- Chromecast discovery and explicit single-file sharing for compatible network players.
