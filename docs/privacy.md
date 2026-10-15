# Privacy and reports

BF6 Crash Helper works locally. Opening it does not start a scan. You explicitly choose to run the check, then decide whether to save a report. There is no automatic upload, account sign-in or telemetry submission.

## A report is a summary

The shareable report is built from selected diagnostic fields rather than bundling the source files. Review its preview and exported text before posting it. It can contain relevant game/build identifiers, crash metadata, selected check results and game-relative file references. Personal paths are replaced with neutral placeholders in that summary. Optional mode, map and free/paid-access selections are user-reported context, not verified account information.

For the recognized binary-specific crash, the parser can also include a captured numeric CAS file identifier and, when the recorded stack supports it, a byte offset and length. These identify a game-file request; they are not account IDs or decryption keys. Only the interpreted numbers and their validation status are exported. Raw registers, stack bytes, memory ranges and source dump contents are excluded. Missing or inconsistent optional data remains unknown.

Raw minidumps, game executables, complete logs, Steam account/licence files, authentication material and ETL recordings do not belong in the share report. The app does not need an EA password, Steam password, API key or login cookie. Do not add these to an issue manually.

Scanning an existing crash file does not make the original safe to share. A minidump can include strings, memory fragments and identifying paths beyond the fields selected for a report. A full dump can contain substantially more private application memory.

## Optional file recording is separate

The timed Windows Performance Recorder option requires a separate confirmation and administrator prompt. Close Battlefield first; launch the game yourself after the status confirms that recording has started. It records file activity rather than reading arbitrary game heap memory. Kernel recording can still include absolute paths and information about other processes on the computer.

The ETL remains local and is excluded from report export. Choose a deadline of 1–30 minutes; the worker requests stop/save at that limit, on the first observed Battlefield exit or on your Stop request. Exit alone is not classified as a crash. If Windows rejects or cannot confirm a stop, the deadline is not a guarantee that recording ended. Its 128 MiB circular event buffers retain a recent window; long recordings can overwrite earlier activity. That buffer size is not a promise that the whole worker or saved ETL uses exactly 128 MiB. Keep private traces private unless you have separately reviewed the contents and deliberately chosen a recipient. The application does not upload them.

Starting and stopping this recording is not a request to change Windows tracing permissions, install a driver or disable protection. An unavailable recorder or rejected elevation should leave the basic scan usable. The recorder uses its own recording instance and must not cancel another application's trace.

### Recording status and recovery

Read the current state before assuming a trace was saved or stopped. **Failed** describes an unsuccessful attempt; **Cleanup pending** means the helper cannot confirm that its private Windows recording ended. An interrupted worker or unreadable state is not reported as successful cleanup.

On reopening, the app reads a bounded local history of its own initialized capture folders and reconnects to a pending recording it finds. It can offer Stop for an active worker or Retry cleanup for an unconfirmed one. This reads local state and checks the helper's worker lock; it does not start, stop or restart a Windows recording automatically. Discovery examines at most 100 capture-folder entries and selects the most recent pending run within that inspected set. It is not a system-wide inventory of all Windows recordings.

Close Battlefield and choose **Retry cleanup** to attempt to stop only that named helper recording. A separate OK/Cancel explanation and Windows administrator prompt precede this retry. It does not stop unrelated recording sessions. Declining or closing the app does not confirm cleanup, and an unconfirmed stop remains visible as such.

## What these checks cannot prove

- A file present on disk is not proof that the game registered it successfully at runtime.
- A file matching a Steam manifest is evidence about bytes, not an account's Steam or EA entitlement.
- File activity can establish observed operations. It does not reveal why the engine selected or rejected a content package.
- A known crash signature requires the appropriate binary identity. The same offset on another build does not establish the same failure.

For an entitlement check, use Steam's own DLC list and the game's content-management screen. [Steam explains what the DLC list shows](https://help.steampowered.com/en/faqs/view/6948-51E6-E624-8A84). No licence dump or network interception is needed for that visual comparison.

## Local recovery data

A cache test writes a local recovery journal and keeps the old cache beside its original location. Undo can keep the newly rebuilt cache in another sibling directory. These records contain real local paths because recovery must identify the exact files; they are not part of the public report. [Recovery details](recovery.md).

Deleting the portable executable does not delete your backups or traces. Keep them until you have checked the result and no longer need recovery.
