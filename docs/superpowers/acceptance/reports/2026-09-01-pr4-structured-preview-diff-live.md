# PR 4 Structured Preview Diff Live Acceptance

**LIVE ACCEPTANCE COMPLETE — dated Preview and Apply/restore/compile runs passed through the real
host MCP protocol and TIA Portal V21 on the disposable project described below.**
The guarded harness is [live-test-preview-write-diff.ps1](../../../../scripts/live-test-preview-write-diff.ps1).
Ordinary source contract tests do not invoke the live harness body; isolated helper checks parse
and execute only the selected functions. The original pre-run scaffold remains below for provenance:
its pending statements describe the boundary before the dated run, while the appended findings are
the live acceptance evidence. The failed first Apply attempt remains part of that evidence.

## Recovery and Target Selection

- The first Apply attempt against `PLC_LAD/Types/AnalogInputSettings` failed only its final
  byte-identity check because TIA canonicalized `T#1s` to `T#1S`; all 21 CRLF lines otherwise matched.
- **Cleanup/recovery only, not PR4 feature acceptance:** guarded `close_project(saveBeforeClose=false)`
  followed by guarded `open_project` recovered the exact clean disk state with `isModified=False`.
- Read-only source selection chose `PLC_LAD/Types/HMI_COUNTERS_UDTs/UDT_WORK_CNT`. Preview passed;
  the subsequent Apply passed both temporary operations, both restoration operations, byte-identical
  public re-exports, and `compile_check` with zero errors, zero warnings, and two PLCs.
- **Cleanup/recovery only, not PR4 feature acceptance:** compile left `isModified=True`, so a second
  guarded no-save close/reopen returned the same project to `isModified=False`.

## Environment

- Date: Pending live run.
- TIA Portal version: V21 required; not observed by this task.
- Host build: Pending exact artifact path and SHA256.
- Disposable project path: Pending separately authorized target.
- Binding verification: Pending exact project and worker/session/Portal identity.
- Block target: Pending source-exportable disposable block.
- Type target: Pending source-exportable disposable type.

## Preview-Only Evidence

- Block preview: Pending real source change and structured diff.
- Type preview: Pending real source change and structured diff.
- Line-ending-only preview: Pending rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true.
- Oversized batch preview: Pending per-line, per-side and deterministic request-order whole-batch truncation.

## Apply / Restore / Compile

- Apply authorization: Not granted by this report; explicit Apply/AllowApply and interactive confirmation or authorized CI bypass required.
- Applied changes: Not run.
- Restore result: Not run.
- Byte-identical re-read: Not run; compares UTF-8 bytes of exported source text, not the project file.
- Compile result: Not run; zero errors required. The harness does not save the project.

## Evidence Boundary

- Proven: Prepared harness source and execution-free contract test coverage only; implementation test evidence belongs in the implementation task report.
- Not proven: PowerShell parsing or runtime behavior, host startup, live MCP/TIA reads or previews, apply/restore/compile, project-file byte identity, saved state, plant or production acceptance. All live gates remain pending.

## Run 2026-09-05T15:13:41.3889267Z (Preview)

### Environment

- Date: 2026-09-05T15:13:41.3889267Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity fc8d88025e684f168d671bfd23d3eab6/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/AnalogInputSettings
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-4a121df936384e6a8b0ac3e3370a7ea6

### Preview-Only Evidence

- Block preview: PASS: source content change with structured diff
- Type preview: PASS: source content change with structured diff
- Line-ending-only preview: PASS: rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true
- Oversized batch preview: PASS: 512-character line cap; 40-line/8192-character side caps; deterministic exhaustion at zero-based index 3 including later small request

### Apply / Restore / Compile

- Apply authorization: No apply authorized by Preview mode
- Applied changes: NOT RUN
- Restore result: NOT RUN
- Byte-identical re-read: NOT RUN
- Compile result: NOT RUN
- Final state: Same verified session; project isModified=False. No save performed.

### Evidence Boundary

- Outcome: PASS for Preview mode only
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence of replacements. Preview alone cannot qualify apply/restore/compile.

## Run 2026-09-05T15:34:00.0925519Z (Preview)

### Environment

- Date: 2026-09-05T15:34:00.0925519Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity 4e5a5208a2274fbf9480dc6d4286bc8f/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/AnalogInputSettings
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-39eed15de86841b7aa12361196a8df54

### Preview-Only Evidence

- Block preview: PASS: source content change with structured diff
- Type preview: PASS: source content change with structured diff
- Line-ending-only preview: PASS: rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true
- Oversized batch preview: PASS: 512-character line cap; 40-line/8192-character side caps; deterministic exhaustion at zero-based index 3 including later small request

### Apply / Restore / Compile

- Apply authorization: No apply authorized by Preview mode
- Applied changes: NOT RUN
- Restore result: NOT RUN
- Byte-identical re-read: NOT RUN
- Compile result: NOT RUN
- Final state: Same verified session; project isModified=False. No save performed.

### Evidence Boundary

- Outcome: PASS for Preview mode only
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence of replacements. Preview alone cannot qualify apply/restore/compile.

## Run 2026-09-05T15:34:36.0127998Z (Apply)

### Environment

- Date: 2026-09-05T15:34:36.0127998Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity faa274f50b844c4e89a2520415d72e47/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/AnalogInputSettings
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-a64709ed5ade421caa1aa4f6f7fcb235

### Preview-Only Evidence

- Block preview: NOT RUN
- Type preview: NOT RUN
- Line-ending-only preview: NOT RUN
- Oversized batch preview: NOT RUN

### Apply / Restore / Compile

- Apply authorization: Explicit -AllowApply and interactive YES
- Applied changes: PASS: both operations reported success; restoring original text next
- Restore result: PASS: both original source replacements reported success
- Byte-identical re-read: NOT RUN
- Compile result: NOT RUN
- Final state: NOT READ

### Evidence Boundary

- Outcome: FAILED: Target 1 failed byte-identical restoration.
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence beyond the exact exported-text checks performed.

## Run 2026-09-05T15:36:47.1841594Z (Preview)

### Environment

- Date: 2026-09-05T15:36:47.1841594Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity ff64b915d5b94d5ea109be92fba4262e/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/AnalogInputSettings
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-298646933e5a409ca2455c3d452b863c

### Preview-Only Evidence

- Block preview: PASS: source content change with structured diff
- Type preview: PASS: source content change with structured diff
- Line-ending-only preview: PASS: rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true
- Oversized batch preview: PASS: 512-character line cap; 40-line/8192-character side caps; deterministic exhaustion at zero-based index 3 including later small request

### Apply / Restore / Compile

- Apply authorization: No apply authorized by Preview mode
- Applied changes: NOT RUN
- Restore result: NOT RUN
- Byte-identical re-read: NOT RUN
- Compile result: NOT RUN
- Final state: Same verified session; project isModified=True. No save performed.

### Evidence Boundary

- Outcome: PASS for Preview mode only
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence of replacements. Preview alone cannot qualify apply/restore/compile.

## Run 2026-09-05T15:45:36.7944391Z (Preview)

### Environment

- Date: 2026-09-05T15:45:36.7944391Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity 84467691db48449a9582d9289d1bbe1e/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/HMI_COUNTERS_UDTs/UDT_WORK_CNT
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-ad168a023cab4c6ea2b4c9e8d93d32d5

### Preview-Only Evidence

- Block preview: PASS: source content change with structured diff
- Type preview: PASS: source content change with structured diff
- Line-ending-only preview: PASS: rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true
- Oversized batch preview: PASS: 512-character line cap; 40-line/8192-character side caps; deterministic exhaustion at zero-based index 3 including later small request

### Apply / Restore / Compile

- Apply authorization: No apply authorized by Preview mode
- Applied changes: NOT RUN
- Restore result: NOT RUN
- Byte-identical re-read: NOT RUN
- Compile result: NOT RUN
- Final state: Same verified session; project isModified=False. No save performed.

### Evidence Boundary

- Outcome: PASS for Preview mode only
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence of replacements. Preview alone cannot qualify apply/restore/compile.

## Run 2026-09-05T15:45:58.8698473Z (Apply)

### Environment

- Date: 2026-09-05T15:45:58.8698473Z
- TIA Portal version: V21 prerequisite; project version not reported
- Host build: C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll; SHA256=F02D34DACCF120C63B29DC9A005BD74116D773ADB1C02B9A6017B3DD9FA8A51E
- Disposable project path: C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21
- Binding verification: PASS: exact project path and worker/session/Portal identity a37ce5e1aa784019809aa49e7d3cdf7e/2/54096
- Block target: PLC_LAD/Blocks/100_Inputs/InputValues_DB
- Type target: PLC_LAD/Types/HMI_COUNTERS_UDTs/UDT_WORK_CNT
- Local evidence and original sources: C:\Users\LCZ\AppData\Local\Temp\tia-preview-diff-104323d1e9e543ccb83281b6d63a7e68

### Preview-Only Evidence

- Block preview: NOT RUN
- Type preview: NOT RUN
- Line-ending-only preview: NOT RUN
- Oversized batch preview: NOT RUN

### Apply / Restore / Compile

- Apply authorization: Explicit -AllowApply and interactive YES
- Applied changes: PASS: both operations reported success; restoring original text next
- Restore result: PASS: both original source replacements reported success
- Byte-identical re-read: PASS: byte-identical UTF-8 exported text; block SHA256=ef7aff2943a83a5c18e3f02db5da9cda5998db0d0cbc35fc14afca8fa5c759ca; type SHA256=3a6de800e7e82c19e9c134578984decba4e4b2edcca51d1a0cc3e1a0da954ba4
- Compile result: PASS: zero errors; warnings=0; PLCs=2
- Final state: Same verified session; project isModified=True. No save performed.

### Evidence Boundary

- Outcome: PASS for Apply mode only
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence beyond the exact exported-text checks performed.
