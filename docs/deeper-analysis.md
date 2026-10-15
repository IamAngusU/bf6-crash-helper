# Deeper analysis: REA, dumps and runtime evidence

The open question is why a valid on-disk CAS file had no usable entry in the game's internal lookup at the failing request. A Steam ownership check, successful file verification and a game engine's runtime registration are different observations.

This document describes further analysis options. BF6 Crash Helper does not install REA, a decompiler or a debugger, and its normal scan does not attach to the running game.

## Which REA?

The original project is **[morluto/rea — Reverse Engineer Anything](https://github.com/morluto/rea)**, published as `rea-agents`. Similar names and copied READMEs in forks do not establish an official download.

REA connects an agent or command line to analysis providers. It can return disassembly, decompiled functions, strings, references and bounded static value-flow results. It does not automatically recover original source or prove that a possible branch executed in a particular match.

### Practical Windows requirements

The project's [Windows Ghidra P0 documentation](https://github.com/morluto/rea/blob/main/docs/windows-ghidra-p0.md) describes an experimental, read-only native PE boundary. It requires Windows x64 with local NTFS target/temp storage, a compatible Node.js release, an existing Ghidra 12.1.x installation and a full 64-bit JDK in Ghidra's supported range. Current documented Node ranges are 22.x ≥22.19, 24.x ≥24.11 or 26+; the documented Ghidra 12.1 range uses JDK 21 or newer.

That is an analyst workstation setup, not a zero-setup dependency suitable for every player. REA's setup can change agent registrations and install workflow instructions. Repository `main` may also contain capabilities absent from a published npm package. Check the actual version, available provider and supported tools before claiming a capability. [Installation and release boundaries](https://github.com/morluto/rea/blob/main/docs/installation.md).

### Useful offline work here

Use a stable, locally authorized copy of the exact investigated PE, record its digest, and work from known crash RVAs. Follow callers and references around CAS registration, lookup-table construction, mount/unmount lifecycle and any conditional package checks. Keep observed instructions separate from decompiler guesses and retain unknown indirect calls as unknown.

REA's [native investigation guide](https://github.com/morluto/rea/blob/main/docs/native-investigation.md) explicitly distinguishes static references and inferred value flows from observed runtime execution. Decompilation could identify a relevant entitlement branch; it would still not establish that the branch rejected this user's account when the crash occurred.

The missing heap bytes are the current evidence gap. A different disassembler cannot reconstruct runtime table contents absent from the dump.

### Windows minidump limitation

REA's documented [`inspect_recorded_crash`](https://github.com/morluto/rea/blob/main/docs/recorded-crashes.md) accepts Linux ELF64 little-endian cores for x86-64 or AArch64. That documented operation is not a Windows `.dmp`/`.mdmp` reader. Existing Windows postmortem analysis remains necessary for recorded registers, thread state and captured memory ranges.

REA's native PE support and Linux core support must not be combined into an unsupported claim of Windows crash-dump coverage. No REA installation, game injection, live debugging or anticheat modification was needed to establish the published CAS mapping.

### What the helper's own dump parser can recover

The helper independently reads bounded portions of an existing Windows minidump. CAS extraction is gated by the recognized `bf6.exe` timestamp, fault RVA, exception code and read target. It then requires a complete captured AMD64 context with consistent instruction/register values before treating R12 as a recorded lookup file identifier.

Offset and length require a further match: the request record at captured exception-context **RBP−0x40** must fall within recorded memory or the recorded crashing thread's stack, have the expected record fields, and contain the same identifier as R12. This reads the dump's recorded bytes, not the running game. It does not search unrelated memory or insert the original case's identifier into every result.

If the registers support only the lookup identifier, the report says so and leaves offset/length unknown. Missing stack ranges, unsupported contexts and inconsistent records cannot confirm a request. A complete request identifies the numeric file ID, byte offset and byte length; it still does not explain the runtime table's state, prove file corruption or demonstrate an entitlement rejection. The share report contains only those interpreted numeric fields and validation status, not raw registers or memory contents.

## What a short Windows trace can answer

Windows Performance Recorder can record file operations around one loading attempt. Correlating issue/completion events can show attempted access and results. An EXE/DLL loader event shows a module load; it does not show whether a CAS asset was mounted, parsed or admitted to an engine-private table. [Microsoft WPR recording guidance](https://devblogs.microsoft.com/performance-diagnostics/wpr-start-and-stop-commands/).

Kernel recording requires administrator privilege. A bounded start/stop recording does not require changing account membership, installing a driver or weakening security settings. [Microsoft's tracing privilege rules](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracea).

The helper's separate trace option should be used for one relevant loading attempt, with a short deadline and private local output. It can retain paths and other process metadata, so the raw ETL is outside the share report. It neither reads the game's arbitrary heap nor proves a Steam/EA entitlement result.

A stop request is not proof that Windows accepted it. Unconfirmed cleanup remains **Cleanup pending** and has a separately confirmed, elevated retry for the helper's own named instance. No successful stop or hard deadline is claimed when Windows has not confirmed it. [Recording recovery](privacy.md#recording-status-and-recovery).

## Why the tool does not silently enable full dumps

A full user-mode dump might contain the missing table and its owner, but also unrelated private application memory. It cannot guarantee exclusion of authentication or licence material.

[Microsoft WER LocalDumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps) uses administrator-controlled HKLM settings; `DumpType=2` requests a full dump. Microsoft notes that applications with custom crash reporting are not supported by that feature. Whether Battlefield's own crash path would allow the desired capture needs separate verification. Enabling it is not part of this portable helper's default workflow.

[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump) can monitor unhandled exceptions and produce a full dump. Exception monitoring interacts with the live process through debugger mechanisms, while full dumps include private, image and mapped memory. It falls outside this project's no-live-debugger capture route. Microsoft's documentation does not provide a Battlefield/anticheat compatibility guarantee.

## Entitlements without collecting account material

For a narrow comparison, inspect Steam's own **Properties → DLC** list and the game's **Install/Uninstall** screen. Record only relevant content names and displayed installed states, with account details omitted. [Steam's DLC guide](https://help.steampowered.com/en/faqs/view/6948-51E6-E624-8A84) and [EA's content instructions](https://help.ea.com/en/articles/battlefield/battlefield-6/error-codes/) describe those screens.

Valve documents distinct semantics for [ISteamApps](https://partner.steamgames.com/doc/api/ISteamApps): a base application may be installed without being owned; `BIsDlcInstalled` combines DLC ownership and installed state. None of these facts reveals a game engine's private CAS-table result. A separate helper must not pretend that installed manifests, a loaded Steam DLL or its own API context proves what the game observed. The helper does not request auth tickets, licence files, account credentials or injected ownership calls.

The strongest result would be a vendor-supported diagnostic event or a sufficiently complete, privately handled crash artifact directly connecting package admission to the failing lookup. Until that exists, an entitlement cause remains a hypothesis.
