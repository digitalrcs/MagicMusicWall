# Find information and edit file tags

[Documentation home](README.md)

Metadata means the information stored with a media file, such as its title, artist and album. Magic Music Wall can read those tags, look up possible matches online, and write selected changes after you review them.

## Find information without changing a file

Select a song and open **Song info / lyrics** from the toolbar or right-click menu. **Song info** beside the player controls opens the current song. Correct the lookup artist/title if necessary; changing those search fields does not edit the file.

| Viewer | What to do |
| --- | --- |
| **Info & albums** | Click **Look up song & albums**, inspect MusicBrainz results, and open a source link when needed. |
| **Lyrics** | Read local lyrics first; use **Find lyrics online** for LRCLIB suggestions. Compare album and duration when multiple versions appear. |
| **Song facts** | Click **Find song facts**, choose the matching Wikipedia article, then **Read selected article**. Follow its source link for the full article. |

**Use selected album** in the information viewer saves an album override in this app's library only. It survives rescans and does not edit the media file. **Restore album from file tag** removes that override. If another program changed the file's tags, rescan first to refresh the indexed values.

Local lyrics can be embedded in the file, or in a `.lrc` / `.txt` file beside it with the same base name. For example, `First Light.flac` can have `First Light.lrc`. The lyrics viewer displays text; it does not highlight lines in time with playback. Missing lyrics or article matches are reported rather than invented.

## Write artist, album and other tags

1. Select one song or video and click **Edit file tags**. This is also on the collection/queue right-click menus and inside the song-information window.
2. Click **Search online** using a clean artist/title. Remove file extensions, track-number prefixes or release-group text from the search if it prevents a match.
3. Select the correct recording/release and click **Preview selected**.
4. Compare **Current file tag** with **Proposed value**. Edit values if needed and check only the fields you want to write.
5. Click **Save checked tags**. Read the filename and before/after changes in the confirmation, then choose **Save these changes**.

![Metadata preview with individually selected fields](images/metadata.png)

Online matches are suggestions. A song can appear on an original album, a compilation and several remasters with different years. Choose the release that matches your copy. You can leave an uncertain field unchecked.

MusicBrainz suggests title, artist, album and release year where available. You can also manually edit album artist, genre, track, disc and comment. A checked empty text value clears that field; a checked numeric value of `0` clears year, track or disc. Unchecked fields stay unchanged.

### Example: correct an album

Suppose the file is `D:\Music\The Example Quartet\Evening Sessions\01 - First Light.flac`, but its album tag is empty. Find the matching release, preview it, and check **Album** only. Keep **Title**, **Artist** and **Year** unchecked if their existing values are already correct. The confirmation then lists just the Album change.

Writing an Album tag also clears that file's earlier library-only album override, so the app shows the newly written tag. File IDs and playlist references are preserved.

## Movie lookup

Movie lookup uses TMDB and needs your own **API Read Access Token**:

1. Open **Setup → Metadata**.
2. Click **Open TMDB API settings** and obtain a token through your TMDB account.
3. Paste the long **API Read Access Token**, not the shorter API key, into the app.
4. Click **Save settings**, then open a video's **Edit file tags** window and search its title.

![Optional movie lookup configuration](images/setup-metadata.png)

The token is encrypted for your Windows account. Leave the box blank to keep an existing token; check the removal option to clear it on save. A portable copy moved to a different Windows account may require entering the token again.

Movie suggestions contain title, release year, genre names and an overview. The overview is saved in the **Comment / overview** field. This version searches movies; it does not match individual TV episodes. Movie lookup remains unavailable until a valid token is configured.

## Formats and backups

| Format | File-tag writing in this editor |
| --- | --- |
| MP3, FLAC, M4A | Enabled; the actual file and selected tags are checked before replacement. |
| MP4, M4V | Enabled for supported containers/tags. |
| OGG, OPUS, WMA, WMV, WAV, AIF, AIFF | Enabled, subject to successful tag verification. |
| MKV, WebM, AVI and other formats | Lookup/read-only here. Playback may still work through VLC. |

After confirmation, playback of that file stops if necessary. The app edits a working copy, verifies the tags, and replaces the original only after those checks pass. It does not rename files or transcode the media.

Every successful save leaves a full backup beside the file:

```text
First Light.flac
First Light.flac.mmw-backup-<unique-id>.bak
```

Keep enough space for a working copy and backup, especially for large movies. Backups remain until you manage them yourself and are not imported as music. The writer also checks preservation of existing common tags, lyrics, artwork bytes and track/disc totals. The backup is still your recovery copy.

To restore manually, close Magic Music Wall and other players, preserve the edited file elsewhere, then rename the chosen backup to the original filename. Reopen the app and rescan. Check that you have selected the correct backup before changing names.

A locked, read-only, missing or unsupported file cannot be saved. Some network shares do not support the required atomic file replacement. The app reports the failure rather than falling back to a direct overwrite; see [tag-save troubleshooting](TROUBLESHOOTING.md#tags-will-not-save).

## Online sources and privacy

Online searches happen only when requested. They send the artist/title text to the selected provider; media files and local paths are not uploaded. These are source-based lookups, not AI-generated facts. Provider coverage, availability and response times vary.

| Provider | Use |
| --- | --- |
| [MusicBrainz](https://musicbrainz.org/doc/MusicBrainz_API) | Music recordings, artist/album names and release dates. No key is required. |
| [TMDB](https://developer.themoviedb.org/docs/authentication-application) | Optional movie metadata with your token. |
| [LRCLIB](https://lrclib.net/docs) | Optional lyric matches. |
| [Wikipedia / MediaWiki](https://www.mediawiki.org/wiki/Extension:TextExtracts) | Article excerpts with source/contributor attribution. |

The phone/web viewers are read-only. File-tag editing is performed in the Windows app; its updated library appears in the remote afterward. No background or bulk tag rewriting is performed.

[Third-party notices](../THIRD-PARTY-NOTICES.md) describe provider content and component licenses. This product uses the TMDB API but is not endorsed or certified by TMDB.
