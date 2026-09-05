# PR 4 Structured Preview Diff Live Acceptance

**PREVIEW PASSED — the dated Preview run below completed through the real host MCP protocol and
TIA Portal V21. Apply remains pending and requires separate authorization.**
The guarded harness is [live-test-preview-write-diff.ps1](../../../../scripts/live-test-preview-write-diff.ps1).
Ordinary source contract tests do not invoke the live harness body; isolated helper checks parse
and execute only the selected functions. The original pre-run scaffold remains below for provenance:
its pending statements describe the boundary before the dated run, while the appended findings are
the live Preview evidence.

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
