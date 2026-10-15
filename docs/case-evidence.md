# A Rapid Royale loading crash: the evidence

This anonymized case motivated BF6 Crash Helper. These observations describe one investigated Windows/Steam installation on **15 October 2026**. They are not a universal Battlefield diagnosis or a confirmed workaround.

## Observed behavior

The player used free REDSEC. Ordinary Battle Royale rounds worked, while Rapid Royale Solo and Trios could crash during a join or the next round's loading. Some Rapid attempts succeeded too. A cache reset was followed by successful play, then the same crash returned.

Steam's normal verification completed successfully. Additional local checks compared the relevant installed metadata and the specifically requested CAS file against active Steam manifests. They matched. No raw dumps, ETL files, executable code or personal absolute paths are included here.

## Exact signature and version boundary

The investigated game build was **25684022**. Its `bf6.exe` PE metadata was:

```text
TimeDateStamp: 0x6abf2953
SizeOfImage:  0x0c305000
File size:    198221160 bytes

Exception:   0xc0000005, read from 0x00000000000000a7
Fault RVA:   bf6.exe + 0x051ed625
```

A PE timestamp is a binary header field, not a reliable date of the user's crash. An RVA is relative to that executable's loaded base and is not a portable function name. Check binary identity before assigning this case's signature. Matching metadata is a recognition condition, not proof of an identical underlying cause; different or unavailable identity must remain unknown.

The helper's own dump reader now checks this signature before attempting an optional CAS-request extraction from captured registers and stack data. It reports missing bytes or inconsistent records as unknown rather than repeating the identifiers below for every matching crash. [Extraction requirements](deeper-analysis.md#what-the-helpers-own-dump-parser-can-recover).

## The CAS request

The value initially seen in the failing lookup was:

```text
CAS identifier:               0x0000be29da23000b
Persistent install chunk ID:  0xbe29da23
CAS index:                   11
Patch flag:                  0

Requested byte offset:       527653320 (0x1f7359c8)
Requested byte length:       13606610  (0x00cf9ed2)
```

The identifier format agrees with the public Frosty SDK's [CasFileIdentifier implementation](https://github.com/FrostyToolsuite/FrostyToolsuite/blob/master/FrostySdk/Managers/Infos/CasFileIdentifier.cs). The local `layout.toc` and one exact offset/length record in the SAN2 `mp_tungsten` TOC connected that request to:

```text
Update\BF6_SAN2_DLC\Data\Win32\installation\san2installpackage\cas_11.cas
```

The file had **584232960 bytes** and SHA-1 **`3c80ca0c179b7faf130988465d5aa40d2fafc19a`**, matching the active Steam depot **2807963**, manifest **1604024018245943940**. The request was inside the file's bounds. These are artifact-specific findings, not hashes that every future game build should match.

The layout associated this installation package with content app **3348440**, named **[Battlefield™ 6 Multiplayer by Steam Support](https://help.steampowered.com/en/wizard/HelpWithGame/?appid=3348440)**. It marked the package optional and non-HD. An internal licence field is metadata; its presence is not evidence of a denied licence check.

## What was still missing

Offline instructions and recorded registers pointed to a failed internal CAS lookup before the requested file segment was read. The dump did not contain the owning runtime table's required heap bytes. The observed ETW window showed relevant TOC/digest access, but no opening of this particular CAS file before that crash. That observation does not prove that the process never accessed the file at an earlier time.

The evidence therefore supports investigating runtime content registration, lookup-table construction and package lifecycle. It does **not** establish which condition removed, omitted or invalidated the entry. A concurrency issue, content-binding mistake and entitlement-dependent branch remain hypotheses until directly supported.

In particular, this is not a demonstrated decryption-key failure. The packed value is a CAS file identifier. Nearby crypto-related strings belonged to another branch and did not establish the cause of this request.

## Official access and relevant comparisons

[EA's REDSEC page](https://www.ea.com/games/battlefield/redsec) presents Rapid Royale as a REDSEC mode with Solo/Trios play and names Vegas Strip and Mirak Valley. The [Season 5 patch notes](https://www.ea.com/de/games/battlefield/redsec/news/battlefield-6-game-update-2-1-1-0) describe its introduction. A paid Battlefield purchase is not a proposed remedy.

EA documents the game's **Settings → System → Setup → Install/Uninstall → Edit** screen for checking installed content. Its article describes an older “multiplayer not installed / purchase to play” issue as fixed; it does not identify this current crash as the same problem. [EA's content-management instructions](https://help.ea.com/en/articles/battlefield/battlefield-6/error-codes/).

A [first-person October report](https://www.reddit.com/r/GeForceNOW/comments/1x4xvcj/comment/pglhji5/) described new Solo/Trios crashes associated with a “Construction Site” map. Another [first-person comment](https://www.reddit.com/r/GeForceNOW/comments/1x4xvcj/comment/pgky1xy/) described playing once and crashing on a subsequent entry. Those reports came from GeForce NOW, contain no matching dump signature and do not prove the same defect or a fix. They are comparisons, not confirmation.

## What would improve the diagnosis

A useful next finding would connect the crash's table entry to the runtime decision that populated or removed it. More repetitions of the same truncated dump cannot recover bytes that were never recorded. [Deeper analysis options and their limits](deeper-analysis.md).
