<p align="center"><a href="https://github.com/IamAngusU/bf6-crash-helper"><img src="assets/brand-header.svg" width="1100" alt="BF6 Crash Helper — verified build, backups before changes, undo and recovery."></a></p>

<p align="center"><a href="README.de.md">Deutsch</a> · <a href="#quick-start">Quick start</a> · <a href="docs/README.md">Documentation</a> · <a href="https://github.com/IamAngusU/bf6-crash-helper/releases/tag/v0.4.0">Download</a></p>
<p align="center"><a href="docs/privacy.md"><img src="assets/badge-local.svg" height="36" alt="Local · no auto-upload"></a> <a href="docs/rapid-workaround.md"><img src="assets/badge-undo.svg" height="36" alt="Verified backups + undo"></a> <a href="LICENSE"><img src="assets/badge-mit.svg" height="36" alt="MIT license"></a></p>

**One small window for the REDSEC Rapid Royale / Mirak Valley workaround and its undo.** English starts by default; select Deutsch at any time. No installer, Git, Python, Node or package manager is needed.

**Experimental; Steam build 25851446 only.** Several joins worked on the investigated PC after this two-file change, while the official update alone had still crashed. This is not a proven universal fix. Direct proof of the successful base-map fallback and broader testing remain incomplete. [Evidence and limits](docs/case-evidence.md).

<img src="assets/rapid-workaround.png" width="564" alt="Small English fix-only window with compatibility check, explained consent, Apply and Powered by IamAngusU. No More tools button.">

## Quick start

<a href="https://github.com/IamAngusU/bf6-crash-helper/releases/tag/v0.4.0"><img src="assets/badge-download.svg" height="42" alt="Download for Windows"></a>

**One line in PowerShell:**

```powershell
& { $ErrorActionPreference='Stop'; $p=Join-Path $env:TEMP ('BF6CrashHelper-'+[guid]::NewGuid().ToString('N')+'.exe'); Invoke-WebRequest -UseBasicParsing 'https://github.com/IamAngusU/bf6-crash-helper/releases/download/v0.4.0/BF6CrashHelper.exe' -OutFile $p; if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne '1f66a08fdeee24ba3295f707b8494c5639b4e6f6bd0fa48294a25733b71d2d81'){throw 'SHA-256 mismatch; do not run this download.'}; Start-Process -FilePath $p }
```

This downloads the pinned v0.4.0 EXE, verifies SHA-256 and opens its window. It does not apply a change or request administrator rights. Review the [readable launcher](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.4.0/start.ps1), [recovery code](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.4.0/src/MirakRecovery.cs) and [elevation worker](https://github.com/IamAngusU/bf6-crash-helper/blob/v0.4.0/src/MirakWorker.cs) **before running it**. Public source and a matching hash are not a safety guarantee.

Or download **BF6CrashHelper.exe** from [the release](https://github.com/IamAngusU/bf6-crash-helper/releases/tag/v0.4.0) and double-click it. Windows 10/11 x64 with .NET Framework 4.8 is required. The EXE is unsigned; keep Windows protection enabled and decide whether you trust its source/build.

1. Close BF6 normally and choose **Check compatibility**. The Steam folder is detected automatically; **Game folder …** lets you select it.
2. Read the scope. Only the reviewed build with matching original EXE, layout, base-map and optional index hashes is supported.
3. Tick the unchecked consent and choose **Back up and apply workaround**. This explicit choice opens UAC. Use the same Windows account.
4. Test several relevant Rapid joins. To revert, close BF6 and reopen the helper. **Undo workaround** appears in the same action area with its own consent; verified backups are retained.

## Already applied?

The helper finds its own saved change when reopened, then checks the current build, guards, index files and needed backups. **Workaround already applied** means the recorded change is still present and Undo is available. Apply stays disabled, so it cannot apply the same change twice. Existing v0.2/v0.3 workaround records remain compatible.

If Steam restored the originals, it shows **Original files already restored**. **Finish recovery** checks them and closes the saved record. After an update it preserves current files; old backups are not inserted into a new build. Partial changes are offered for recovery; conflicts or damaged backups block the operation. [Failure handling](docs/rapid-workaround.md).

Missing optional files without a helper record are shown as **Files absent without known backups**, not as a confirmed fix. The app cannot invent a backup or identify who removed them. Keep your original backup or use Steam's file verification for current original files.

## Exact scope

Exactly the optional SAN2 Mirak TOC and digest are backed up and removed from the loading path. Both backups must be durable and verified before either removal. The base map remains. A running game, unsupported build, altered guard, linked path or conflicting original stops changes. [Exact paths, guards and rollback](docs/rapid-workaround.md).

Version 0.4 contains **only this workaround and recovery**. The former general diagnostics, dump reader, cache actions and Windows recorder are removed from the executable. There is no More tools page or recording worker. It does not change account permissions, game settings or anti-cheat files. Buying Battlefield is not a proposed fix.

There is no automatic upload. **Details**, **Backups** and **Review source** explain the current operation. The footer links **Powered by IamAngusU** to this repository. [Privacy](docs/privacy.md) · [Forum draft, English / German](docs/forum-post.md).

## Verification and feedback

Developers: `./build.ps1 -Test` on Windows. The build uses an explicit fix-only source list. Tests cover apply/undo, failures during backup/removal, interrupted operations, updates, file conflicts, corrupt backups, path links, concurrent operations, one-time worker requests, recognition after reopening and UI consent/language gates. A scope test confirms that diagnostics, cache and recorder types are absent. Tests use synthetic libraries, without live game changes, UAC or ETW. Each release states its actual verification and CI limits.

Report the build, mode/map if known and the visible status through [GitHub Issues](https://github.com/IamAngusU/bf6-crash-helper/issues). Keep game files, backups, dumps and private logs out of public issues.

---

Powered by [IamAngusU](https://github.com/IamAngusU). [MIT](LICENSE). Independent community project, not affiliated with EA, DICE, Battlefield Studios or Valve.
