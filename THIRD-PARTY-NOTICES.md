# Third party components

Magic Music Wall uses the following separately licensed components. The application UI and integration code were written for this project; inspiration is not copied implementation.

| Component | Version | License / upstream source |
| --- | --- | --- |
| LibVLCSharp and LibVLCSharp.WPF | 3.10.2 | LGPL-2.1-or-later, https://github.com/videolan/libvlcsharp |
| VideoLAN.LibVLC.Windows | 3.0.24 | VLC/libVLC and plugins retain their individual licenses; https://www.videolan.org/legal.html ; https://code.videolan.org/videolan/vlc |
| TagLibSharp | 2.3.0 | LGPL-2.1-only, https://github.com/mono/taglib-sharp |
| QRCoder | 1.8.1 | MIT, [upstream source](https://github.com/Shane32/QRCoder); bundled text: `licenses/QRCoder-LICENSE.txt` |
| NAudio.Wasapi and NAudio.Core | 2.2.1 | MIT, [upstream source](https://github.com/naudio/NAudio/tree/v2.2.1); bundled text: `licenses/NAudio-LICENSE.txt` |
| Microsoft .NET and ASP.NET Core runtime | 10.0 | MIT and bundled third-party notices, https://github.com/dotnet/runtime and https://github.com/dotnet/aspnetcore |

The Windows distributions keep managed/native dependencies separate so they can be replaced under their licenses. Preserve all dependency license files and notices included in the published directory. The release includes a companion archive of upstream source snapshots, with exact revisions, hashes and known native dependency inventory limits recorded in [Third-party source provenance](licenses/REDISTRIBUTION.md). The installer and portable packages also include the exact .NET, ASP.NET Core and Windows Desktop runtime notices used by the build. The project's copyright-reserved notice does not replace any third-party license.

## Optional online information

Franky's vector character is adapted from DigitalRCS's MIT-licensed Franky ESP32 project (`scripts/generate_frog.py`, revision `49ebf8f5154b56816bfa25a2c88956f1c9f4cd05`). Its colors and face geometry are reused with new sound-reactive animation. See `licenses/Franky-Art-LICENSE.txt`; the original project is https://github.com/digitalrcs/DigitalRCS-Local-LLM-Chatbot.

These services are queried on demand; their content is separate from the application's software licenses. The package does not bundle a lyrics collection or Wikipedia articles.

| Provider | Use and content notice | Official references |
| --- | --- | --- |
| MusicBrainz / MetaBrainz contributors | Candidate recordings, artists, releases and album names. MusicBrainz core data is CC0; supplementary data has separate CC BY-NC-SA 3.0 terms. Results include source links; saved album choices retain their source reference. | [API](https://musicbrainz.org/doc/MusicBrainz_API), [data licensing](https://musicbrainz.org/doc/About/Data_License) |
| TMDB | Optional movie title, release date, genres and overview lookup using the user's API Read Access Token. This product uses the TMDB API but is not endorsed or certified by TMDB. Provider attribution and approved logo appear in Setup > Metadata > Provider credits. | [API and attribution requirements](https://developer.themoviedb.org/docs/faq), [TMDB](https://www.themoviedb.org/) |
| LRCLIB | Optional lyrics lookup. Results identify their source and matched recording. Access to the API does not make lyrics part of this application's software license; lyrics retain their respective rights. | [API documentation](https://lrclib.net/docs) |
| Wikipedia contributors | Selected English Wikipedia introductory excerpts, with article title/link and contributor credit. Displayed text is shortened and whitespace normalized; the viewer includes CC BY-SA 4.0 attribution and its license URL. | [TextExtracts API](https://www.mediawiki.org/wiki/Extension:TextExtracts), [Wikimedia terms](https://foundation.wikimedia.org/wiki/Policy:Terms_of_Use/en), [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |

Online requests identify Magic Music Wall and DigitalRCS.com in the User-Agent and handle provider rate limits. Searches transmit artist/title text; the current lyrics implementation compares album and duration locally against returned candidates. No audio or media files are uploaded. Locally embedded or adjacent-file lyrics are read from the user's collection and retain their existing rights.

QRCoder generates the remote access QR image entirely on the PC. The full access URL, including its key, is not sent to an external QR-generation service.

The TMDB logo is preserved as `tmdb-logo-original.svg` from the [asset embedded by the official developer FAQ](https://files.readme.io/29c6fee-blue_short.svg). `Assets/TmdbLogo.xaml` uses the same wordmark geometry and gradient for native WPF rendering; the mark is not recolored or redrawn. TMDB retains its trademark rights.
