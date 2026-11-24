# Troubleshooting

Applies to Magic Music Wall **1.0.1**.

[Documentation home](README.md)

Start with the visible status message in the player or Setup. It usually identifies whether the problem is a missing file, disconnected output, network address, lookup provider or tag-save failure.

## No sound or the wrong output

1. Confirm that Windows itself can play through the intended speakers/headphones. Use another app with the same device.
2. In Magic Music Wall, refresh audio outputs and choose the device under **Listening on**.
3. Check the app's volume and Windows' per-app volume mixer, device volume and master mute.
4. If the app reports a disconnected or unexpected output, reconnect the device, refresh, and press Play. It does not automatically resume after reconnection.
5. If another app also fails through the same output, fix the Windows/device connection before changing the music library.

The player checks VLC's reported route and pauses when it detects an unexpected change. This cannot guarantee that Windows emits no sound before its route-change notification arrives.

## Bluetooth is choppy

Compare another application using the same Bluetooth connection, with any USB audio cable disconnected. A charging cable can also expose a separate USB audio device; a good USB test does not prove that Bluetooth is working.

If multiple apps cut out, check the headset's charge, distance, nearby interference, other simultaneous connections and the PC manufacturer's Bluetooth driver. On a desktop, check the manual to confirm that any antennas required by its Bluetooth adapter are attached. Different wireless adapters may use different antenna connectors.

A microphone/call profile can affect Bluetooth audio, but do not disable it as a routine fix if you need the headset microphone. Use Windows and the device manufacturer's guidance for your hardware. The app cannot repair a weak radio signal or a driver fault.

## A file or folder is missing

- The collection is a catalog of the folders/files you added. It is not a file browser for every drive.
- Use **Manage folders → Add folders…** or **Add files…** for a new source. Use **Rescan library** after adding files to an existing source.
- Reconnect an unplugged USB drive or network share before rescanning.
- Check that Windows can open the path in File Explorer using your current account.
- A saved path on another PC's internal drive is not made available by moving the installer or portable app.

Missing media stays in the saved catalog/playlist until you explicitly remove its entries. Removing from the library keeps the file on disk; a later rescan can import it again.

For a portable copy, launch with **Start-MagicMusicWall.cmd** and move the whole app folder, including **Data** and any **Media** folder inside it. Portable mode adjusts paths inside that folder when its location or drive letter changes. Paths outside it stay unchanged; reconnect those sources or add their new locations.

## A playlist looks empty or will not play again

Click the playlist name on the left to browse its saved songs. Check its sidebar song count and clear any search you entered while inside that playlist.

An **Empty playlist** starts with no songs. To add songs, browse the library, select them, choose the destination under **Add songs to playlist** on the right, then click **Add selected to playlist**.

**Clear queue** above **Up next** only clears the playback queue. Click **Play playlist** to reload the saved playlist. **Queue playlist** appends it to the current queue. A song's **+ Queue** button adds that one song. A single click selects a row; double-click plays it without replacing the existing queue.

## The phone page will not open

1. Keep the Windows app open and the PC awake.
2. In **Setup → Remote**, confirm the remote is enabled and its status says it is running.
3. Try **Open sign-in page** or **Open remote page** on the PC. If that fails too, inspect the Setup status for a port/address error.
4. Confirm the phone and PC are on a reachable home network. Guest Wi-Fi/client isolation can block access.
5. Use **Copy web address** for the current plain URL, then sign in. A changed PC address or a move to another PC can make an old bookmark unreachable. For quick access, show a fresh QR code; old QR links stop working after **Replace access link**.
6. Check Windows Firewall's allowed private-network access for the app. Use the PC's normal security controls; do not turn the firewall off as a workaround.

The remote and file-sharing service need different ports. The default web remote port is `8787`; the file-sharing default `0` chooses an available port.

## The web page opens but asks me to sign in

Enter the remote passphrase you chose in **Setup → Remote**, then select **Connect to your music**. The plain URL works without an access key in it. If the page says no passphrase has been set, set and confirm one in Setup on the Windows PC, click **Save settings**, and choose **Check again** on the web page. An existing QR access link can also connect.

If you forgot the passphrase, set a new one in the Windows app; you do not need the old one. Changing/removing the passphrase or replacing the access link revokes remembered browser access. **Remember this browser for 30 days** lasts across normal app restarts, but clearing cookies, changing browser, or using a private window requires another sign-in. Without that option, sign-in lasts for the browser session, up to eight hours.

If the page reports too many sign-in attempts, wait one minute before trying again. Passphrases are case-sensitive. Use a passphrase unique to this app because the home-network page uses HTTP.

## Browser audio/video does not start

Tap Play once to satisfy the browser's autoplay rules. Check that **Allow music and video playback in web browsers** is enabled and saved.

The browser receives the original file; there is no transcoding. Its codec/container support may differ from VLC on the PC. Try another file, a compatible browser/player, or **Control PC**. **Play listed tracks** uses only displayed results and does not automatically collect nested-folder contents.

## A TV device disappears or is unavailable

Choose **Find / refresh Chromecasts** after waking the receiver. Previously discovered devices remain listed for the app session and may be marked unavailable. Check that both devices can reach each other and that multicast discovery is permitted on the local network.

Choose **This PC** explicitly to return to local playback. A Fire TV is not assumed to be a Chromecast; use **Share file to Fire TV / VLC** with a compatible network player. Playback still depends on that player's codec support.

## Online information is missing or wrong

Try a clean title and artist without a track prefix or filename extension. Compare release names and years; compilations, remasters and live versions can have different metadata.

Providers may have no match, be offline, or ask you to wait because of rate limits. Follow the displayed message and retry later. Local playback works without online lookup. Movie lookup requires the long **TMDB API Read Access Token** saved under **Setup → Metadata**. A token encrypted on another Windows account may need to be entered again.

## Tags will not save

For missing or uncertain MusicBrainz information in the constellation, use the separate steps below; library enrichment does not write file tags.

- Select at least one changed field, then review the save confirmation.
- Check the [supported formats](METADATA.md#formats-and-backups). MKV/WebM/AVI are read-only in this editor.
- Reconnect the drive and check that the file and folder are writable.
- Close other players or active browser streams that may hold the file open.
- Ensure enough free space for a full working copy and backup.
- A network share must support atomic replacement. The app does not directly overwrite the original when that safe replacement fails.
- If a selected tag does not round-trip correctly for that container, saving stops and keeps the original.

Successful writes display the original-file backup path. If the file was saved but the library refresh failed, preserve that backup, restore access to the library data folder, and rescan. See the [backup restoration steps](METADATA.md#formats-and-backups).

## The constellation selects the wrong music or shows few connections

The constellation restores your last exploration. Enable **Follow now playing** to follow track changes; leave it off to explore independently. **More like this** adds related tracks without replacing the queue.

Clear map search/genre filters and choose **All music** if results seem missing. Use **Metadata** or **Blended** before sound analysis; **Sound** needs analyzed audio on both sides of a connection. Check the coverage line. Missing tags and incomplete MusicBrainz coverage can leave few relationships. A rebuild does not automatically fetch online information or analyze sound. See [the three separate scan actions](GETTING-STARTED.md).

## MusicBrainz says Needs review or stops with a busy message

**Needs review** is not a failed save. Automatic matching requires a unique recording, matching artist/title, known duration within three seconds, and matching album when available. Incomplete search results and alternate versions also require review.

Select the row, open **Find match**, check the artist/title and optional album, then choose **Search selected song**. Clear an incorrect album filter if needed. Compare versions before **Use selected match**. View stored information on **Saved info**; **Remove saved info** clears the app's enrichment without editing the media file.

If MusicBrainz reports busy or rate limited, wait for the displayed interval and retry later. Requests are paced, but the provider can still be temporarily unavailable. Completed saves remain; **Lookup missing** skips saved songs and **Refresh saved** updates previously chosen IDs. No playback or full library rescan is needed to retry. [Matching and privacy details](METADATA.md#save-musicbrainz-information-in-your-library).

## Recover library data

Open **Setup → TV & data → Open data folder** to find the active data directory. Installed mode normally uses `%LOCALAPPDATA%\MagicMusicWall`; portable mode uses its adjacent **Data** folder.

Close the app before copying/restoring library data. Keep a copy of the current folder before restoring an older backup. `library.json` contains media references and playlists; `library.json.bak` is the previous library save. Your music is stored separately at its original paths.

If a library file cannot be read, the app preserves a uniquely named `library.corrupt.*.json` before recovering. An unreadable or newer incompatible library is not silently overwritten. Keep those files if you need help investigating the problem.

When asking for help, include the app version, Windows version, action you took, expected/actual behavior and the relevant status message. Remove private media paths, remote URLs/QR codes and tokens from public screenshots or logs.

## Visuals feel weak or Franky holds his mouth open

Select **Playing music · visuals** and raise the vertical **Visual power** slider beside the animation. It ranges from 0.5× to 3.0× and defaults to 1.6×. It affects animation only; adjust playback loudness with the separate Volume control. Franky follows changes in relative loudness and closes quickly between beats. Constant audio can still hold a steady mouth shape; this is not lyric lip-sync. If nothing moves, confirm local PC playback is active, check the live-output label and try **Reconnect visuals**. Casting and browser-only audio do not feed these desktop visuals.

## Shuffle still plays in order

Turn on **Shuffle**, then press **Next** or let the current song finish. The player chooses randomly from remaining entries and moves the chosen entry into the next visible queue position. It can occasionally pick the entry that was already next. If only one unplayed entry remains, it must play that entry. Enable **Repeat queue** for another pass; disabling Shuffle resumes the visible queue order. Desktop Next, automatic advancement and web-remote Control PC Next use the same selection logic. Browser-only playback uses its own queue.

## Restoring a listening session

In **Setup → Sound**, enable **Restore my queue and position**. The app checkpoints the exact queue, including repeated songs, and restores paused. Automatic resume is a separate option. Missing catalog entries are skipped; unavailable drives keep their indexed entries. **Undo** restores the most recent clear/remove/reorder without deleting files.

## Volume differs between tracks

Enable ReplayGain in **Setup → Sound**, save, and restart the app. It uses existing track gain tags with peak protection; it does not analyze or write tags. Untagged files keep their existing level. Gapless playback and crossfade are not guaranteed.

## A guest cannot change playback

Guest links intentionally allow browsing and appending to the PC queue only. The owner passphrase or owner access link is required for transport, output changes, removals and browser streaming. Revoke the guest link in Setup and save to stop its access.
