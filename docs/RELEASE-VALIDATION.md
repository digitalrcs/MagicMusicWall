# Release validation

## Version 1.0.1 maintenance update

Version 1.0.1 moves **Mini player** after both main view choices and refreshes the current guides and main-window screenshots. The version 1.0.0 results below remain the broader validation baseline; they are not a claim that every check was rerun for this maintenance update.

The 1.0.1 Release native-session harness passed all 20 checks. Normal and compact main-window screenshots were regenerated; the main-window layout was visually reviewed with Mini player after both view choices.

## Version 1.0.0 baseline

Validated on Windows on 24 November 2026. Automated fixtures use generated or fictional media and isolated data folders; they do not modify the installed collection.

| Area | Evidence |
| --- | --- |
| Build | Release build succeeds with zero warnings and errors. |
| Core | 263 tests pass, including exact duplicate queue identity, session recovery, catalog removal/undo, M3U interchange, relinking, smart playlists and validated backup rollback. |
| Streaming | 244 tests pass, including full queue paging, stale commands, browser media rules and server-enforced guest authorization/revocation. |
| Native session | 20 checks pass using the real WPF app and silent WAVs: paused restore without loading audio, retained position, duplicate selection, reordering, undo, external file append and graceful-close persistence. |
| Details/settings | Native normal/small reader sizing, independent scrolling, splitter and unsaved-settings checks pass. Switching tabs does not incorrectly mark settings dirty. |
| Web | Chromium desktop/phone checks pass for a 6,000-entry queue, duplicate occurrences, page races, shrinking queues, persistence, clear/undo, reconnect, expired sign-in, removed files and guest controls. Real desktop-bridge fixture captures were also checked. |
| ReplayGain | Digital PCM tests pass: −6 dB gain ratio 0.5001, positive gain peak protection 1.0508 with a 0.95 declared peak, and untagged ratio 1.0000. No speaker was needed for these measurements. |
| Visuals/analysis | Ten visual modes render at three power levels; generated-audio analysis, cache invalidation, cancellation and corrupt-cache recovery pass. |
| AI/map UI | Nine isolated native checks cover saved requests, queue exclusions, exploration restoration, follow-playing and related-track queueing. No live AI request is implied. |

## Reproduce

Run `pwsh -File scripts/Verify.ps1 -Desktop` on Windows with .NET 10. The optional `-ReplayGain -FfmpegPath <path>` requires FFmpeg to generate disposable test files. Run the commands in [the web-check README](https://github.com/digitalrcs/MagicMusicWall/tree/main/tests/MagicMusicWall.Web.Checks) for browser checks. See [building](BUILDING.md) for native VLC prerequisites.

Documentation screenshots show the current application with demonstration metadata. They are not pictures of a customer's library. Main library, minimum-size layout, Setup Sound, Manage folders, song details, article reader, AI assistant, constellation and web remote were refreshed.

## Boundaries

- Physical Bluetooth receivers, Chromecast/Fire TV devices, network shares under failure, and every media codec/device combination require testing on the destination hardware. The output test tone helps verify the selected speaker.
- ReplayGain reads existing track tags; it does not analyze loudness or change original files. Restart after changing the setting. Gapless playback and crossfade are not guaranteed in this version.
- Media keys are handled through Windows application commands. Windows lock-screen/System Media Transport Controls integration is not provided.
- Detail tabs organize existing workflows. File tags, MusicBrainz review and AI-note editing retain separate guarded editor windows rather than embedding their save/cancel lifecycles.
- Private ZIP backups include library, settings and listening session only. They exclude media, audio-analysis caches and AI notes. Restore validates entries before replacing files, retains a recovery ZIP, and rolls back handled failures. Multiple files cannot be replaced as one power-loss-atomic transaction.
- Guest links are append-only for the PC queue. This does not turn the existing trusted-LAN HTTP service into an internet-facing or encrypted service.
- Cloud sync, a plugin framework, extra AI providers and additional visualizer styles were outside this release.

AgentChat's shared task board and pinned decisions coordinated this work. The installed AgentChat version exposes no whiteboard canvas/API. LM Studio supplied bounded regression suggestions; they were reviewed, corrected where necessary and checked against actual behavior.
