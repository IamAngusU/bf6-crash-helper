# BF6 Crash Helper v0.4.0

**Only the Rapid Royale workaround and its recovery.** More tools, general diagnostics, dump reading, cache actions and Windows recording have been removed from the executable, including the recording worker entry point. The build uses an explicit fix-only source list and a test checks the excluded types are absent.

Existing helper records are now checked against the live build, guards, original index files and required backups. Reopening automatically checks its recorded installation. It distinguishes **Workaround already applied**, **Original files already restored**, newer game files, interrupted recovery, blocked recovery and missing optional files without known history. Apply is never offered again for a pending recorded change; absence alone is never labeled a confirmed fix. Existing v0.2/v0.3 workaround journals remain compatible.

English remains the default, with a live Deutsch switch. The footer now reads **Powered by IamAngusU** and links to the repository. Full technical errors stay under Details, with a short readable status summary.

The exact two-file scope, verified backups, same-user UAC opt-in, guarded undo and update/conflict protections remain. **Experimental; Steam build 25851446 only.** A verified applied state describes files and recovery, not a permanent or universal game fix. [Scope and evidence](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.4.0/docs/rapid-workaround.md).

Locally built/tested on Windows: **109 Mirak assertions in 25 scenarios**, plus compact UI consent, language, recognition and fix-only assembly checks. English/German initial and applied-state previews were rendered and reviewed. Tests use synthetic libraries, with no live game mutation, UAC or ETW. The original private experiment's backups were separately verified and copied into local helper history without changing game files. An additional read-only local GUI probe confirmed Workaround already applied, Undo available and Apply disabled on the original PC. No private journals or game data are published.

Hosted CI could not start because the account is blocked; no hosted build attestation is claimed. The EXE is unsigned. Review [the code](https://github.com/IamAngusU/bf6-crash-helper/tree/v0.4.0/src) before execution. The [README one-liner](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.4.0/README.md#quick-start) pins the release/hash and only opens the UI. Keep Windows protection enabled.

Powered by [IamAngusU](https://github.com/IamAngusU). MIT.
