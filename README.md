<p align="center"><a href="https://github.com/IamAngusU/bf6-crash-helper"><img src="assets/brand-header.svg" width="1100" alt="BF6 Crash Helper — inspect the evidence, choose each change, keep a way back."></a></p>

<p align="center"><a href="README.de.md">Deutsch</a> · <a href="#quick-start">Quick start</a> · <a href="docs/README.md">Documentation</a> · <a href="https://github.com/IamAngusU/bf6-crash-helper/releases">Releases</a></p>

<p align="center">
  <a href="docs/privacy.md"><img src="assets/badge-local.svg" height="36" alt="Local · no auto-upload"></a>
  <a href="docs/recovery.md"><img src="assets/badge-undo.svg" height="36" alt="Cache backup + undo"></a>
  <a href="LICENSE"><img src="assets/badge-mit.svg" height="36" alt="MIT license"></a>
</p>

Battlefield closes while you join a round. BF6 Crash Helper helps you collect useful evidence and try a reversible game-cache reset, with a separate choice for every action.

**The current Rapid Royale loading crash is not fixed by this tool.** A successful round after a cache reset does not establish a fix. [What the investigation actually found](docs/case-evidence.md).

## Quick start

<a href="https://github.com/IamAngusU/bf6-crash-helper/releases/latest"><img src="assets/badge-download.svg" height="42" alt="Download for Windows"></a>

1. Open the [latest release](https://github.com/IamAngusU/bf6-crash-helper/releases/latest) and download **BF6CrashHelper.exe**.
2. Double-click the downloaded file. No installer, account, terminal command or PowerShell script is needed.
3. Read the scan explanation, tick its consent box and click **Run check**. Opening the app does not scan or change your PC.
4. Review the results. Export a report only when you choose to save one.

For Windows 10/11 with .NET Framework 4.8 available. No separate runtime is needed on a current supported Windows installation. The release executable is unsigned: if Windows shows SmartScreen, review the publisher/source and file before deciding whether to open it. Keep Windows protection enabled.

Ordinary scans run without administrator rights. The optional Windows file trace has its own explanation, confirmation and administrator prompt.

## Choose the next step

- **Scan:** inspect installation and existing crash metadata, plus selected files against available local Steam manifests. This is a targeted check, not a full Steam verification.
- **Save a report:** create a local, sanitized summary you can review before sharing. Nothing is uploaded automatically.
- **Try a cache reset:** close Battlefield first, explicitly confirm the test, and retain the previous cache as a backup. Steam may stay open. Undo preserves any rebuilt cache too.
- **Start Steam verification:** an additional OK/Cancel dialog explains that Steam may download original files again. Only confirming starts it; the helper cannot undo Steam's changes.
- **Record a short file trace:** separately opt in to a timed Windows recording. Its private ETL can contain file paths and process information and stays outside the share report.

If Windows cannot confirm that a recording stopped, the app shows **Cleanup pending** rather than success. **Retry cleanup** has its own confirmation and administrator prompt and targets only the helper's named recording. Reopening the app checks its recent local recording history so a pending recording can be controlled again; it never starts a new capture automatically. [Recording status and recovery](docs/privacy.md#recording-status-and-recovery).

Cache resets can cause recompilation and temporary stutter. They do not repair a proven engine bug or change account entitlements. [Backup and undo](docs/recovery.md) · [Exactly what is shared](docs/privacy.md).

## Evidence before guesses

This project grew out of a repeatable REDSEC Rapid Royale loading failure. Offline analysis identified a specific CAS file request while its file matched Steam's active manifest. The missing runtime registration's cause remains unknown. Neither a decryption failure nor a Steam/EA licence rejection was demonstrated.

Rapid Royale is part of free REDSEC. Buying Battlefield 6 is not a proposed fix. [Case evidence and sources](docs/case-evidence.md) · [Deeper analysis, REA and capture limits](docs/deeper-analysis.md).

## Contribute

Report reproducible behavior through [GitHub Issues](https://github.com/IamAngusU/bf6-crash-helper/issues). Include the game build, map/mode, loading stage and a reviewed summary. Keep raw dumps, traces, account data and game binaries private. Existing crash signatures are specific to a binary version; an unknown result is useful and must stay unknown.

Developer build instructions are in the repository build script. Players only need the release executable.

---

Created by [IamAngusU](https://github.com/IamAngusU). [MIT](LICENSE) · Independent community project, not affiliated with EA, DICE, Battlefield Studios or Valve. Battlefield and Steam names belong to their respective owners.
