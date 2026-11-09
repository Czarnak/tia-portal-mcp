# Native library metadata probe - PR B, Task 0

Standalone net48/x64 console for B1-B10 in [the design, section 6](../../../../specs/2026-11-08-library-metadata-design.md).
Results: [2026-11-09 native probe report](../../2026-11-09-library-metadata-pr-b-native-probes.md).
Both runs completed; the read-only comment exception differs from the design's acceptance expectation.
Uses installed Siemens V21 Openness directly, following the PR A console pattern; no MCP server or PLC project is needed.

From this directory:

```powershell
dotnet build NativeLibraryMetadataPrBProbe.csproj -m:1
.\bin\Debug\net48\NativeLibraryMetadataPrBProbe.exe --self-test
.\bin\Debug\net48\NativeLibraryMetadataPrBProbe.exe 'C:\Temp\LibraryMetadataPrB-1'
# B8 runs separately because oversized metadata can crash Portal:
.\bin\Debug\net48\NativeLibraryMetadataPrBProbe.exe --long-comment 'C:\Temp\LibraryMetadataPrB-B8-1'
```

Each live command requires a new absolute directory and starts its own headless Portal.
Approve Siemens Openness access if shown. It never attaches to an existing Portal or opens an existing user library.
Each case creates a fresh library, saves its baseline, and records before/after metadata.
B6 reopens only its own fixture read-only. Libraries and autoflushed `results.jsonl` stay in the work directory;
nothing is deleted. Normal completion closes probe-owned libraries and disposes the probe's Portal.

| Case | Observation |
| --- | --- |
| B1 | Active comment set, ordinal read-back inside the uncommitted transaction and after disposal; clean baseline becomes modified. |
| B2 | Supported inactive language: comment item null or present; if present, attempt a write. |
| B3 | Clear a seeded comment; record item existence and text before and after commit. |
| B4 | Activate a supported inactive language; record its comment item and complete comment collection. |
| B5 | Set editing and reference languages separately, each with a fresh inactive-language fixture; record activation or native exception. |
| B6 | Read-only comment and language attempts: exact failing call, exception chain and unchanged attached metadata. |
| B7 | Set comment, activate a language, change editing/reference; record `CanCommit`, throw before commit, compare full metadata after rollback. |
| B8 | Separate run: 100,000-character comment; native acceptance/refusal, exact read-back if accepted. |
| B9 | `Languages.Find` for an unsupported specific culture selected from the installed .NET catalogue and neutral `en`: null, returned culture or native exception. |
| B10 | Exact LF/CRLF, whitespace, `<`, `&`, Polish and Japanese text inside the transaction and after commit. |

All edits use `ExclusiveAccess` plus `Transaction(library)`; save/open/close happen outside transactions.
JSONL records native calls, returns, exception types/chains, nulls and complete metadata without truncation.
B2/B5/B8/B9 native refusals are observations; setup errors, failed checks and unexpected exceptions exit 1.
Fatal/disposed Portal errors stop the run with no retry. Inspect partial files before another run and always use a new directory.
Exit 0 means the selected observations completed; it does not approve PR B. Review contradictions against the design with the owner before implementation.

`--self-test` checks path guards, exact text/null JSON round trips (including the B8 input) and snapshot comparison without starting Portal.
The standalone project builds against the installed V21 API; no product code, reference stubs or test dependency changes are required.
The live trace may contain native user names and local paths; review it before sharing or committing.
