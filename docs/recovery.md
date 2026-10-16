# Backup, recognition and undo

The current app exposes only the Rapid Royale workaround and its recovery. [Exact files, supported build, preflight and failure behavior](rapid-workaround.md).

1. Close BF6, review compatibility and read the explained two-file scope.
2. Explicitly consent to Apply. Both original backups must be durable and hash-verified before either optional index is removed.
3. Reopening detects the helper's own record and checks live files. A verified applied workaround offers Undo instead of another Apply.
4. To revert, close BF6, consent separately and choose Undo. Backups are preserved after restoration.

Originals restored by Steam are recognized separately. Finish recovery checks them and closes the saved operation without rewriting identical files. Updated builds keep current files; old backups are not inserted. Partial apply/undo states can resume recovery. Conflicting originals, damaged backups or unverified paths stop changes and preserve available evidence.

Absence without an app record is not proof of a successful workaround. The app does not manufacture backups for a manual change. Keep the original backup or use Steam's own file verification for current originals.

The journal format and backup folder remain compatible with v0.2/v0.3 workaround records. The maintainer's earlier private experiment was migrated separately after full file/hash verification; arbitrary external scripts are not automatically imported by the public app.

Cache maintenance and tracing from earlier releases are not part of the current executable.
