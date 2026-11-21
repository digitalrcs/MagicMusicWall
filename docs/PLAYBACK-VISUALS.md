# Playback visuals (version 0.9.12)

Applies to Magic Music Wall **0.9.12**.

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
| Fine spectrum | 192 thin bars covering distinct frequency ranges; the original 64-bar Spectrum stays unchanged |
| Franky sings | Franky the frog opens his mouth, bobs and releases musical notes in response to audio energy |

**Mountain range** responds with taller peaks and a faster fall between beats. Franky's mouth follows energy in the 300–3,400 Hz vocal-presence range, while his notes and bounce respond to the full mix. This is a frequency filter, not vocal isolation or lyric lip-sync: instruments in that range can still make him sing. Silence closes his mouth and fades the notes.

Franky's vector character is adapted from DigitalRCS's original [ESP32 Franky artwork](https://github.com/digitalrcs/DigitalRCS-Local-LLM-Chatbot/blob/49ebf8f5154b56816bfa25a2c88956f1c9f4cd05/scripts/generate_frog.py), preserving his colors and face shapes with a new animated singing mouth. The original MIT license is included as `licenses/Franky-Art-LICENSE.txt`.

Select **Music list** to return to your folders and files. Your browsing position, queue and playback remain in place. The app remembers your visual style; it starts on the music list.

### Visual power

The slider fills the panel height with a larger handle. Arrow keys and the **+ / −** buttons adjust by **0.01**; clicking the track adjusts by **0.05**. The value shows two decimal places for fine tuning.

Use the vertical **Visual power** slider to the right of the animation. Slide upward for bigger movement and a stronger response to quiet passages; slide downward for a gentler display. Its range is **0.5×–3.0×**, with a **1.6×** default. It applies to all ten styles and is saved between sessions. It changes animation intensity only, never playback volume. Franky also opens and closes his mouth faster, following relative loudness rather than holding the same pose throughout louder music.

## How it works

Visuals use Windows output loopback via [NAudio](https://github.com/naudio/NAudio/blob/v2.2.1/Docs/WasapiLoopbackCapture.md). The app reads samples from the selected PC audio output, including Bluetooth. It does not open a microphone, save audio, send it online, or replace the playback engine. Other apps playing through the same output can also affect the visuals.

Capture stops when playback pauses, you return to the list, the window is minimized, or the app closes. Selecting another output restarts capture on that device. If capture fails, music can still play; use **Reconnect visuals** to retry. Muted or silent output produces little or no spectrum activity. Windows output volume and device behavior affect the signal.

Casting and browser-only playback happen on other devices and cannot feed these desktop visuals. Choose a PC audio output to use them. These are desktop visuals; they are not streamed to the web remote.
