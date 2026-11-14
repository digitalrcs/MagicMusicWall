# Control the PC or listen in a browser

[Documentation home](README.md)

The web remote is a page served by Magic Music Wall while the Windows app is open. It works on reachable devices on your trusted home network. There is no cloud server or router-forwarding setup.

## Connect a phone or another PC

1. Open **Setup → Remote** in the Windows app.
2. Enable **Run the web remote whenever this app is open**.
3. Choose the PC's home-network address, or **Automatic**. The default web port is `8787`.
4. Click **Save settings** and confirm that Setup reports the remote is running.
5. Click **Show QR code** and scan it with your phone's camera. Alternatively, use **Copy remote link** and open that complete link in the other device's browser.

![Web remote with collection and playback controls](images/web-remote.png)

The other device must be able to reach the PC. A guest Wi-Fi network may isolate devices even when both have internet access. If Windows asks about network access, allow only the private network you intend to use.

Leave the app open, keep the PC awake, and keep the media drives connected. Closing the app stops the remote. When enabled in Setup, the remote starts on later app launches.

## Choose where playback happens

| Mode | Sound/video plays on | Queue used |
| --- | --- | --- |
| **Control PC** | The Windows app's selected output, such as its Bluetooth stereo | The PC's **Up next** queue |
| **Listen/watch here** | This phone, tablet or browser | That browser's independent queue |

In **Control PC**, selecting one file queues only that file. Use its separate Play action for immediate playback. **Play folder** includes subfolders; **Play collection** plays the full current library collection.

In **Listen/watch here**, use the browser's audio/video controls. **Play listed tracks** plays only the displayed results, up to 500. It does not recursively collect songs from folders below the one you opened. Browser listening does not restart or control the PC's playback.

Use **Copy listening link** or **Listening QR code** in Setup to open the page with browser playback selected. The browser may still require a tap before it can start sound or video.

## Browse and view song information

The remote has **Folders / All songs**, **All folders** and **↑ Up** controls. Opening a folder only navigates. Collection searches cover all its library folders; a selected playlist keeps its own search scope. Narrow searches when more than 500 results match.

Tap **Song info** for the current track, or **Info** beside a library or queue song, to read saved details and local lyrics. The dialog also offers online albums, lyrics and song facts. These viewers do not write media tags; use the Windows app for that.

## Understand browser format support

Browser listening sends the original file with seek support; it does not convert it. A file that plays on the PC through VLC may contain a codec that your browser cannot play. Try a known-compatible file or use **Control PC** with the PC's player. TV file sharing has the same receiver-codec limitation.

Turn off **Allow music and video playback in web browsers** and save if you want the page to control the PC only.

## Keep access links private

Treat the full remote/listening link and its QR code like a key. Anyone with that link who can reach the PC can control playback and, when enabled, stream library files. Do not include live links or QR codes in public screenshots, issue reports or documentation.

QR codes are generated on the PC; no external QR service receives the link. The browser removes the access key from its address bar after opening and retains it for that tab. Copy the complete link from Setup when connecting another device.

**Replace access link**, followed by **Save settings**, invalidates previous remote links and QR codes. Show/copy a fresh one afterward.

Browser streams use separate temporary links for individual files. Anyone holding a stream link with network access can read that file while the link remains valid. Stopping/restarting the remote or disabling browser playback and saving invalidates these links, though a browser may finish bytes already buffered.

The remote uses local HTTP and is intended for a trusted LAN. The app does not configure HTTPS, expose arbitrary disk browsing, change your router, or provide internet hosting.

If the page will not connect, see [remote troubleshooting](TROUBLESHOOTING.md#the-phone-page-will-not-open).
