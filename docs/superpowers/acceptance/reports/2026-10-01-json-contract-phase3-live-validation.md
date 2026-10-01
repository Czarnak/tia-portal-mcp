# JSON Contract Phase 3 / Write-Safety Phase 2 — Live Validation

Status: the authorized lifecycle acceptance matrix passed on the frozen installed candidate.
This record supplements the [offline qualification](2026-10-01-json-contract-phase3-offline-validation.md)
and [implementation plan](../../plans/2026-09-30-json-contract-phase3-lifecycle.md).
It records lifecycle behavior, client confirmation, persisted artifacts, and source restoration;
it does not establish PLC or plant acceptance.

## Candidate and authorization

- Repository/package candidate: `90a361bb24c9dc4d73d7c727260f8a2ff44474d1`.
- Installed package: `3.0.1-local.113.g90a361b`.
- Installed host SHA256: `91043E06C59B2EBE3D4E26225C0AB95276AC71CD7350AFEACDE2D19EE379E15A`.
- Production/test sources match the offline-qualified candidate
  `8f0e26f83db4faf6cd243fef7489f6419db3925b`; intervening commits changed documentation only.
- The maintainer authorized exact disposable targets for Fixture A (source), Fixture B (create),
  Fixture C (save-as), an archive destination, and one temporary tag table, with source restoration.
- An applied archive-inside-source refusal test received separate exact-target human authorization
  after automatic approval review initially rejected that call.

Exact project names, paths, raw source/tag contents, and client captures remain in ignored local
evidence under `priv/json-contract-phase3/live-evidence/`. The durable record uses fixture aliases.
The principal evidence files are `manifest.json`, `contract-summary.json`,
`unsupported-client.json`, and `audit-artifact-summary.json`.

## Captured calls and response contract

The manifest contains **55 primary captured calls**, including supporting reads, legacy batch
operations, and two retained client policy refusals. This is not a count of 55 successful lifecycle
operations. It contains 35 primary lifecycle response documents; two secondary unsupported-client
attempts add two server refusals and two audit records.

| Lifecycle response classification | Primary | Secondary | Total |
| --- | ---: | ---: | ---: |
| Successful applied response | 14 | 0 | 14 |
| Typed attempted-operation failure | 2 | 0 | 2 |
| Dry-run preview | 14 | 0 | 14 |
| Blocked server response | 5 | 2 | 7 |
| Total | 35 | 2 | 37 |

All 35 primary documents passed text/structured equality, complete-root, and truthful-verdict
checks. The contract summary also passed recursive ordinal property ordering and typed
result/verification value checks without nested JSON strings. Successful applied calls returned
successful typed verification. Dry runs returned previews without lifecycle mutation or elicitation,
including unacknowledged discard and hard-block guards.

## Runtime and guard matrix

| Requirement | Observed result |
| --- | --- |
| Open | Successful Fixture B to A switch with typed result and verification |
| Create | Successful Fixture B creation after explicitly closing the current project |
| Save | Successful Fixture A save and clean verification |
| Save-as | Successful Fixture C creation and destination verification/binding |
| Archive | Successful clean-source archive and source verification |
| Close | Successful clean close and accepted dirty discard; closed verification observed no open project |
| `closes_source_project` | Informational guard on a clean worker-owned source switch |
| `discards_unsaved_source_changes` | Dirty-source open blocked with `guard_blocked` |
| `discards_unsaved_changes` | Unacknowledged preview, actual decline, and accepted discard |
| `archive_without_save` | Informational guard on dirty-source preview and attempted archive |
| `archive_discards_restorable_data` | Informational guard on archive preview and successful archive |
| `archive_inside_project_folder` | Preview plus separately authorized actual `guard_blocked` refusal; forbidden archive absent |
| `target_exists` | Actual refusal for both create and save-as, including the approved Fixture C destination |

Hard-block refusals returned `isError:true` with null result and verification. The actual decline
returned `access_denied`; Fixture A remained open and dirty and the temporary tag table remained
present. Neither agent `acknowledge` nor a prior preview bypassed confirmation.

The unsupported-capability client used the same installed executable with capabilities `{}`.
Discovery returned 14 tools, including six lifecycle tools with the migrated schemas. The close
attempt returned `access_denied`, made zero elicitation requests, and left status exactly unchanged.
The corrected verifier completed with process exit code 0.

Two accepted discard guards were audited as `satisfiedBy:user`; none were satisfied by an agent.
The first prompt was accepted accidentally, which the maintainer confirmed in chat. Its capture
name suggested refusal, but its actual outcome was a successful accepted close and discard.
It is acceptance evidence, not decline evidence. Client-returned acceptance and audit provenance
do not by themselves prove that a human saw every prompt or intended every consequence.

## Attempted failures and diagnostic limits

| Case | Classification and follow-up |
| --- | --- |
| Create while another project was open | Real Siemens `worker_operation_failed`; successful creation followed explicit close and state inspection |
| Archive modified source with `saveBeforeArchive:false` | Real Siemens `worker_operation_failed`; successful archive followed clean-source restoration/save |
| Client policy refusals before dispatch | Client refusals, not server guard/elicitation refusals; no server audit records |
| Save assertion expected `executed` instead of `applied` | Harness assertion corrected against the retained response without replaying the call |
| Unsupported verifier expected the literal word “unsupported” | Harness assertion corrected for the actual “does not support” capability message; both real refusals retained |
| Audit hash verifier omitted `sha256:` | Verifier corrected to the documented hash format; all 37 records passed without replaying live calls |

The two real attempted failures returned `success:false`, top-level `error:null`,
`isError:false`, typed failure details, and null verification. They are not pre-mutation
rejections or successful operations. State was inspected before a new authorized call.

An initial non-JSON existing-source create policy refusal was not retained as a raw response;
the manifest notes that limitation and the source-state inspection. It is not counted as a
captured lifecycle response or an audit record. Corrected harness checks do not erase the original
diagnostics or convert client refusals into server evidence.

## Audit, artifacts, and restoration

Exactly **37** guarded-write audit records correspond to the 35 primary lifecycle documents and
two secondary unsupported-client refusals. Expected calls were grouped by exact response text
using ordinal comparison; actual multiplicity matched for every group. There were no cardinality
mismatches and zero response-hash failures. The format checked was `sha256:` followed by lowercase
hexadecimal digits. Client policy denials contribute no server records.

| Persisted artifact | Observed size/check |
| --- | --- |
| Fixture B project | 151,376 bytes; SHA256 recorded in ignored evidence |
| Fixture C project | 151,373 bytes; SHA256 recorded in ignored evidence |
| Compressed archive | 2,299,105 bytes; ZIP directory readable; 17 entries |
| Archive inside Fixture A's project directory | Absent after the hard-block test |

ZIP directory metadata was readable; entry data/CRC validation and Siemens archive retrieve/restore
were not run.

The final closed Fixture A file matched the saved baseline taken before the temporary change:
`D6131194C5CDF36B799C21A320909D4A6E3EAC6D90B2838AE950A87C717069A4`.
After reopening and the final save/tag comparison, Fixture A was open and unmodified, tag-table
contents matched the baseline, and the temporary table was absent. The source was restored in
the requested open/clean state.

## Scope retained

This acceptance covers the six lifecycle tools, seven guards, default-on accepted/declined
elicitation, unsupported-capability refusal, dry runs, typed attempted failures, binding
verification, audit correspondence, artifacts, and Fixture A restoration on this candidate.

Forced timeout/crash/disconnect and live verification-failure injection were not performed.
Default-off exact-set behavior remains supported by the offline qualification; this live record
does not claim a separate opt-out-client matrix. There was no PLC compile, download, runtime,
or plant acceptance, no Siemens archive retrieve/restore, and no redesign-wide token retirement.
Network and legacy batch token flows remain active. Publishing, push, merge, release/tag, and
successor-phase execution are not claimed. Any production/base change requires fresh qualification.
