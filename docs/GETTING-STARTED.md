# Get started with Magic Music Wall

Applies to Magic Music Wall **1.0.2**.

[All guides](README.md) · [Download the latest release](https://github.com/digitalrcs/MagicMusicWall/releases/latest)

This walkthrough is for Windows 11 Intel/AMD PCs. Have one folder of your own music ready. Your music stays in its original location; the app saves an index and playlists.

## 1. Install the player

On the release page, expand **Assets** and download the file ending in **Setup-x64.exe**. Close an older running copy, run Setup, and open **Magic Music Wall** from the Start menu. The runtime and playback engine are included.

GitHub's **Source code (zip)** and green **Code** button are for developers. They are not the ready-to-run application. For a USB copy that runs without installation, follow the [portable instructions](WINDOWS-INSTALLER.md#run-the-portable-copy).

The installer is unsigned. Use the official release and follow your PC's security policy. [Installation and updating details](WINDOWS-INSTALLER.md).

## 2. Hear your first song

1. Connect your speakers or pair your Bluetooth stereo in Windows. Confirm another Windows app can play through it.
2. In Magic Music Wall, select that device from the audio-output list beside the bottom playback controls. Use **View options → Refresh audio outputs** if necessary.
3. Open **Manage folders**, click **Add folders…**, and choose your music folder. Wait for the scan to finish.
4. In **Folders**, double-click a folder to open it, then click a song's **+ Queue**. It should appear in **Up next**.
5. Press the bottom **Play** button and adjust the volume.

Opening a folder only browses it. **+ Queue** adds one song; **+ Queue folder** adds its indexed contents. Explicit Play actions can replace the queue. If there is no sound, start with [audio troubleshooting](TROUBLESHOOTING.md#no-sound-or-the-wrong-output).

![Folder grid with demonstration music](images/library.png)

## 3. Keep a playlist

Queue a few songs, click **Save queue** above **Up next**, and give the playlist a name such as **Evening favorites**. Later, click its name on the left and choose **Play playlist**. Clearing the queue does not erase the playlist. See [playlist instructions](USER-GUIDE.md#make-a-playlist) to add, remove or reorder songs.

## 4. Explore the constellation

Click **Music constellation**. On first use, it starts from the current music, selected queue/library item, selected/open folder, or first queued song. With no musical context, the alphabetical list is the fallback. Later visits restore your last exploration. Enable **Follow now playing** if you want the view to follow track changes.

Choose **Artist**, **Album** or **Song** under **Show**. Click a name to explore, then use **+ Queue … songs** to add what you want. Clicking a name alone does not start playback.

Start with **Metadata** or **Blended**. To improve the available information:

| Action | What it adds | Internet? |
| --- | --- | --- |
| **Rescan library & rebuild map** | New files and changed file tags from your added folders | Only if the files are on a network location |
| **MusicBrainz info → Lookup missing** | Available recording details, genres, community tags and instrument credits | Yes, during lookup |
| **Analyze sound** | Estimated sound features from local audio samples | No online service; files must be accessible |

These are separate actions. You do not have to repeat sound analysis when you download MusicBrainz information. Saved results are reused. For uncertain matches, select the song, use **Find match → Search selected song**, compare versions, then choose **Use selected match**. [Full constellation guide](MUSIC-MAP.md).

## 5. Optional: use your phone

1. Keep the Windows app open and the PC awake on your home network.
2. Open **Setup → Remote**, enable the remote, choose the reachable PC address, and set/confirm a remote passphrase. Click **Save settings**.
3. Use **Copy web address** and open it in the phone's browser, or expand **Advanced access links and QR codes** to show and scan a QR access link. Sign in when prompted.
4. Choose **Control PC** to operate the stereo's playback. Choose **Listen / watch here** for independent playback on the phone; enable browser playback in Setup and press Play in the browser.

The browser player is not a synchronized mirror of the desktop player's position. Keep passphrases and QR access links private. Follow the [web remote guide](WEB-REMOTE.md) for setup and connection help.

## After adding more music

For files added outside the app to an existing source, click **Rescan library**. For a new source, use **Manage folders → Add folders…**. Then use **Lookup missing** or **Analyze sound** if you want those extra map details. For automatic refresh while the app is open, enable **Setup → Library → Watch source folders for changes while the app is open**. Changes are grouped before rescanning; disconnected sources remain indexed.

Use **Setup → TV & data → Back up library…** to save a private ZIP containing the library, settings and listening session. Keep music, sound-analysis caches and AI notes backed up separately; they are not included. **Restore backup…** validates the ZIP, retains a recovery backup, then closes and restarts the player.

Your queue order, duplicate entries and current position are saved automatically. Reopening restores the session paused by default; enable automatic resume in **Setup → Sound** only if you want playback to begin on launch. **Undo** in Up next reverses the latest queue clear, removal or reorder. **Manage folders → Undo library removal** restores the last removed catalog entries and playlist membership until a later catalog edit; it does not restart playback or restore the PC queue.

[Common questions](FAQ.md) · [Troubleshooting](TROUBLESHOOTING.md) · [All guides](README.md)
