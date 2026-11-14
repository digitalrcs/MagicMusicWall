# Third-party source provenance

Magic Music Wall distributes separately replaceable managed assemblies and native VLC libraries. Their upstream licenses and notices remain applicable. This document records technical provenance; it does not grant a new license, promise a source offer, or determine the licensing of every native binary.

`scripts/Copy-RuntimeNotices.ps1` copies the license and third-party notice files from the exact Microsoft runtime pack versions identified by the published application's runtime/dependency manifests. The generated `licenses/runtime-notices.json` records the package versions and file hashes without recording build-machine paths.

`scripts/Package-ThirdPartySources.ps1` creates a companion archive containing these upstream source snapshots and the repository's corresponding license texts:

| Component | Source revision |
| --- | --- |
| LibVLCSharp and LibVLCSharp.WPF 3.10.2 | `3082c09ca955131c705fc4767fde03e9c4a14999`, recorded by the NuGet package |
| TagLibSharp 2.3.0 | `b5ae84f2e84087bf160bb0471420200dd2b5d809`, tag `TaglibSharp-2.3.0.0` |
| QRCoder 1.8.1 | `d050cc7c0bc986f14cfedf24b947e0ec5c1a7736`, recorded by the NuGet package |
| VLC 3.0.24 | Official `vlc-3.0.24.tar.xz`; upstream Git tag resolves to `6de05adcbaf2e8b85fe86aad4169393098628119` |

## Native dependency inventory limitation

The companion archive is **not verified complete corresponding source for every native binary**. The VideoLAN.LibVLC.Windows 3.0.24 NuGet package does not supply an exact native build configuration or an external-library source manifest. The inspected Windows x64 payload includes 321 plugin DLLs, including `libx26410b_plugin.dll`; the [matching VLC module source](https://github.com/videolan/vlc/blob/6de05adcbaf2e8b85fe86aad4169393098628119/modules/codec/x264.c) identifies GPL-2-or-later. The NuGet package's LGPL expression must not be read as a uniform license for every plugin or external library. Both VLC GPL and LGPL texts are included.

The VLC source archive contains 117 `contrib/src/*/rules.mak` files, checksum files and patches, plus its build machinery and instructions. These identify source inputs to the VLC release recipes, but the inspected archive contains no external source archives under `contrib/tarballs/` and does not prove which recipe options produced every NuGet binary. For example, those recipes identify x264 revision `e067ab0b530395f90b578f6d05ab0a225e2efdf9` and FFmpeg 8.1.2; this is recipe provenance, not an independently verified binary-to-source match.

As checked on November 14, 2026, the [VLC 3.0.24 release directory](https://download.videolan.org/pub/videolan/vlc/3.0.24/) provides the VLC source archive, signatures/checksums and platform binary subdirectories; no complete external-source bundle was listed there. VideoLAN separately hosts a [per-component contrib source mirror](https://download.videolan.org/pub/contrib/), including [FFmpeg 8.1.2](https://download.videolan.org/pub/contrib/ffmpeg/ffmpeg-8.1.2.tar.xz). The older [Windows contrib directory](https://download.videolan.org/pub/videolan/contrib/x86_64-w64-mingw32/) contains historical build archives and a moving `latest` archive; neither was established as an exact source bundle for the NuGet payload. No such unverified bundle is included.

The release maintainer must resolve the remaining native dependency provenance and applicable redistribution requirements before describing a binary/source package as complete. The generated source archive's manifests record exactly what it contains and its SHA-256 hashes. The VLC hash is checked against the publisher's checksum; the three GitHub snapshot hashes were pinned after retrieving the exact revisions and checking the archive root and license text. Those GitHub hashes provide repeat-download integrity rather than an independent publisher signature. Upstream source archives and license texts are preserved unchanged.
