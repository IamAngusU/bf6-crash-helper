# Rapid Royale / Mirak Valley: experimental workaround

Author: [IamAngusU](https://github.com/IamAngusU) · [Source repository](https://github.com/IamAngusU/bf6-crash-helper) · [German quick start](../README.de.md)

This packages a successful **single-PC experiment**, not a proven universal fix. After the official update, the untouched installation still produced the same crash. After withholding two optional Mirak indexes, the player reported multiple successful Rapid joins, including further play the next day. Direct capture proving which base-map path the successful joins used is still missing. Other maps, server-side changes and intermittent failures can affect the result. [Evidence timeline](case-evidence.md).

## What you approve

The app defaults to no changes. **Check compatibility** is a read-only action. The separate Apply checkbox/button explains the operation before the Windows elevation prompt. Reopening the same downloaded executable finds its recovery record; Undo has its own unchecked consent. No installer, login, Python, Git, package manager, driver, protection-setting change or scheduled task is needed for this workaround.

<img src="../assets/rapid-workaround.png" width="800" alt="Rapid Royale page with separate compatibility, Apply consent and Undo consent controls. The window can be resized or scrolled.">

Only this exact two-file pair is removed from the game's loading path, after verified backups of **both** exist:

```text
Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc
Update\BF6_SAN2_DLC\Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.digest
```

The intact base index at `Data\Win32\game\glaciermp\levels\mp_tungsten\mp_tungsten.toc` is a required guard. The experiment is intended to let the game select its free base content. That fallback is a hypothesis, not a verified runtime guarantee. CAS files, the executable, settings, account/licence files and anti-cheat components are not changed. It can fail to help or make a load fail; close BF6 and use Undo in that case. No paid Battlefield purchase is proposed.

## Supported installation

- Windows 10/11 x64 with .NET Framework 4.8; local NTFS Steam library.
- Steam app `2807960`, build **25851446**, with the manifest reporting its fully installed state.
- The selected game directory must match that app manifest under the same `steamapps/common` library.
- The exact reviewed SHA-256 values are compiled into [MirakSpec.Production](../src/MirakRecovery.cs), for `bf6.exe`, `Data/layout.toc`, the base Mirak TOC and both optional targets. Future/different builds and altered files are refused.
- BF6 must be closed before Apply or Undo. Steam may remain open, but an active/unfinished update or a manifest that cannot be locked for read causes a refusal.
- Linked, redirected or multiply linked removal targets and paths too long for safe recovery are refused. A moved installation is not automatically rebound to an old recovery record.

The helper does not infer Steam or EA ownership. A file-integrity match cannot establish entitlement. If one of the optional indexes is already absent and this Windows user has no helper recovery record, it refuses Apply. It cannot invent the original files or claim to have backed up a manual workaround. Use your original backup or Steam verification.

## Recovery and failures

Backups and the durable recovery journal stay under:

```text
%LOCALAPPDATA%\BF6CrashHelper\mirak-v1\
  mirak.json
  mirak.json.previous
  <transaction-id>\0.original
  <transaction-id>\1.original
```

`Prepared → BackedUp → Applying → Applied` and `UndoPending → Restored` distinguish interruption from completion. Journal replacement and backup writes flush to disk before removal. A fresh transaction never overwrites an old backup. A bounded journal and per-installation/global and per-store locks prevent silent overlapping changes.

Both backups are copied and hash-verified before either original is removed. Removal uses an exclusive, final-path-validated original-file handle. Directory ancestors are held against rename while writing; the original bytes are verified again immediately before removal. A failed Apply attempts automatic recovery. If the game starts, a write fails or a conflict blocks that recovery, the pending journal and available copies remain for **Undo / recover interruption** after closing BF6. The app never claims an incomplete operation succeeded.

Undo preflights the entire pair and guards before restoring anything. It keeps backup copies, stages each restored file in its destination directory, verifies the staged bytes, then moves it into an absent destination without overwrite. That works across different Steam/AppData drives. Already restored identical originals are accepted without rewriting them. A crash during staging can leave a short `.b6-*.tmp` file; retry uses a fresh name and preserves the old partial file. A corrupt/missing backup or conflicting current original stops automatic restoration.

Validated guard files and backups are held read-only for the mutation window. During Undo, already-present original files are held stable too. This closes the gap between preflight and copying if another program tries to change those inputs; an actual I/O failure can still leave a pending operation that needs recovery.

After a game update, **old-build backups are never inserted into the new build**. If both indexes are present, Undo records `UpdatedFilesPreserved`, keeps those newer files and retains the old backups. If updated files are missing, it stops and directs you to Steam verification for current originals. Same-build changes to guarded files also stop restoration. Corrupt journals are not silently reset; keep the journal and backups for inspection.

The elevated worker consumes one bounded, time-limited request, checks the same Windows account, build/hashes, closed game and recovery state again, and returns a local result receipt. Cancelling UAC consumes that request without starting a change. A timeout, interrupted worker or missing receipt is **unconfirmed**, not success; review recovery before retrying. If you need another administrator's credentials, that account has a different AppData recovery store; the helper refuses that cross-account request rather than losing track of backups.

Steam verification is a separate, confirmed action on the Cache tab. It can restore current Steam files and undo the experiment's absence; the helper does not undo Steam downloads. Recheck compatibility before applying again. Do not remove backups just because a successful round occurred. Deleting or replacing the portable executable does not delete the recovery history.

## Verification

`./build.ps1 -Test` uses synthetic Steam libraries and fake game bytes, not a live installation. The workaround suite covers successful apply/undo/reapply, stale review tokens, unsupported/unfinished/mismatched manifests, missing targets, changed guards, each backup/removal failure point, a game starting mid-operation, changed originals, corrupt/missing backups, update preservation, update conflicts, Steam restoration, interrupted prepare/apply/undo, partial staging, tampered journals, locks, read-only files, hard links, directory junctions, long paths, worker ownership/expiry/bounds and consumed cancellation requests. UI checks cover initial consent and independent Apply/Undo gates. Existing synthetic dump, recorder and cache suites remain included.

These tests verify defensive behavior, not that the game will work for every player. No test suite approves real UAC, modifies a live Steam installation or proves anti-cheat/publisher approval. The workaround itself is supported by the private experiment described above. Release verification and any hosted-CI limitation are stated in each release. An unsigned executable and a matching hash still require a decision to trust its source/build.

## Review before execution

- [Launcher equivalent](../start.ps1): version-pinned download and SHA-256 check; no `Invoke-Expression`, execution-policy changes or automatic Apply.
- [Recovery engine](../src/MirakRecovery.cs): exact scope, allow-list, journals and restoration.
- [Elevation worker](../src/MirakWorker.cs): request validation, one-time claims and result receipts.
- [Tests](../tests/MirakTests.cs): temporary fixtures and failure cases.

Please inspect the code before execution, even though it is public on GitHub. Public source and checksums are useful for review; they are not proof of harmlessness. Reports are optional, local and separate. Do not upload raw game files, dumps, private traces or recovery backups.
