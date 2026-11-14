# Contributing to Magic Music Wall

Start with the [developer build guide](docs/BUILDING.md). Use an isolated development data directory and synthetic or disposable media for testing.

The project's source, documentation and DigitalRCS branding are copyright reserved. Making the repository public does not grant an open-source license. See [LICENSE.txt](LICENSE.txt) and contact DigitalRCS.com for permissions; third-party components keep their own licenses.

## Report a problem

Include the app version, Windows version, steps to reproduce, expected result and actual result. For playback issues, identify the output connection type and whether the same device works in another app. For metadata issues, describe the format and selected fields.

Remove remote access links, QR codes, provider tokens, personal media paths and other private details before posting logs or screenshots. A small synthetic sample or clear reproduction is more useful than a full personal library export.

## Propose a change

Explain the user-visible problem and the resulting behavior. Keep the change focused. Include relevant validation and any limitations, such as untested physical casting or audio hardware.

Preserve these behaviors:

- Browsing a folder does not queue it; an individual-file action affects that file unless the user explicitly chooses a bulk action.
- Library entries and playlists reference stable media IDs.
- Removing a library entry does not delete the original media.
- File-tag writing requires review and confirmation, stages and verifies the edit, and retains an original-file backup.
- Public packages contain no user library, settings, media or access keys.
- The web remote exposes the authorized catalog, not an arbitrary disk or shell interface.

Add tests where they protect meaningful behavior. Do not use the user's actual media for write tests. UI or packaging changes should include an appropriate rendered/runtime check without claiming untested hardware results.

Read the project's license information and [third-party notices](THIRD-PARTY-NOTICES.md) before redistributing components or importing external code/assets. Preserve source credits and the existing DigitalRCS.com branding.
