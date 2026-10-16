# Privacy

Version 0.4 is only the two-file Rapid Royale workaround and its recovery. It contains no general diagnostic scan, dump reader, cache controls, report export or Windows recorder. No game injection, account login, telemetry or automatic upload is used.

Compatibility reads Steam's installation paths, library list and app manifest, then the exact EXE, layout, base-map and optional index files needed by the build/hash allow-list. It does not inspect account licences or credentials. Unsupported or unknown state stays unsupported or unknown.

Opening a fresh helper does not scan a game. When its own pending recovery record exists, opening it automatically verifies that recorded installation's current files and backups, so it can distinguish an active workaround, restored originals, an update and blocked recovery. This is read-only. Apply/Undo still require separate, unchecked consent and the same-user Windows elevation prompt.

The private store `%LOCALAPPDATA%\BF6CrashHelper\mirak-v1` contains installation paths, build/state, original index backups, recovery journal and local one-time elevation requests/receipts. These are needed for recovery and stay local. Deleting the EXE does not delete them. Do not upload these game-data backups or local journals publicly.

Details shows paths and full technical errors locally. The source and Powered by IamAngusU links open GitHub only when selected; Backups opens the local folder. Keep private paths and account data out of screenshots/issues.

Older releases had additional private cache/trace functions. Version 0.4 does not delete their data, stop their recordings or provide those controls. If an older recording is still active, finish its cleanup with the older helper before replacing it. The independent private investigation is not distributed in this release.
