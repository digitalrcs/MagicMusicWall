# Troubleshooting

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
- Use **Add folder** or **Add files** for a new source. Use **Rescan library** after adding files to an existing source.
- Reconnect an unplugged USB drive or network share before rescanning.
- Check that Windows can open the path in File Explorer using your current account.
- A saved path on another PC's internal drive is not made available by moving the installer or portable app.

Missing media stays in the saved catalog/playlist until you explicitly remove its entries. Removing from the library keeps the file on disk; a later rescan can import it again.

For a portable copy, launch with **Start-MagicMusicWall.cmd** and move the whole app folder, including **Data** and any **Media** folder inside it. Portable mode adjusts paths inside that folder when its location or drive letter changes. Paths outside it stay unchanged; reconnect those sources or add their new locations.

## A playlist looks empty or will not play again

Click the playlist name on the left to browse its saved songs. Check its sidebar song count and clear any search you entered while inside that playlist.

An **Empty playlist** starts with no songs. To add songs, browse the library, select them, choose the destination under **Add songs to playlist** on the right, then click **Add selected to playlist**.

**Clear** above **Up next** only clears the playback queue. Click **Play playlist** to reload the saved playlist. **Queue playlist** appends it to the current queue. A single song's filename/**+ Queue** only adds that one song.

## The phone page will not open

1. Keep the Windows app open and the PC awake.
2. In **Setup → Remote**, confirm the remote is enabled and its status says it is running.
3. Try **Open remote page** on the PC. If that fails too, inspect the Setup status for a port/address error.
4. Confirm the phone and PC are on a reachable home network. Guest Wi-Fi/client isolation can block access.
5. Copy a fresh complete link or show a new QR code. The old one may be invalid after **Replace access link**, a changed PC address, or a move to another PC.
6. Check Windows Firewall's allowed private-network access for the app. Use the PC's normal security controls; do not turn the firewall off as a workaround.

The remote and file-sharing service need different ports. The default web remote port is `8787`; the file-sharing default `0` chooses an available port.

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

- Select at least one changed field, then review the save confirmation.
- Check the [supported formats](METADATA.md#formats-and-backups). MKV/WebM/AVI are read-only in this editor.
- Reconnect the drive and check that the file and folder are writable.
- Close other players or active browser streams that may hold the file open.
- Ensure enough free space for a full working copy and backup.
- A network share must support atomic replacement. The app does not directly overwrite the original when that safe replacement fails.
- If a selected tag does not round-trip correctly for that container, saving stops and keeps the original.

Successful writes display the original-file backup path. If the file was saved but the library refresh failed, preserve that backup, restore access to the library data folder, and rescan. See the [backup restoration steps](METADATA.md#formats-and-backups).

## Recover library data

Open **Setup → TV & data → Open data folder** to find the active data directory. Installed mode normally uses `%LOCALAPPDATA%\MagicMusicWall`; portable mode uses its adjacent **Data** folder.

Close the app before copying/restoring library data. Keep a copy of the current folder before restoring an older backup. `library.json` contains media references and playlists; `library.json.bak` is the previous library save. Your music is stored separately at its original paths.

If a library file cannot be read, the app preserves a uniquely named `library.corrupt.*.json` before recovering. An unreadable or newer incompatible library is not silently overwritten. Keep those files if you need help investigating the problem.

When asking for help, include the app version, Windows version, action you took, expected/actual behavior and the relevant status message. Remove private media paths, remote URLs/QR codes and tokens from public screenshots or logs.
