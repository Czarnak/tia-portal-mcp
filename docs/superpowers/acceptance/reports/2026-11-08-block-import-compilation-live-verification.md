# Block import and compilation — scoped live verification

Date: 2026-11-08. Issue: [#221](https://github.com/Czarnak/tia-portal-mcp/issues/221), compilation follow-up.

## Candidate and scope

Implementation commit `0dd0089f` on `fix/issue-221-import-compile-results`, based on local main `5af42c14`.
The worktree host and net48 worker were built against installed TIA Portal V21. No installed server was replaced.

- Project: `C:/Users/LCZ/Desktop/RnD/TIA-Portal/DisposableProjects/MixOfEverything/MixOfEverything.ap21`.
- Portal PID: `43340`; CPU 1516-3 PN/DP, `6ES7 516-3AP03-0AB0`, firmware V4.1.
- Exact project path and PID were asserted before fixture creation.
- Host DLL SHA-256: `ab0ba41e8a0753952276d708d19dc558de8e8b1f00de76d05110ef4a9f5e4dcc`.
- Worker EXE SHA-256: `2bf321fe980bc9613cbb2169b1ce789d74c0318f5c2021e5c1b10f59dc59284b`.

## Behavior

`update_block_logic` still imports, compiles, and freshly verifies the target. Its successful import/final read no longer requires successful compilation. The existing `result.importOutcome.compileStage` and `compileReport` carry compilation independently. Observed lost-session evidence still fails as `portal_lost`, retaining committed-import evidence.

## Native check

An isolated tag table, a root GlobalDB target, and an unrelated root SCL FB were created with unique `Issue221C` names. The FB first referenced an existing Boolean tag; deleting that owned tag then produced a genuine PLC compilation error. The target DB import and a subsequent tag creation were submitted together in one public `plc_write` call.

| Observation | Result |
| --- | --- |
| PLC compile before target import | 1 error from the unrelated FB |
| Target DB source import | `succeeded`; `importResultState:success`; `targetMutationCommitted:true` |
| Compilation in the import outcome | `compileStage:failed`; 1 error retained |
| Following tag creation in the same call | `succeeded`; independently read back from its tag table |
| Write verification | Success with a new `contentHash`, equal to the next source read |
| Imported target content | Added `ProbeValue` member present in the fresh source export |
| Target-only compile | Success, 0 errors |
| Cleanup | Both owned blocks and the owned tag table freshly read as `target_not_found` |
| PLC compile after cleanup | Success, 0 errors |

All final assertions passed and the probe exited 0. The disposable project was left open and was never saved.

The temporary probe needed two setup corrections: optional arguments for the reused timing runner, and insertion of a `VAR` section when an empty DB source omits one. The first live attempt stopped before the target import and deleted all its owned fixtures before the successful rerun.

MCP traces and temporary fixture scripts were not promoted into durable evidence.

## Offline verification

- RED: 5 expected regression failures, 39 passes.
- GREEN: the same focused verifier/coordinator/batch cases passed 44/44.
- Full non-packaging suite: 8,404/8,404, 0 failures, 0 skipped.
- Installed V21 serial solution build: 0 errors, 8 warnings.
- Independent source review: no actionable findings.

## Limits

The native check used one CPU and a source-format GlobalDB target at PLC root with an unrelated root FB error. XML/source host result projection, unavailable compilation, final-read failures, and fatal-session preservation were verified offline. Other CPU families, bound local sessions, and native fatal-loss injection were not exercised. Diagnostic sanitization remains; `compile_check` supplies detailed messages. PLC type imports and block creation retain their separate contracts.
