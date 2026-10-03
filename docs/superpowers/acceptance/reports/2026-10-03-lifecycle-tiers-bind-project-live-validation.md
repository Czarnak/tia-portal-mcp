# Lifecycle tiers and project binding: full and read-write live validation

**Run date:** 2026-10-03 (Europe/Warsaw; full-mode raw UTC captures fall on 2026-10-02).
**Status:** Full-mode and read-write groups PASS. Task 13 remains open for read-only acceptance.

This report records the full-mode and read-write groups of [Task 13](../../plans/2026-10-02-lifecycle-tiers-and-project-binding.md). The maintainer requested grouping by access mode and explicitly authorized each group on two running disposable projects. Read-only execution and Task 14 documentation changes remain deferred.

## Candidate and provenance

- Branch/source candidate: `feat/bind-project`, `3bb504b4647cdd57677323eb44aac3305a33aafd`.
- Installed version: `3.0.1-local.143.g3bb504b`.
- The native host and worker command lines specified `--access-mode full` for the first group and `--access-mode read-write` after the maintainer restarted the native session for the second group.
- Installed host SHA256: `A527934E4562C0A07BAE921593C762BDAE918E0C0CF2418EE3ADD58562DAB2E8`.
- Installed worker SHA256: `5E87EB50D2530DA3EB1BF7F77C1F32A8EC1C2053C1603BBC01A85B76EAFD021E`.
- Both installed hashes matched the corresponding local Release binaries.
- Full-mode live discovery returned 15 tools. All six lifecycle tools advertised structured outputs and `dryRun`, without public `acknowledge`, `confirm`, or `safetyToken`.
- Source files were unchanged throughout both groups; installed and local Release binary hashes matched again for read-write.

Exact paths, project/tag contents, raw client captures, audit records, and hashes remain in ignored local evidence:
`.superpowers/sdd/2026-10-02-lifecycle-tiers-and-project-binding/live-2026-10-03-full/`.
Created projects and the archive remain under ignored `artifacts/lifecycle-bind/full-20261003-3bb504b/`.
The principal records are `provenance-restoration.json`, `contract-audit-artifact-summary.json`,
`audit-matched-records.json`, `host-exit-probe.json`, and the numbered tool captures.

## Full-mode binding and Portal selection

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

## Full-mode contracts, confirmation, and audit

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

## Full-mode artifacts and restoration

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

## Full-mode graceful host exit

A separately controlled full-mode host bound already-open B, read its clean status, and then
received EOF on stdin. Its worker exited with code 0 within **67.58 ms**, below the two-second
grace. The host exited with code 0 within **86.02 ms**. The captured stderr recorded normal shutdown.
A fresh native bind/status read afterward confirmed B remained open and clean with baseline
metadata unchanged. No forced termination was needed.

## Read-write binding and lifecycle matrix

Read-write captures, audit records, client summaries, binary/runtime provenance, and restoration
comparisons remain in ignored local evidence:
`.superpowers/sdd/2026-10-02-lifecycle-tiers-and-project-binding/live-2026-10-03-readwrite/`.
Its principal records are `contract-audit-artifact-summary.json`, `restoration-summary.json`,
`user-prompt-observation.md`, `runtime-final-observation.json`, and the numbered tool captures.
Artifacts remain under ignored `artifacts/lifecycle-bind/readwrite-20261003-3bb504b/`.
The retained evidence verifier completed with exit code 0.

The fresh native session was initially unbound. No-path binding returned `target_ambiguous`
with both original Portals and their primary project paths. Binding A verified PID 21600;
binding B without force returned `binding_conflict`; forcing the switch verified PID 29956.
Returning to A succeeded, followed by a preview and applied save in read-write. The maintainer
observed the MCP save confirmation form and reported no TIA Openness dialog during this group's
binding or switching. Controlled-client discovery also returned 15 tools.

An isolated read-write copy of A (Fixture E) and a newly created project (Fixture F) kept this
group's mutations separate from full-mode Fixtures C/D. Original B was not mutated.

| Operation | Observed read-write result |
| --- | --- |
| `open_project` | Clean F-to-E switch succeeded with typed destination verification; restoration opens also succeeded |
| `create_project` | F was created after explicit close; create while E was open returned the expected typed Siemens failure |
| `save_project` | A save succeeded and verified open/clean |
| `save_project_as` | A-to-E copy succeeded and verified destination binding |
| `archive_project` | Clean E archive succeeded in `DiscardRestorableDataAndCompressed` mode; dirty archive without saving returned the expected typed Siemens failure |
| `close_project` | Clean close and confirmed dirty discard succeeded with no-open-project verification |

Every lifecycle tool had a successful dry-run preview and a successful applied call.
The two attempted failures had `success:false`, `error:null`, `isError:false`,
`result.failure.category:worker_operation_failed`, and `verification:null`. Fresh status and
artifact/table inspection followed each failure before continuation. A native `Test-Path`
immediately after status capture 18 returned `False` for the failed-create destination from
capture 17. The retrospective command-result observation is retained in
`create-attempt-artifact-observation.json`. Successful creation in capture 28 later used that
same path for F. Failed dirty archive produced no archive and left E modified with its
temporary table present.

All seven frozen guards were exercised again:

| Guard | Observed read-write result |
| --- | --- |
| `closes_source_project` | Informational guard on clean F-to-E switching, in preview and apply |
| `discards_unsaved_source_changes` | Dirty E preview exposed the block; actual open returned `guard_blocked` |
| `discards_unsaved_changes` | Dirty preview and declined close remained unacknowledged; confirmed discard recorded `acknowledged:true` and `satisfiedBy:user` |
| `archive_without_save` | Informational guard on dirty preview and attempted archive |
| `archive_discards_restorable_data` | Informational guard on preview and successful clean archive |
| `archive_inside_project_folder` | Preview exposed the block; actual archive returned `guard_blocked`, with forbidden output absent |
| `target_exists` | Actual create and save-as collisions returned `guard_blocked` |

While the selected Portal had no open project, `execute_read_batch` with `list_tag_tables` and
E's real unopened path returned per-operation `access_denied` and did not open E. The preceding
`get_project_status` call for that path correctly returned canonical `isOpen:false`. That
status response is valid no-project behavior; the driver was corrected to use the project-data
read for the refusal check, retaining both captures.

## Read-write confirmation and audit

The 42 lifecycle documents comprised 10 previews, 12 applied attempts, and 20 blocked responses.
Ten applied attempts succeeded and two returned the expected typed failures above. Exactly 42
audit v2 records matched the exact response text and multiplicity, with zero response-hash
failures. Every structured document equaled its decoded JSON text document, and the matched
audits recorded `accessMode:read-write`.

| Audit confirmation | Count | Covered cases |
| --- | ---: | --- |
| `none/not_requested` | 14 | Ten previews and four hard-block refusals; no elicitation |
| `user/confirmed` | 12 | Native applied attempts, including the two typed failures |
| `user/declined` | 9 | Six tool declines, save acceptance with false/missing `confirm`, and dirty-close decline |
| `user/cancelled` | 1 | Save cancellation |
| `user/unsupported` | 6 | One refusal per lifecycle tool without form elicitation capability |

The capable controlled client observed one elicitation request for each of six lifecycle
declines, save cancellation, and save acceptance with `confirm:false` or no `confirm` member:
nine requests total. A second controlled client observed one request for dirty-close decline.
These refusals returned `access_denied`; before/after state and destination checks showed no
mutation. Dirty E remained open/modified with the temporary table present after decline.
The noncapable client made six lifecycle attempts, received six `access_denied` refusals and
zero elicitation requests, and preserved state/artifacts.

Dirty-close preview and decline guards recorded `acknowledged:false` and `satisfiedBy:null`;
confirmed discard recorded `acknowledged:true` and `satisfiedBy:user`. Full-mode policy
provenance is covered by the preceding group. All supplemental client responses were
programmatic. Native audit confirmation establishes client-returned acceptance; only the
native save form has a separate recorded human observation. These records do not prove that
a human saw every form. No raw worker-dispatch trace was captured.

## Read-write artifacts, restoration, and host exit

| Artifact/check | Observed read-write result |
| --- | --- |
| Copied E project file | 151,382 bytes; SHA256 retained locally |
| Created F project file | 151,388 bytes; SHA256 retained locally |
| Clean compressed archive | 2,346,418 bytes; 21 ZIP entries; all entry CRC checks passed; SHA256 retained locally |
| Forbidden and failed archives at final inspection | Archive inside E and dirty archive absent |
| E after confirmed discard/reopen | Saved-file SHA256 unchanged; all four tag tables matched baseline contents exactly; temporary table absent |
| Original A | Open/clean in PID 21600; closed-file SHA256 matched baseline before reopening |
| Original B | Open/clean in PID 29956; baseline metadata preserved |
| Final native session | Verified binding to A; host and worker still in read-write |
| Final Portal observation | Both original windows responsive with original project titles |

The temporary table was created only in E through the existing batch preview/apply flow and
discarded without saving. E's closed-file hash before and after discard was
`61E1820F49305F4BF9489387B4E015592360C60F6412357EA601F1B49C2AC3EC`.
Original A's unchanged closed-file hash was
`D6131194C5CDF36B799C21A320909D4A6E3EAC6D90B2838AE950A87C717069A4`.

All three controlled read-write hosts received EOF and exited with code 0. Their workers
exited with code 0 within **57.51–84.40 ms**, below the two-second grace; host exits took
**72.14–110.03 ms**. Final process observation confirmed all six auxiliary host/worker PIDs
were absent, both original Portals remained responsive, and the native read-write session
remained running. No forced termination was needed.

## Remaining acceptance

Neither completed group identified a production fix requirement. Each evidence directory retains
`test-driver-corrections.md` and the original captures. Read-write corrections covered capture-helper
evaluation scope without an extra live call, the valid no-project status response, and a legacy
batch preview with a valid token but no `success` member. The preview assertion stopped before
mutation; the unchanged operation list was then applied once with that token. Expected typed
Siemens failures were inspected before continuation. These are driver corrections, not production
defects or plan gaps.

Task 13 is not complete. Read-only binding/discovery, tree browsing, and its host-exit observation
remain pending. Task 14 current documentation/spec updates remain deferred.

This run does not qualify headless detach guards, secondary-project or Multiuser behavior,
forced crash/timeout/disconnect, archive retrieval, PLC compile/download/runtime control,
publication, or plant acceptance. ZIP integrity is not Siemens archive-retrieve acceptance.
