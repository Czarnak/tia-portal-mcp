# PR 4 Structured Preview Diff Live Acceptance

**PENDING — no live execution has been authorized or performed for this report.**
The guarded harness is [live-test-preview-write-diff.ps1](../../../../scripts/live-test-preview-write-diff.ps1).
Its source contract tests read text only. Neither the harness nor a PowerShell parser was run
as part of implementation. A future separately authorized run appends its dated findings below;
this pending template is not evidence of a live pass.

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
