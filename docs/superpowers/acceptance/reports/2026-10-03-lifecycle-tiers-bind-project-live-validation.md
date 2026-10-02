# Lifecycle tiers and project binding live validation

**Run date:** 2026-10-03 (Europe/Warsaw; raw UTC captures fall on 2026-10-02).
**Status:** Full-mode group PASS. Task 13 remains open for read-write and read-only acceptance.

This report records the full-mode subset of [Task 13](../../plans/2026-10-02-lifecycle-tiers-and-project-binding.md). The maintainer requested grouping by access mode and explicitly authorized full-mode live testing on two running disposable projects. Read-write/read-only execution and Task 14 documentation changes were deferred.

## Candidate and provenance

- Branch/source candidate: `feat/bind-project`, `3bb504b4647cdd57677323eb44aac3305a33aafd`.
- Installed version: `3.0.1-local.143.g3bb504b`.
- The native host and worker command lines both specified `--access-mode full`.
- Installed host SHA256: `A527934E4562C0A07BAE921593C762BDAE918E0C0CF2418EE3ADD58562DAB2E8`.
- Installed worker SHA256: `5E87EB50D2530DA3EB1BF7F77C1F32A8EC1C2053C1603BBC01A85B76EAFD021E`.
- Both installed hashes matched the corresponding local Release binaries.
- Live discovery returned 15 tools. All six lifecycle tools advertised structured outputs and `dryRun`, without public `acknowledge`, `confirm`, or `safetyToken`.
- Source files were unchanged throughout this run.

Exact paths, project/tag contents, raw client captures, audit records, and hashes remain in ignored local evidence:
`.superpowers/sdd/2026-10-02-lifecycle-tiers-and-project-binding/live-2026-10-03-full/`.
Created projects and the archive remain under ignored `artifacts/lifecycle-bind/full-20261003-3bb504b/`.
The principal records are `provenance-restoration.json`, `contract-audit-artifact-summary.json`,
`audit-matched-records.json`, `host-exit-probe.json`, and the numbered tool captures.

## Binding and Portal selection

Fixture A initially ran in UI Portal PID `21600`; Fixture B ran in UI Portal PID `29956`.
Both were open and unmodified.

| Case | Observed result |
| --- | --- |
| Initial unbound `bind_project` without a path | Typed `target_ambiguous`, both Portals advertised, binding remained unbound |
| Bind A by its advertised path | Verified binding to PID 21600; status reported the same project path |
| Bind B without force | Top-level `binding_conflict`, null result |
| Bind B with force | Verified switch to PID 29956; previous binding identified A |
| Return to A | Verified switch; A remained open and clean |
| Cached A tree cursor while bound to B | `cursor_binding_mismatch` |
| Old A cursor after returning to the original A path | `cursor_binding_mismatch`; returning to a path did not revive an old binding epoch |
| Repeated no-path bind while verified on A | Successful `unchanged` transition, with no new inventory requirement |

The advertised `TiaPortalProcess.ProjectPath` values matched the primary projects observed through
verified status and the TIA window titles. This run did not test secondary-project or Multiuser path reporting.

The maintainer reported that the Openness dialog appeared only on the first binding and that
they selected “Accept all.” Later server warnings that a dialog *may* appear do not establish
that another dialog was shown. Openness access and lifecycle elicitation are separate mechanisms.

## Full-mode lifecycle matrix

An isolated copy of A (Fixture C) and a newly created disposable project (Fixture D) were used
for lifecycle mutation. The original B project was not mutated. Original A was reopened in its
original Portal after testing.

| Operation | Observed result |
| --- | --- |
| `open_project` | Successful worker-owned clean D-to-C switch; typed result and destination verification |
| `create_project` | Successful D creation after explicitly closing C |
| `save_project` | Successful C save; verified open and unmodified |
| `save_project_as` | Successful A-to-C copy; verified destination binding |
| `archive_project` | Successful clean C archive in `DiscardRestorableDataAndCompressed` mode |
| `close_project` | Successful clean close and modified discard close; verified no open project |

Every lifecycle tool had a successful dry-run preview as well as a successful apply.
All seven guards from the frozen lifecycle matrix were exercised:

| Guard | Observed result |
| --- | --- |
| `closes_source_project` | Informational guard on clean worker-owned switching, in preview and apply |
| `discards_unsaved_source_changes` | Dirty C preview reported the block guard; actual open was rejected with `guard_blocked` |
| `discards_unsaved_changes` | Dirty close preview and applied discard reported `acknowledged:true`; applied audit recorded `satisfiedBy:policy` |
| `archive_without_save` | Dirty preview and attempted archive reported the informational guard |
| `archive_discards_restorable_data` | Informational guard on preview and successful clean archive |
| `archive_inside_project_folder` | Preview exposed the block guard; actual archive was rejected and its output remained absent |
| `target_exists` | Actual create and save-as collisions both returned `guard_blocked` |

Dry runs returned previews even when they contained block guards. In full mode, the dirty-close
preview already had `acknowledged:true`; its audit still recorded `satisfiedBy:null` and
`confirmation:none/not_requested`. The actual discard recorded policy satisfaction.

TIA refused archiving dirty C with `saveBeforeArchive:false`. This was a typed attempted failure:
`success:false`, `error:null`, `isError:false`, `result.failure.category:worker_operation_failed`,
and `verification:null`. A fresh status read confirmed C was still modified, and the temporary
tag table was still present. No archive file appeared. The run then closed C explicitly without
saving; that call succeeded under full-mode policy and discarded the temporary change.

## Contracts, confirmation, and audit

The 27 lifecycle response documents comprised 10 previews, 13 applied attempts, and four block
refusals. Twelve applied attempts succeeded; the dirty archive attempt failed as described above.

- Every captured structured document equaled its decoded JSON text document.
- Exactly 27 audit v2 records matched the exact lifecycle response text with matching multiplicity.
- All 27 response SHA256 checks passed.
- Every matched record had `accessMode:full`.
- Fourteen records reported `confirmation.by:none`; thirteen reported `confirmation.by:policy`.
- Every record reported `confirmation.outcome:not_requested`; no lifecycle elicitation was requested.
- The applied dirty discard guard recorded `acknowledged:true` and `satisfiedBy:policy`.

The controlled auxiliary client advertised form elicitation capability. Its binding/status
host-exit probe received zero elicitation requests. The 27 lifecycle calls were made through
the native client, with no-prompt behavior established by their matching server audit records.

## Artifacts and restoration

| Artifact/check | Observed result |
| --- | --- |
| Created D project file | 151,383 bytes; SHA256 retained locally |
| Copied C project file | 151,377 bytes; SHA256 retained locally |
| Clean compressed archive | 2,346,387 bytes; 21 ZIP entries; all entry CRC checks passed |
| Forbidden archive inside C | Absent |
| Dirty archive attempted without saving | Absent |
| C after modified discard | Saved-file SHA256 exactly matched the pre-change baseline |
| C tag tables after reopening | Temporary table absent; complete captured PLC tag-table contents matched the baseline |
| Original A after restoration | Open and clean in PID 21600; closed-file hash unchanged before reopening |
| Original B after restoration/detach | Open and clean in PID 29956; observed baseline metadata unchanged |
| Both original TIA windows | Responsive, with original project window titles |
| Final native session binding | Verified on A |

A temporary tag table was created only in C through the existing preview/apply batch flow to
make the project modified. It was removed by the tested discard, without saving that change.
The saved C hash before and after discard was
`732B9B05D50EDE9C42F5013831E7811AFECD1C17ABA323972CF3F2045DA110B4`.
Original A's closed-file hash before restoration was unchanged from the baseline:
`D6131194C5CDF36B799C21A320909D4A6E3EAC6D90B2838AE950A87C717069A4`.

## Graceful host exit

A separately controlled full-mode host bound already-open B, read its clean status, and then
received EOF on stdin. Its worker exited with code 0 within **67.58 ms**, below the two-second
grace. The host exited with code 0 within **86.02 ms**. The captured stderr recorded normal shutdown.
A fresh native bind/status read afterward confirmed B remained open and clean with baseline
metadata unchanged. No forced termination was needed.

## Remaining acceptance

The full-mode group identified no production fix requirement. Driver corrections and the
original captures are retained in `test-driver-corrections.md`; they were not treated as passing
assertions or erased, and fresh status checks preceded continuation after unexpected assertions.

Task 13 is not complete. Remaining groups include every-mode binding/discovery, read-only tree
browsing, the read-write write-after-bind and full prompted lifecycle matrix (including decline,
block/dry-run no-prompt behavior and audit provenance), and refusal of implicit opening through a
read with an unopened path. Task 14 current documentation/spec updates remain deferred.

This run does not qualify headless detach guards, secondary-project or Multiuser behavior,
forced crash/timeout/disconnect, archive retrieval, PLC compile/download/runtime control,
publication, or plant acceptance. ZIP integrity is not Siemens archive-retrieve acceptance.
