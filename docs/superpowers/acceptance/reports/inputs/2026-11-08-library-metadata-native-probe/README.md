# Native library metadata probe - PR A, Task 0

Standalone net48/x64 console for A1-A4 in section 6 of the library-metadata design.
Calls installed Siemens V21 Openness directly; no MCP server or PLC project is needed.
Results: [2026-11-08 native probe report](../../2026-11-08-library-metadata-native-probes.md).
Run 1's 10,000-character author crashed the disposable Portal. That input is now excluded from
the console; its recorded result is retained as evidence, not repeated. Run 2 completed the
multiline-author case and repeated A1-A3 successfully.

From this directory:

```powershell
dotnet build NativeLibraryMetadataProbe.csproj -m:1
.\bin\Debug\net48\NativeLibraryMetadataProbe.exe --self-test
.\bin\Debug\net48\NativeLibraryMetadataProbe.exe 'C:\Temp\LibraryMetadataProbe-A1'
```

The live command requires a new absolute directory. It starts its own headless Portal session;
approve the Siemens Openness access dialog if shown. It never attaches to an existing Portal.
Created libraries and `results.jsonl` stay in that directory for inspection; nothing is deleted.
The probe closes its user libraries and disposes its own Portal session on normal completion.

| Case | Native observation |
| --- | --- |
| A1 | `IEngineeringComposition.Create(typeof(UserGlobalLibrary), Name/TargetDirectory/Author)`, without `Comment`; returned CLR type and exact author. After native refusal only, try `typeof(GlobalLibrary)` with a different name. |
| A2 | Typed `Create<UserGlobalLibrary>(directory, name)`; author Portal chooses when omitted. |
| A3 | Every metadata member on a fresh library, then after saving/closing and reopening read-only; first installed system library if Portal reports one. |
| A4 | Run 1 recorded the fatal 10,000-character author. The current console creates only the LF/CRLF author, recording returned text, lengths and ordinal equality. Uses the type selected by A1. |

JSONL records each call, return and exception chain. Metadata getters are independent, preserve
nulls, and include all comment, language, product and history entries without truncation; array
lengths give the counts. A missing system library is explicitly skipped. A fatal/disposed Portal
stops the run; there is no automatic recovery. A failed create can leave files: inspect the work
directory before any retry, and always use a new directory for the next run.

Exit 1 means an unexpected case or fatal failure; native refusals in A4 and unavailable metadata
members are observations. Exit 0 does not approve PR A: review the records against the design and
bring any contradiction back to the owner before implementation. Results can contain the default
author and other native user names; review them before sharing or committing.

`--self-test` checks work-directory guards and exact author/null JSON round trips without starting
Portal. Build uses the installed V21 API, not the incomplete reference stubs. No PR B probes,
product changes, fixture-copy framework or test dependency are included.
