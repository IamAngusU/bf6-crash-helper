# Cache test and recovery

For the separate two-file Rapid Royale workaround, use [Rapid recovery and update behavior](rapid-workaround.md). Its backups are distinct from the cache backups below.

A cache reset is an optional experiment. It is not a verified fix for the investigated Rapid Royale failure. The original case crashed again after a period of successful play following a reset.

## Before the test

Close Battlefield normally. The app does not stop it for you. Steam can stay open because this operation does not change Steam's files.

The only game directory involved is the local Battlefield cache:

```text
%LOCALAPPDATA%\Battlefield 6\cache
```

Game installations, settings, account data and broader NVIDIA/DirectX caches are outside this operation. Review the app's explanation and explicitly confirm before applying it.

## What happens

The helper fingerprints the cache contents, records the recovery plan, checks that Battlefield is still closed, and moves the existing cache to a uniquely named sibling backup. It creates an empty cache directory for the game's next launch.

Backup names use this form:

```text
cache.bf6helper-backup-<unique-id>
```

The game may rebuild cached data on its next launch. Compilation can take time and temporarily affect smoothness. Observe several relevant round transitions before drawing conclusions; a single successful join does not establish a fix.

## Undo

Close Battlefield again, then choose the app's undo action. The helper validates the original backup before moving it back. If the game has created a new cache, that directory is preserved separately:

```text
cache.bf6helper-kept-<unique-id>
```

Undo restores the selected original cache and retains the rebuilt one rather than overwriting it. Saved recovery records are stored locally under `%LOCALAPPDATA%\BF6CrashHelper`.

## If an operation stops halfway

Use undo before starting another cache change. The journal records pending moves so the app can distinguish an interrupted operation from a completed one. If a backup is missing, changed, linked or outside the expected directory, automatic recovery stops with an explanation.

Keep the journal, original backup and any kept cache. Do not resolve an uncertain state by deleting directories or overwriting the live cache. The app must preserve the available copies and report the unresolved condition.

## Steam verification

The **Start Steam file verification** button presents an additional OK/Cancel explanation. Confirming launches Steam's verification for Battlefield 6. Steam may download original files again, and these changes have no undo in the helper. Opening the app or running a scan never starts this operation.

A clean verification result is useful evidence about installed files, not proof that an internal runtime registration succeeded. The cache undo action only restores the cache saved by the helper; it does not roll back Steam verification.
