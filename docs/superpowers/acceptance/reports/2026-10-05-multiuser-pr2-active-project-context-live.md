# Multiuser PR 2 qualification and standalone live preparation

Date: 2026-10-05. Live status: **NOT RUN; exact fixtures and authorization pending**.
This is the prepared record for [PR 2](../../plans/2026-10-05-multiuser-pr2-active-project-context.md),
not a live acceptance result or Multiuser runtime qualification.

## Candidate and offline evidence

Implementation commits through `1ceb3b77a9a5e03e57e5bef1de330d63f71f606b` deliver Tasks 1–4.
The verified source-assertion repairs are committed as `9a492f9`. Explicit user authorization
to commit was received on 2026-10-05; maintained documentation and fixture preparation accompany
the qualified source. Record the final clean branch HEAD as the frozen candidate before live
execution. No current-head live acceptance is claimed; exact fixtures and authorization remain pending.
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
| Current-candidate live standalone acceptance | NOT RUN |

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

Before live use recheck these hashes against the actual launched payload, record the clean
candidate SHA and product version, and obtain exact-target authorization. Do not assume a
global installation or the later stub-package output matches this retained real-reference build.

## Disposable fixture contents

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

## Working copies and scratch destinations

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

## Permitted live actions to authorize later

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

## Live matrix and evidence still required

| Group | Required observations |
| --- | --- |
| Read-only selection | A/B ambiguity; exact A; repeat epoch; conflicting B without force; forced B; return to A rejects old cursor; configured-selector parity; no lifecycle discovery |
| Read-write lifecycle | Six previews/applies; zero preview prompts; one prompt per actual call; decline/cancel preserve state; ownership replacement/dirty blocking; SaveAs/close identity postconditions |
| Full/adverse state | Six previews/applies without server prompts; blocks remain; exact dirty discard; occupied destination; archive-inside-project refusal; dirty archive without save typed outcome |
| Headless/external changes | Modified last-client detach blocked; explicitly clean/save before release; UI close/reopen and SaveAs reject former binding/cursor; reattachment does not restore ownership |
| Contract/restoration | Text equals structured JSON; typed result/phase/verification; one matching audit/hash per lifecycle call; actual filesystem evidence; final clean fixture inventories/comments |

Before live execution record clean candidate SHA, exact real-reference host/worker/Contracts hashes,
V21 product version, observed PIDs and paths, authorized requests and cleanup scope. Retain failures
and inspect possible mutation before any retry. An unavailable headless case leaves the gate open.
