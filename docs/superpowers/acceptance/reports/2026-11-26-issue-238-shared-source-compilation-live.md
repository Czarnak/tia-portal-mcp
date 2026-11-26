# Issue #238: shared source compilation live verification

Date: 2026-11-26. Result: **scoped PASS**, including fixture restoration.

The maintainer authorized live verification on an already-open disposable project after installing
the local branch build. All content calls used that installed MCP, in `full` mode, against one
explicit PLC software selector in a verified standalone-project binding. The target was an S7-1500
CPU 1516-3 PN/DP configuration in TIA Portal V21, product/file version `2100.0.121.1`.
Five owned SCL FCs, one owned block group and one owned tag table/tag isolated the mutations.
Real project names, paths and selectors are omitted; raw MCP receipts remain private outside the repository.

## Candidate provenance

Qualified source commit: `5788a8103ae9a76d934dabeba4abc1122a142db0`.
Installed host, contracts and worker all reported `4.4.0-local.39.g5788a810`.
Their SHA-256 fingerprints were checked before and after the live cases and were unchanged.
This qualifies the installed candidate, not a later rebuilt binary.

| Installed binary | SHA-256 |
| --- | --- |
| `TiaMcpServer.dll` | `4BAEA9BC5FB1574F6EE1ED6A6B87CEE58026465D6ED68B9F17C737DDC82AAE5C` |
| `TiaMcpServer.Contracts.dll` | `B6DB9E4D943855E9C53051361D1F40768723E628C5E62AE3ECE6AC4050512CBD` |
| `TiaMcpServer.OpennessWorker.exe` | `87113D5E2D87325C1600552678EB457E39437BA99582D2C3A547A89AC49FA139` |

The source candidate had already passed a serial solution build and all 9,054 non-packaging tests.
The full test run used the built test assembly after a project-runner restore stall. No product
source changed during live verification.

## Functional cases

| Case | Observed result |
| --- | --- |
| Two independent source updates | Both imports completed and reported committed mutations, `compileStage:"deferred"`, null per-item compile reports, successful fresh final reads, present targets and removed temporary sources. One shared PLC compilation succeeded with zero errors/warnings. Both returned hashes matched separate fresh reads. |
| Input rename with matching caller update | The callee input and caller named argument both changed from `Value` to `RenamedValue` in one call. Fresh source exports confirmed both edits. Both imports succeeded/deferred; one shared compilation succeeded with zero errors/warnings; both hashes matched fresh reads. |
| Renamed input with stale caller | The callee input changed to `StaleValue` alongside an independent source update; the caller was omitted from the request. Both requested imports and their verification succeeded, while the shared compilation failed with two errors and root `success` remained `true`. The compiler explicitly rejected formal parameter `RenamedValue` on the caller. Both requested-item hashes matched fresh reads. |
| Source generation fails at item 2 | A valid first update preceded an FC source with a missing header return type and a valid third update. Statuses were `succeeded`, `failed`, `skipped`; phase was `applied`, root success was false, root error was null and MCP `isError` was false. One successful shared compilation listed only the committed first operation, with zero errors/warnings. The failed item reported `worker_operation_failed`, import stage `unknown`, commitment null, compile stage `unavailable`, a successful final read, a present target and removed temporary source. Its final source hash and the skipped target's hash equaled their pre-call hashes. Both returned hashes, including the failed item's readback hash, matched fresh reads. Unknown commitment does not prove absence of transient mutation. |
| Source patch, source update and tag edit | All three items succeeded. The public patch operation remained `patch_block_logic`; only patch/update IDs appeared in the one successful shared compilation. Fresh source confirmed the replaced anchor and the independent edit; both returned hashes matched fresh reads. The tag retained `trusted` verification and its new address was confirmed by a separate tag-table read. |
| Five-source comparison | Five one-item calls each used `per-operation`, null `sharedCompilations`, and a successful per-item compiler observation. The same five source changes in one call produced five deferred imports and one successful shared compiler observation. All final hashes agreed between variants and with separate fresh reads. |

Every successful deferred import in these cases retained a successful final read, present target
and removed temporary source. The shared reports retained the explicit selector's PLC scope and
`blockPath:null`; only operation IDs with positive committed-import evidence were attributed to them.

Across all 17 content-write calls, including setup of the caller pair, source resets, recovery calls
and the additional native observations below, **35 returned content hashes** matched separate fresh
reads. Parsed text documents and structured documents agreed for all 17 calls. A successful
readback hash on a failed item reports its observed content; it does not turn the import into success.

## Timing method and result

This was one observational comparison on the same small, warmed fixture and installed candidate.
The workload added the same comments to five existing SCL FCs. The control used five one-item
`plc_write` calls. The sources were then reset, and all five starting hashes were checked against
the original control baseline before the same contents were submitted as one five-item call.
All five resulting hashes were identical between variants.

Elapsed write-call wall time was measured from tool invocation to response. Source-reset calls,
separate verification reads and receipt-file writes were excluded. The comparison includes RPC,
planning, import, compilation and in-call readback costs; it does not isolate compiler time or
compare two different product builds.

| Variant | Individual write-call durations | Total write-call time | Reported whole-PLC compilation observations |
| --- | --- | --- | --- |
| Five per-operation calls | 59.487, 55.812, 55.686, 56.496, 54.792 s | 282.273 s | Five per-item reports, all succeeded; detailed messages omitted, counts retained |
| One shared call | 236.012 s | 236.012 s | Five deferred per-item outcomes and one succeeded shared report |

The shared workflow took **46.261 s less (16.4%)** in this single comparison. This is not a
general performance claim for large projects. Compilation observations are public outcomes, not
an independently instrumented trace of every internal Siemens compiler invocation; source
generation itself was not deferred.

## Native observations and explicit recovery

The stale-caller case did not leave the unrequested caller byte-unchanged. A subsequent source
export contained both the old `RenamedValue := 1` argument and a native-added
`StaleValue := _int_in_` placeholder. Restoring the callee's input name alone still left two
compiler errors. A separate `compile_check` confirmed those errors. The caller was freshly read,
then explicitly rewritten from its previously verified valid source using the current hash.
Its fresh read contained the valid argument without the extra placeholder, its returned hash
matched, and a separate compile succeeded with zero errors/warnings. The receipts do not isolate
whether the native caller adaptation occurred during generation or compilation.

The first attempted generation-stop input used a malformed SCL body (`IF ;`) with a valid FC
header. Portal accepted all three imports as committed; the shared compiler reported three errors,
while root success remained true and all three hashes matched fresh reads. This was **not** counted
as an early-stop pass. The two affected sources were explicitly restored using fresh hashes;
their valid baseline hashes were recovered and compilation returned to zero errors before the
missing-return-type header probe in the functional table. No product implementation was changed
to reject source text that Portal accepted.

## Restoration

The initial project status was open and unmodified. Because the project file was locked while
open, it was closed without saving before fixture mutations, fingerprinted, and reopened.
The saved `.ap21` file SHA-256 was:

`564A644A3116FE60CBA9265E40E5A797FD68B57B0C954DFF7E3059C84D702A81`

After the cases, the modified project was closed with `saveBeforeClose:false` and reopened.
The project-file hash was unchanged; the original 12-node selected-PLC inventory matched exactly,
the original tag-table values matched, and the original Main XML export hash was unchanged:

`xml:sha256:f94beec10c771b559e64f79abd7b12edf69a23658339e38343fde14d9f85ef8a`

That verification checkpoint also compiled with zero errors/warnings, but its read/compile
sequence left the native modified flag set. Those verification-side changes were discarded in a
second close without saving. The project-file hash still matched, the same saved fixture was
reopened, and a status-only check confirmed **open and unmodified**. No further content export or
compile followed that final check. No project save, download or PLC control operation was used.

## Qualification limits

Live scope was full mode, one standalone PLC selector, SCL FCs and the isolated tag edit.
Multi-PLC and unqualified selector scopes, software/device alias deduplication, other block kinds
or languages, local sessions, cancellation, transport/Portal-loss skip paths and oversized report
omissions were not exercised live here. Their implementation evidence remains offline.
The report does not claim rollback, atomic project reads, or absence of transient mutation in
the generation-failure case.
