# Upstream license texts

Retrieved on November 14, 2026. The four license text files are unmodified copies from the corresponding upstream Git revisions. Their Git blob hashes were verified after saving.

| Local file | Component / revision | Upstream source | Git blob SHA-1 |
| --- | --- | --- | --- |
| `LibVLCSharp-3.10.2-LICENSE.txt` | LibVLCSharp and LibVLCSharp.WPF 3.10.2; commit recorded in the NuGet package | [LICENSE](https://github.com/videolan/libvlcsharp/blob/3082c09ca955131c705fc4767fde03e9c4a14999/LICENSE) | `8000a6faacf471c537530805ab29523c7732e11a` |
| `TagLibSharp-2.3.0-COPYING.txt` | TagLibSharp 2.3.0; tag `TaglibSharp-2.3.0.0`, commit `b5ae84f2e84087bf160bb0471420200dd2b5d809` | [COPYING](https://github.com/mono/taglib-sharp/blob/b5ae84f2e84087bf160bb0471420200dd2b5d809/COPYING) | `8add30ad590a65db7e5914f5417eac39a64402a3` |
| `VLC-3.0.24-COPYING.txt` | VLC 3.0.24; tag `3.0.24`, commit `6de05adcbaf2e8b85fe86aad4169393098628119` | [COPYING](https://github.com/videolan/vlc/blob/6de05adcbaf2e8b85fe86aad4169393098628119/COPYING) | `d159169d1050894d3ea3b98e1c965c4058208fe1` |
| `VLC-3.0.24-COPYING.LIB.txt` | libVLC / VLC 3.0.24; same revision | [COPYING.LIB](https://github.com/videolan/vlc/blob/6de05adcbaf2e8b85fe86aad4169393098628119/COPYING.LIB) | `4362b49151d7b34ef83b3067a8f9c9f877d72a0e` |

The installed NuGet metadata declares `LGPL-2.1-or-later` for LibVLCSharp 3.10.2 and VideoLAN.LibVLC.Windows 3.0.24, and `LGPL-2.1-only` for TagLibSharp 2.3.0. VLC's upstream source includes both GPL and LGPL texts; its plugins and external libraries have their own license notices. These files record the upstream texts, not a license determination for every native binary in the package. See the version-specific [VLC dependency acknowledgements](https://github.com/videolan/vlc/blob/6de05adcbaf2e8b85fe86aad4169393098628119/THANKS) and [VideoLAN libVLC documentation](https://videolan.videolan.me/vlc/master/).

## NAudio 2.2.1

Added November 20, 2026: `NAudio-LICENSE.txt` is the MIT license from [NAudio v2.2.1](https://github.com/naudio/NAudio/blob/b5d5ff83fd378f046398891fe5cd99426ce44732/license.txt). The source companion includes this exact revision; its archive checksum is pinned in `scripts/Package-ThirdPartySources.ps1`.
