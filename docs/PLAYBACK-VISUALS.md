# Playback visuals (version 0.9.5)

## More room for playlists

Click **Library tools** at the bottom of the left sidebar. The tool buttons fold away vertically, giving the saved playlists more space. Click the header again to reopen them. The app remembers this setting.

## Switch from your collection to music visuals

1. Start playing a song through a PC audio output.
2. Above the folder/file list, select **Playing music · visuals**.
3. Choose a style from the dropdown:

| Style | What it shows |
| --- | --- |
| Spectrum | Frequency bars, bass on the left and treble on the right |
| Mirror bars | A spectrum reflected around the center |
| Waveform | The recent audio waveform |
| Orbit rings | Four rotating, frequency-reactive rings |
| Star voyage | A flight through stars whose size and speed respond to energy |
| Nebula | A swirling field of glowing particles |
| Mountain range | Layered frequency ridges |
| Light tunnel | A moving geometric tunnel with reactive rings |

Select **Music list** to return to your folders and files. Your browsing position, queue and playback remain in place. The app remembers your visual style; it starts on the music list.

## How it works

Visuals use Windows output loopback via [NAudio](https://github.com/naudio/NAudio/blob/v2.2.1/Docs/WasapiLoopbackCapture.md). The app reads samples from the selected PC audio output, including Bluetooth. It does not open a microphone, save audio, send it online, or replace the playback engine. Other apps playing through the same output can also affect the visuals.

Capture stops when playback pauses, you return to the list, the window is minimized, or the app closes. Selecting another output restarts capture on that device. If capture fails, music can still play; use **Reconnect visuals** to retry. Muted or silent output produces little or no spectrum activity. Windows output volume and device behavior affect the signal.

Casting and browser-only playback happen on other devices and cannot feed these desktop visuals. Choose a PC audio output to use them. These are desktop visuals; they are not streamed to the web remote.
