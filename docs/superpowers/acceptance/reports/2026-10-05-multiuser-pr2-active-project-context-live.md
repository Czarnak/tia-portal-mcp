# Multiuser PR 2 qualification and standalone live acceptance

Date: 2026-10-05. Live status: **PASS within the standalone scope and deviations below**.
This records acceptance of [PR 2](../../plans/2026-10-05-multiuser-pr2-active-project-context.md)
on frozen candidate `a47bf0dbc3efba4ff654464b6393d01163dc7900`.
Public Multiuser runtime operations and Issue #65 completion remain outside this result.

## Candidate and offline evidence

Implementation commits through `1ceb3b77a9a5e03e57e5bef1de330d63f71f606b` deliver Tasks 1–4.
The verified source-assertion repairs are committed as `9a492f9`. Explicit user authorization
to commit was received on 2026-10-05. Preparation was committed as `a47bf0d`; the worktree was
clean when that candidate was frozen. Production and tests were unchanged throughout live
acceptance. Subsequent report/index updates are documentation-only.
Stable SDK 10.0.401 follows the repository's 10.0.400/latestFeature policy. PR 1 was merged
as [PR #106](https://github.com/Czarnak/tia-portal-mcp/pull/106); its recorded qualification and
independent review were inspected through GitHub.

Qualification logs are retained under `artifacts/multiuser-pr2/qualification/` in the execution
worktree. Stub and installed-reference compilation, executable in-memory boundary tests,
FakeWorker protocol tests, package checks, and live evidence are distinct.

| Gate | Current result |
| --- | --- |
| Solution/reference restore | PASS |
| Reference source/provenance drift verification | PASS; no reference update |
| Serial Release solution build with stubs | PASS; zero warnings/errors |
| Full serialized suite and 0.80 line-coverage threshold | PASS: 5036/5036, zero skipped; line rate 0.9396 (93.96%) |
| Installed V21 solution/reference-probe compilation | PASS with `UseTiaPortalReferenceStubs=false`; zero warnings/errors |
| Package/doctor and Siemens exclusion | PASS; one canonical worker subtree, 15 required files, no Siemens DLLs |
| Independent production review | No Important/Critical production findings; source-assertion repairs PASS in 121 focused tests |
| Current-candidate live standalone acceptance | PASS: 131 captured tool calls, including 64 lifecycle calls |
| Independent live-evidence review | No Important/Critical behavior findings; supplemental captures and report reviewed separately |

The first full run completed with 5032 passing and four failing source assertions, total 5036.
The failures were `GuidanceStrings_NameBindProject`,
`Baseline_HoldsExclusiveAccessThroughExactOwnerProofAndCompile`,
`OwnerInspection_CannotCompileOrMutate`, and
`WorkerProgram_UsesASharedHelperForTheRepeatedSessionAndProjectRequirement`. Each named a removed
source expression, not a runtime behavior failure. Assertions now require the delivered
standalone owner resolver and its shared guidance policy; the existing validation/order guards
remain. The affected groups plus active-context tests passed 121/121 before the final suite.
The failed run's log and coverage remain under their original paths; the final run uses fresh
`TestResults/multiuser-pr2-final` and `test-coverage-final.log`.
The final full run passed all 5036 tests in 7 minutes 42 seconds, with collection/assembly
parallelism disabled, one xUnit thread, and `MaxCpuCount=1`. Exactly one final Cobertura
report was generated; `verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passed at 0.9396.
Coverage report: `TestResults/multiuser-pr2-final/6e6b4a39-99a9-4494-83be-a8eb386ae439/coverage.cobertura.xml`.
Documentation relative-link and whitespace checks passed. No public host/contract/catalog or
reference-source/artifact change was needed. No test rerun followed documentation-only edits.

The installed V21 API file reports version `2100.0.121.1`. Compilation does not prove live
selection, lifecycle, persistence, headless attachment, or access-dialog behavior.
The real-reference payload was copied before package rebuilding to
`artifacts/multiuser-pr2/real-reference/`; it has no Siemens DLLs and was not globally installed
or launched. Retained payload SHA256:

| Artifact | SHA256 |
| --- | --- |
| `TiaMcpServer.dll` | `5D5D97133F089CE686DD1408A84D14B7B5E499B437EEE4926D63118FD267E69D` |
| `TiaMcpServer.Contracts.dll` | `1DFA6FF779FAA14998AC136087196EF8AB51442326E0E67647756F99D13F8C6B` |
| `openness-worker/TiaMcpServer.OpennessWorker.exe` | `45AB0320FA4912404879F9A225D865AA0E5BBEA4BAACDB413E669EE3FFC0CB3B` |

These are retained offline-build hashes, not the installed live payload. The live payload below
was checked against the candidate's local Release output before execution.

## Authorized live candidate and evidence

The user authorized live tests on three entirely disposable standalone projects, including
offline edits, lifecycle save/discard, isolated headless attachment and fixture restoration.
The initial Sessions directory contained `.als21`/`.amc21` files; only inventory reads occurred
before the user corrected the fixtures to `.ap21`. No session or Project Server mutation occurred.

The configured native MCP used `tia-mcp.exe --access-mode full`. Private stdio clients launched
the exact installed `TiaMcpServer.dll` with read-only, read-write or full mode, serially.
Installed package: `3.0.1-local.179.ga47bf0d`; Portal executable and Base API version:
`2100.0.121.1`. Installed hashes matched the candidate's local Release output:

| Live artifact | SHA256 |
| --- | --- |
| `TiaMcpServer.dll` | `222DE0B496D1BFAE37E435931543E24DD25951E72504CEB4684192B66E40DCDD` |
| `TiaMcpServer.Contracts.dll` | `8095B7ABA3BC8925CAAA6659A69CB49D2699A79291FEDBC95D25AE5C4D74B970` |
| `openness-worker/TiaMcpServer.OpennessWorker.exe` | `E539E6492E555AD5CEB1B9F2FEFB99ED49A8FDEA943EACECFD0B6586BE512901` |

Raw live evidence is retained locally, outside the execution worktree, under
`C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\artifacts\multiuser-pr2-live`.
`provenance.json` records exact authorized project paths, PIDs, scratch root, version and hashes.
`client.py`, each group's `call-NNN.json`, `stdout.jsonl`, `stderr.log`, `initialize-tools.json`, and
`qualification-summary.json` retain requests, responses, elicitation, audit deltas and checks.
Run `python artifacts/multiuser-pr2-live/verify_evidence.py` from that original checkout to
recompute canonical equality, audit hashes, identity/guard checks and file postconditions.
Private local paths and project content remain in ignored captures rather than this report.

## Actual fixtures and restoration

| Role | Observed initial state | Final state |
| --- | --- | --- |
| A | Dedicated UI Portal PID 20800; saved standalone project; 96 tree nodes at depth 3 | Original project open and clean; only reads/binds performed |
| B | Different UI Portal PID 29992; saved standalone project; 22 tree nodes at depth 3; empty root comment | Original path reopened, clean, original comment preserved |
| H | User-supplied standalone fixture, initially closed; empty project; root comment `Created by MCP tool test` | Baseline comment restored and saved; fresh headless reopen verified clean persistence; then closed |

Complete closed-folder copies of H supplied scratch W1/W2. No locked UI-project files were copied.
Scratch root is the exact fresh child `PR2_ActiveContext_a47bf0d_20261005` under the user's
authorized fixture directory, recorded only in ignored local provenance. It contains `Working/W1`, `Working/W2`, create and
SaveAs projects, an intentionally occupied `Collision/PR2_Existing` directory, and two nonempty
archives `Archives/PR2_Archive_RW.zap21` and `PR2_Archive_Full.zap21`. These closed scratch
artifacts are retained for inspection. Negative archive and preview destinations are absent.
No baseline backup was modified. No online PLC, download, run/stop or Project Server action ran.

H's first headless Portal PID 31196 had one attached client, the worker, before and after the
dirty-detach refusal. After the baseline comment was restored and explicitly saved, clean
detach succeeded and that Portal exited. A fresh headless Portal PID 37140 independently
reopened H with `modified=False` and the persisted baseline comment, then released and exited.
`helper-restoration-observations.json` records these tool-output observations and helper/client
exit codes. Inventory snapshots retain the last-client counts. `inventory-final.txt` contains
only the original UI A/B projects; no headless Portal or `FixtureHelper.exe` remained.
Final clean status is captured in `read-only-final/call-003.json` (A) and
`preview-proof/call-009.json` (B).

## Live matrix results

| Group | Calls / lifecycle | Qualified observations |
| --- | --- | --- |
| `read-only-02` | 12 / 0 | Ambiguous no-path bind; exact A; repeat binding epoch unchanged; B conflict without force; forced switch; return to A rejects old cursor |
| `configured-read-only` | 4 / 0 | Matching configured selector promoted; divergent explicit selector conflicts; forced exact bind succeeds |
| `read-only-final` | 4 / 0 | Divergent ordinary read rejected without switching; A still clean; repeat bind unchanged |
| `read-write` | 35 / 24 | All six lifecycle previews and successful applies; decline/cancel preserve state; clean replacement closes worker-owned source; dirty replacement blocks; detach/reattach loses ownership |
| `full` | 57 / 30 | All six previews/applies without elicitation; dirty discard policy; collision/inside-project blocks; typed dirty-archive failure; external identity invalidation; dirty headless last-client refusal and clean release |
| `full-final` | 10 / 4 | Lifecycle SaveAs changes identity; old nonempty-tree cursor rejected; new tree succeeds; close/open restores B |
| `preview-proof` | 9 / 6 | All six previews elicit nothing; full source status identical; all 368 closed-scratch file hashes/lengths unchanged; preview destinations absent |

Read-only discovery exposed five tools and no lifecycle tools; writable modes exposed fifteen.
Across the 64 lifecycle calls: 26 preview, 7 blocked, 31 applied. Applied includes 29 successes
and two typed failed attempts described below. Confirmation records total `user:15`,
`policy:18`, `none:31`. Each entered read-write actual call received exactly one form,
including declined/cancelled calls; previews and pre-confirmation guard blocks received none.
Full received no elicitation. Dirty-close acknowledge satisfaction was `policy`.

All 131 captured tool documents have matching decoded text and structured content. Each of
the 64 lifecycle calls appended exactly one audit v2 record with the correct mode/tool/phase,
exact delivered text and recomputed SHA256. Successful applies have typed successful result
and verification; rejected calls carry root errors; attempted worker failures retain typed
failed results, `error:null`, and MCP `isError:false`.

## Retained failures and deviations

- `read-write/call-013.json`: TIA refused opening alongside the UI-owned B project with
  "Another project is already open." The attempted call returned a typed failure, preserved B
  open and clean, and cleared the failed worker context. `call-014.json` and
  `native-preflight-restoration.json` record inspection; explicit bind recovered selection.
  UI-owned preservation is qualified; successful simultaneous opening is not claimed.
- `full/call-024.json`: archive without save on dirty W1 failed as `worker_operation_failed`,
  with `verification:null` and `error:null`, phase `applied`, and no archive created. Fresh status
  remained modified. Explicit authorized dirty close discarded the edit; reopening verified
  the baseline comment. This is the required observed outcome, not a successful archive.
- Two stop captures retained incorrect test expectations: the former external-SaveAs path
  correctly returned `target_not_found` (`full/call-040.json`); dirty headless detach correctly
  returned public `binding_conflict` (`call-050.json`). Source maps the worker guard to that
  public category while retaining binding; `call-051.json` proves continuity. Expectations
  were reconciled with source and fresh state, without replaying mutations to hide failures.
- H contained no PLC. Temporary root-project comments produced dirty state in H/W1/W2;
  baseline restoration was verified. This qualifies context/lifecycle guards, not PLC tag writes.
- External close/reopen and SaveAs were performed by an independent net48 Openness helper
  attached to the UI Portal. No human UI-click or access-dialog observation is claimed.
- Form acceptance was scripted under the user's explicit authorization. It proves MCP
  elicitation/policy behavior, not that a human saw a client-rendered form.
- The open B project header was exclusively locked, so source byte hashing was unavailable.
  Exact full public status matched before/after all six previews. The separate closed-scratch
  manifests matched all 368 files; no broader source-database byte-preservation claim is made.
- The first private read-only launch used a nonexistent package apphost and failed before any
  TIA action. The corrected DLL launch is `read-only-02`; the empty first folder is retained.

Independent review checked the main RPC captures against stdout, audit hashes, installed
provenance, binding/cursor continuity and ownership/headless evidence, with no Important/Critical
behavior finding. Requested explicit divergent-read, lifecycle-SaveAs cursor and preview-file
checks are included above. Production was not changed to accommodate live results.
This closes only the bounded PR 2 standalone acceptance gate; public `.als21`, local-session
lifecycle, Project Server and future content-service compatibility still require later delivery.

## Superseded preparation proposal

The following records the fixture proposal prepared before authorization. The user supplied
the disposable projects documented above instead. Proposed paths and PLC contents below were
not used and are not outstanding prerequisites for this completed standalone acceptance.

### Disposable fixture contents

Use this exact proposed root, or provide an alternative absolute root before authorization:
`C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures`.
All project folders are expendable V21 **standalone `.ap21` projects**, with complete supporting
files, not copies of only the `.ap21` file. Before opening A/B, make closed-folder backups of all
three complete projects under `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Backups`. Keep these untouched and
outside the authorized mutation/cleanup scope. No Project Server or `.als21` session is needed.
The runner obtains working copies from these closed baselines, not from locked open project files.

Prepare three separately named projects with identical minimal content:

| Fixture | Exact proposed project path | Initial state |
| --- | --- | --- |
| A | `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\PR2_A\PR2_A.ap21` | Open, saved, unmodified in a dedicated UI Portal |
| B | `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\PR2_B\PR2_B.ap21` | Open, saved, unmodified in a different UI Portal |
| H | `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\PR2_H\PR2_H.ap21` | Saved, unmodified, initially closed; runner opens it in an isolated headless Portal |

Inside each project:

- One ordinary offline S7-1500 PLC device named `PR2_PLC`, using a CPU available in the installed
  V21 catalog. Record its order number/firmware. No physical PLC connection is required.
- One PLC-global tag table named `PR2_FixtureTags`.
- One tag `PR2_Probe`, type `Bool`, address `%M100.0`, comment `PR2 baseline`.
- Keep generated default blocks; no custom program, HMI, safety program, IO network, Software Unit,
  external library, or credentials are needed. Save all three baselines and verify they reopen.

The PLC/tag is a deterministic reversible edit target and supplies enough tree nodes for a
paged browse/cursor check. A temporary comment change to `PR2 temporary edit` creates dirty state;
restoration returns the saved comment to `PR2 baseline`. These contents are the proposed test
fixture, not a claim that PLC-content behavior has already been accepted on this candidate.

Keep A and B open in distinct UI instances for ambiguous/exact binding tests. Record both
Portal PIDs. No engineering client may edit them during a case except the specifically scheduled
UI close/reopen or SaveAs step. H must have no extra Openness clients at the last-client detach
case: the runner coordinates headless startup and worker attachment, releases the setup client's
attachment, then proves the worker is the only client before creating dirty state.

### Proposed working copies and scratch destinations

Prepare these empty parent directories; the runner creates/recreates exact disposable children
from the untouched baseline under the subsequently authorized scope:

| Parent | Exact child names and purpose |
| --- | --- |
| `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\Working` | `PR2_W1\PR2_A.ap21` and `PR2_W2\PR2_B.ap21`: initially closed complete-folder copies of A/B for clean/dirty worker-owned replacement tests |
| `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\Create` | `PR2_Create_RW` and `PR2_Create_Full`: lifecycle creation destinations |
| `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\SaveAs` | `PR2_SaveAs_RW`, `PR2_SaveAs_Full`, `PR2_UI_SaveAs`: accepted and external identity changes |
| `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\Archives` | `PR2_Archive_RW`, `PR2_Archive_Full`, `PR2_Archive_Dirty`: archive base names, actual emitted archive paths recorded |
| `C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\Collision` | Runner creates `PR2_Existing` for a known occupied destination; user supplies no valuable data |

The proposed archive-inside-project negative target is
`C:\Users\LCZ\Desktop\TIA-MCP-PR2-Fixtures\Working\PR2_W1\BlockedArchive`;
the guard must refuse it without writing an archive. Record actual project directories and
archive extensions during dry runs before approving any actual lifecycle call.

### Proposed authorization scope before the user's approval

Authorization must identify these exact fixture paths and the frozen candidate. It needs to cover:

1. Read inventory/status/tree, exact bind/rebind, and launch/attach/release the isolated headless
   Portal; one server run at a time in read-only, read-write, and full.
2. Six lifecycle previews and exact-target open/create/save/SaveAs/archive/close actual calls on
   working fixtures; clean worker-owned replacement and UI-owned preservation.
3. Temporary `PR2_Probe` comment edits, save/restore and explicitly discard unsaved fixture edits
   for dirty-source, dirty-close, dirty-archive, and headless-detach checks.
4. Scheduled UI close/reopen at the same path and UI SaveAs for invalidating stale identity;
   return to the authorized baseline afterward.
5. Create/remove the listed scratch children and close/release helper Portals at cleanup. Untouched
   backups are excluded. Download, PLC run/stop, online work, and Project Server operations are outside scope.

### Original planned matrix

| Group | Required observations |
| --- | --- |
| Read-only selection | A/B ambiguity; exact A; repeat epoch; conflicting B without force; forced B; return to A rejects old cursor; configured-selector parity; no lifecycle discovery |
| Read-write lifecycle | Six previews/applies; zero preview prompts; one prompt per actual call; decline/cancel preserve state; ownership replacement/dirty blocking; SaveAs/close identity postconditions |
| Full/adverse state | Six previews/applies without server prompts; blocks remain; exact dirty discard; occupied destination; archive-inside-project refusal; dirty archive without save typed outcome |
| Headless/external changes | Modified last-client detach blocked; explicitly clean/save before release; UI close/reopen and SaveAs reject former binding/cursor; reattachment does not restore ownership |
| Contract/restoration | Text equals structured JSON; typed result/phase/verification; one matching audit/hash per lifecycle call; actual filesystem evidence; final clean fixture inventories/comments |

Candidate provenance, observed postconditions, failure inspection, headless evidence and final
restoration are recorded in the completed live sections above. This original preparation matrix
does not imply public Multiuser acceptance.
