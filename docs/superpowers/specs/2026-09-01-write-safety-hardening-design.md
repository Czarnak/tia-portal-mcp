# Write-Safety Preview and Registered-Surface Hardening Design

**Date:** 2026-09-01

**Status:** Architecture approved; awaiting written-spec review
**Source:** `priv/CLAUDE_THOUGHTS_VERIFICATION.md` and the follow-up repository and live-TIA investigation
**Delivery:** Six active milestones, each delivered as a separate pull request

## Goal

Improve the reviewability, MCP metadata, test fidelity, and state-snapshot precision of the
existing write-safety system without weakening its server-enforced guarantees or changing the
public write-tool names and input schemas.

Every pull request in this design must pass both repository verification and scope-specific
live TIA Portal V21 acceptance. Offline, stub, and FakeWorker evidence is necessary but never
sufficient for completion.

## Scope

The active work is:

1. Add explicit MCP annotations and verify the emitted `tools/list` metadata.
2. Put behavioral coverage on the registered write classes and make compatibility wrappers
   delegate instead of duplicate behavior.
3. Complete the `update_tag` mutable-state snapshot with the three external-access flags.
4. Add bounded, structured preview evidence for block and type content replacements.
5. Narrow and deduplicate tag-related current-state reads without weakening conflict detection.
6. Narrow project-tree current-state reads operation by operation.

PLC start/stop investigation was originally the fourth milestone. It is explicitly deferred and
does not become a quarantine or removal change under this design.

## Non-goals

- Replacing `preview_write_batch` and `apply_write_batch` with one self-previewing tool.
- Removing `BatchTools` or `ProjectLifecycleTools`.
- Removing `confirm` from any write tool.
- Changing the safety-token format, ten-minute lifetime, single-use behavior, or audit format.
- Weakening exact project/session binding or the pinned apply lease.
- Predicting Siemens post-write state in the host.
- Generalizing text diffs to lifecycle, network, creation, deletion, or tree operations.
- Changing the canonical structured-JSON network contract.
- Implementing, removing, or quarantining `start_plc` or `stop_plc`.
- Claiming a particular MCP client's warning or approval user experience.
- Treating PLCSIM acceptance as physical-PLC or plant acceptance.

## Current evidence

The existing architecture already provides the strongest safety properties identified in the
verification note:

- the token binds the exact requested input and freshly read current state;
- the token retains the complete verified project/session binding;
- tokens are single-use, including after an uncertain apply attempt;
- apply re-reads state and consumes the token under a pinned binding lease; and
- successful writes are audited.

The confirmed hardening gaps are:

- registered batch and lifecycle tools omit explicit MCP behavior annotations;
- most behavioral tests exercise compatibility wrappers rather than the registered classes;
- `update_tag` can change `ExternalAccessible`, `ExternalVisible`, and `ExternalWritable`, while
  the current tag snapshot omits all three values;
- the public tag reader can skip an unreadable tag or constant and return a partial payload, which
  is not acceptable as a fail-closed write-safety snapshot;
- block and type replacement previews contain target and hash evidence but no bounded content
  comparison;
- tag operations repeatedly read the selected PLC's complete tag-table state;
- three structural block operations bind to the complete project tree; and
- the current PLC start/stop token binds project metadata rather than CPU operating state.

Two guarded, read-only V21 probes were performed while planning. An offline provider and a
PLCSIM-connected provider both exposed only connection state, with no public `Start`, `Stop`,
CPU operating-mode, or RUN/STOP-state read. The connected provider reported `State=Online`, but
its public CLR and self-description surfaces did not expand. This evidence is the reason PLC
control work is deferred rather than guessed from undocumented APIs.

## Architecture invariants

Every milestone must preserve these invariants:

1. Siemens Openness calls remain in the net48 worker. The net8 host performs validation,
   orchestration, hashing, token handling, response construction, and audit only.
2. The public tools remain `preview_write_batch`, `apply_write_batch`, the six lifecycle write
   tools, and `network_write`. No active milestone renames, combines, or removes a tool.
3. Preview remains non-mutating. Apply still requires the unchanged request, `confirm=true`, and
   the matching single-use safety token.
4. Tokens continue to bind tool name, normalized project path, ordered targets, exact requested
   input, exact current state, and the complete verified project/session binding.
5. Apply continues to perform cheap envelope rejection before expensive reads, then fresh-state
   read, token consumption, mutation, and audit under the pinned binding lease.
6. Display evidence and its truncation policy never participate in target, input, or current-state
   hashing. Changing display formatting must not invalidate or validate a token.
7. New worker success payloads are typed shared contracts. Missing, malformed, ambiguous, or
   unsupported state fails closed before token issuance.
8. Batch order and per-operation identity remain significant. Read deduplication may reuse one
   payload internally, but it must expand back to one ordered state entry per operation before
   `OperationBatchStateComposer` hashes it.
9. No milestone introduces undocumented Siemens calls. Runtime behavior that cannot be proven
   from the installed API and live acceptance remains deferred.
10. Each milestone is independently reviewable and shippable. It receives its own branch, pull
    request, implementation plan, documentation updates, verification output, and live acceptance
    report.

## Delivery sequence

The pull requests are intentionally serial. Later work may depend on coverage or contracts added
by earlier work, but no pull request may absorb the next milestone merely because the files
overlap.

| Pull request | Scope | Dependency |
| --- | --- | --- |
| PR 1 | Registered MCP annotations | Existing baseline |
| PR 2 | Registered-class coverage and wrapper delegation | PR 1 |
| PR 3 | Complete `update_tag` mutable-state snapshot | PR 2 |
| PR 4 | Bounded block/type preview evidence | PR 2 |
| PR 5 | Narrow and deduplicate tag scopes | PRs 2 and 3 |
| PR 6 | Narrow project-tree scopes | PR 2; reuse only proven internal patterns from PR 5 |

The PLC start/stop investigation is a deferred work item, not an active pull request.

PRs 4 and 5 remain separate even though both touch batch preview state. PR 4 changes human review
evidence; PR 5 changes the safety snapshot selectors themselves.

## Milestone 1: registered MCP annotations

### Design

Add explicit annotations to the registered methods in `WriteBatchTools` and
`ProjectWriteTools`.

| Tool | `ReadOnly` | `Destructive` | `OpenWorld` | Reason |
| --- | ---: | ---: | ---: | --- |
| `preview_write_batch` | `true` | `false` | `false` | The tool only reads current state and issues a token. |
| `apply_write_batch` | `false` | `true` | `false` | The tool can mutate the bound TIA project. |
| Six lifecycle write tools | `false` | `true` | `false` | Each public tool can mutate on its apply call, so tool-level metadata is conservative even though its first call is a preview. |

`network_write` already uses the conservative mutating classification and remains a regression
reference. Annotations are untrusted client hints and must not be described as stronger than the
token flow.

### Verification boundary

- Add protocol-level tests that call `ListToolsAsync`, not reflection-only tests.
- Assert exact annotations, names, and availability in read-only and read-write server modes.
- Preserve existing input schemas and tool counts.
- Run a live V21 host, complete MCP initialization, call `tools/list`, and record the emitted
  annotations. The same run must make one benign project read so the report proves that the host
  is connected to a real TIA session rather than only testing an in-memory SDK server.
- No project mutation is required for this milestone.

## Milestone 2: registered-class coverage and wrapper delegation

### Design

Make the registered classes the behavioral authority:

- `BatchTools` delegates reads to `ReadBatchTools` and writes to `WriteBatchTools`.
- `ProjectLifecycleTools` delegates status reads to `ProjectReadTools` and writes to
  `ProjectWriteTools`.
- Delegation flows toward the registered classes, never from registered production classes into
  legacy wrappers.
- The wrappers remain compiled compatibility/test seams. Deletion is deferred.

Before delegation, add direct tests for the registered methods covering schema, read-only gates,
binding gates, preview issuance, token validation and replay, pinned lease behavior, ordered batch
execution, audit isolation, and worker/protocol error propagation. Existing wrapper-oriented tests
may then be migrated or retained as delegation tests, but they may not remain the sole evidence for
registered behavior.

### Verification boundary

- Demonstrate a behavioral RED on a registered method before changing delegation or production
  mechanics; a reflection-only failure is insufficient for a runtime behavior claim.
- Run the full registered path against FakeWorker and the actual MCP protocol surface.
- In live V21, execute a registered generic-batch preview and a self-previewing lifecycle call
  without its token. Verify both are non-mutating and retain the expected binding and token data.
- No apply is required because this milestone changes test/runtime alignment rather than write
  semantics.

## Milestone 3: complete `update_tag` mutable-state snapshot

### Design

Introduce a typed exact-target `TagUpdateSafetySnapshot` in `TiaMcpServer.Contracts` and a
dedicated strict worker reader for `update_tag`. The snapshot binds deterministic PLC, folder,
table, and tag identity plus every property the current mutator can change: name, data type,
logical address, `ExternalAccessible`, `ExternalVisible`, and `ExternalWritable`.

The three external values are nullable so an actual `false` differs from a property that the
selected PLC/tag does not expose. If the request intends to change a flag whose current value
cannot be read, preview fails before issuing a token.

For this milestone, `update_tag` composes the strict exact-target snapshot with the existing broad
`ListTagTablesAsync` state instead of replacing it. This adds the missing mutable fields without
discarding current collision/context coverage or making an unrelated unreadable tag a new global
failure. Milestone 5 replaces that composition with proven scoped collision selectors.

The exact-target reader must not skip or partially serialize the target. The existing public
`list_tag_tables` response, its best-effort behavior, and the other tag operation selectors remain
unchanged. `IsSafety` is not added because the current write path rejects it before mutation.

### Verification boundary

- Add contract, strict-reader, serialization, FakeWorker, and registered preview/apply tests for
  all three flag values, including `false` versus unavailable and an exact-target read failure.
- Prove the legacy broad state is still composed for `update_tag` until milestone 5 and that an
  unrelated best-effort omission does not newly fail the preview.
- Prove that changing only one external flag changes the current-state hash and causes an old token
  to fail with `state_changed` before mutation.
- Prove ordinary tag updates that do not request an unavailable external flag remain supported.
- In live V21, read real tag flags, preview an `update_tag`, change exactly one flag through an
  explicitly authorized action on a disposable project, and verify that the stale token is
  rejected. Restore or discard the disposable project change and record the final state.

## Deferred item: PLC start/stop investigation

No pull request is created for this deferred item.

### Current behavior preserved

- `start_plc` and `stop_plc` remain advertised and implemented as they are today.
- Their current project-status snapshot remains unchanged.
- The reflected worker control calls are neither replaced nor quarantined.

### Reason for deferral

The installed and live V21 public `OnlineProvider` surface did not expose the assumed `Start` and
`Stop` methods or an authoritative CPU RUN/STOP read. The user will investigate the Siemens API
surface more deeply before choosing compatibility, quarantine, removal, or a supported control
implementation.

### Known risk while deferred

- The reflected control calls are contradicted by the inspected V21 runtime and may fail when
  invoked.
- The safety token does not bind CPU operating state, so it cannot detect an intervening RUN/STOP
  transition.
- No milestone in this design may cite generic project status as proof of CPU operating state.

### Re-entry conditions

Work may resume only when all of the following exist:

1. A documented, installed V21 public API for the intended control action.
2. A documented, authoritative CPU operating-state read.
3. Deterministic binding to the exact project, PLC, CPU device item, and online target.
4. A fail-closed design for unavailable, transitional, protected, and incompatible states.
5. Successful live qualification against the intended PLCSIM and, if the feature claims physical
   hardware support, separately authorized hardware acceptance.

## Milestone 4: bounded block/type preview evidence

### Design

Reuse the existing top-level `diff` field in the text preview envelope. Widen the text-preview
helper from `string?` to a structured response-only value and populate it only for
`update_block_logic` and `update_type_content`.

The canonical `network_write` preview remains unchanged. Lifecycle and non-content batch
operations continue to emit `diff: null`.

The structured batch diff contains one entry per eligible operation in request order:

- operation ID, operation name, and normalized write format;
- raw current/requested SHA-256, character count, and line count;
- whether raw text is equal, normalized lines are equal, or the difference is line-ending-only;
- unchanged prefix and suffix line counts;
- current and requested changed-span line counts;
- bounded current and requested excerpts; and
- explicit omitted-line, omitted-character, and exhausted-batch-budget metadata.

Evidence compares the exact-format state already read for token binding with the submitted
replacement text. It is a current-versus-requested comparison, not a prediction of compiled or
post-import Siemens state. It performs no additional worker read.

### Deterministic bounds

- At most 40 excerpt lines and 8,192 excerpt characters are retained from each side of one
  operation.
- When a changed span exceeds 40 lines, retain its first 20 and last 20 lines and report the exact
  omitted-line count.
- A single displayed line is capped at 512 characters and reports omitted characters.
- Across the complete batch, excerpts are capped at 32,768 characters and 320 lines, allocated in
  request order.
- Every eligible operation retains hashes and counts even after the batch excerpt budget is
  exhausted.
- Raw hashes and equality use the original text. Line-window comparison normalizes CRLF and CR to
  LF and explicitly reports a line-ending-only difference.

Changing these display limits later is a presentation change only. The `diff` value and all
truncation decisions remain outside token issuance and validation.

### Verification boundary

- Add registered-class tests for source and XML block content, type content, mixed batches,
  identical content, line-ending-only changes, long lines, per-entry truncation, and whole-batch
  truncation.
- Prove non-content operations keep `diff: null`.
- Prove changing or omitting display evidence has no effect on token validation when target,
  request, state, and binding are unchanged.
- Preserve the existing exact-format current-state tests.
- In live V21, use host-level MCP calls against a disposable project to preview source-format block
  and type replacements, verify bounded evidence, apply unchanged input with the issued token,
  restore byte-equivalent content, compile, and record the clean final state. Apply and restore
  require explicit authorization for the exact disposable target.

## Milestone 5: narrow and deduplicate tag scopes

### Design

Replace the blanket `ListTagTablesAsync` safety selector with typed, operation-specific worker
snapshots. The worker resolves Siemens objects; the host never reconstructs object identity from
partial text.

The selector shapes are:

- `create_tag_table`: exact PLC and parent-folder identity plus occupancy for the requested table
  name;
- `delete_tag_table`: parent membership plus a deterministic Simatic ML export of the complete
  target table using timestamp-free export options;
- `create_tag`: target table identity plus collision results for the requested effective tag name
  and logical address;
- `update_tag`: complete current target-tag state, target table identity, and collision results for
  the requested effective new name/address;
- `delete_tag`: current target-tag identity and every safety field introduced for `update_tag` in
  milestone 3, plus target table identity;
- create/update/delete user constant: target table identity, complete target-constant state when it
  exists, and collision results for the requested effective name.

Collision probes are scoped to exact requested names/addresses across the selected PLC. They do
not hash unrelated tag content. Results use deterministic canonical paths and ordering.

Within one preview or apply state-read phase, identical selectors may execute once. The cache key
must include normalized project path, verified PLC identity, folder path, table name, object name,
effective requested name/address where relevant, and selector kind. Reused payloads are expanded
back into the original operation order before combined-state hashing.

No snapshot cache survives the current preview or apply phase. Apply always performs fresh reads.

### Verification boundary

- Add selector-contract tests for every tag/table/constant operation and malformed worker payload.
- Prove same-object and relevant-collision changes invalidate the token.
- Prove unrelated sibling-table changes no longer invalidate operations whose proven preconditions
  do not include that sibling.
- Prove identical selector calls are deduplicated while combined state remains ordered and
  operation-specific.
- Prove apply still re-reads after preview and does not use a cross-phase cache.
- In live V21 on a disposable project, exercise same-object drift, relevant name/address collision,
  unrelated sibling-table drift, and a successful explicitly authorized apply followed by restore
  or discard. Record both rejection and non-false-invalidation evidence.

## Milestone 6: narrow project-tree scopes

### Design

Replace complete-project tree snapshots only for the three structural operations that currently
use them:

- `create_block`: bind the exact PLC/software-unit and ancestor group chain, target parent-group
  identity, and requested block/group-name occupancy in that parent. Because the current worker
  imports with `ImportOptions.Override`, an occupied target additionally binds the exact existing
  block export rather than only its name;
- `create_block_group`: bind the exact PLC/software-unit and ancestor group chain, target
  parent-group identity, and both block and group occupancy for the requested name;
- `delete_block_group`: bind the exact target group, its parent membership, and the complete target
  subtree because that subtree is the destructive effect under review. The subtree snapshot
  includes deterministic content exports for contained blocks, not only their names.

The worker returns typed, deterministically ordered snapshots. An unresolved or ambiguous path,
unsupported software-unit selector, duplicate candidate, or malformed snapshot fails before token
issuance.

Deduplication follows the milestone 5 rule: reuse only identical reads within one phase, then
expand them back into per-operation order. No generic tree-pruning heuristic is introduced.

### Verification boundary

- Add selector tests for root groups, nested groups, software units, missing targets, ambiguous
  targets, occupied names, occupied-target content, and complete content-bearing delete subtrees.
- Prove a relevant parent, occupancy, rename, move, deletion, or descendant change invalidates the
  token when it changes the intended operation or destructive effect.
- Prove unrelated device, tag, type, or sibling-tree changes do not invalidate a narrowly scoped
  operation.
- In live V21 on a disposable project, determine and record the actual name-collision behavior for
  sibling blocks/groups, exercise relevant and unrelated drift, perform one explicitly authorized
  reversible apply, and restore or discard the project. The same run must prove that an
  occupied-target content change invalidates `create_block` and that a descendant block-content
  change invalidates `delete_block_group`. The live result is part of acceptance, not an optional
  follow-up.

## Error handling

- A missing, unreadable, malformed, ambiguous, or unsupported safety snapshot fails before token
  issuance; there is no fallback to a broader stale payload or empty state.
- A stale token continues to fail as `state_changed`; binding mismatches remain
  `binding_conflict`.
- An unsupported tag flag requested for mutation fails before token issuance rather than being
  omitted from the state hash.
- Oversized preview evidence uses the deterministic truncation contract. Unexpected evidence
  generation or serialization failure fails the preview and issues no usable token.
- Rejected worker payloads are not echoed to clients.
- A failed or unavailable live-TIA gate leaves the pull request incomplete. It is not converted
  into an offline-only acceptance claim.

## Testing and verification policy

Each implementation plan must use behavioral TDD:

1. Add a focused test against the registered runtime path.
2. Observe a meaningful failure caused by missing production behavior.
3. Implement the smallest production change.
4. Run the focused test and relevant integration slice.
5. Run the complete serial stub build and test suite.
6. Run the milestone's live V21 harness and write its acceptance report.
7. Review the diff, documentation, and deferred-work register before the pull request is complete.

The common repository commands are:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

Each pull request adds or updates a scope-specific PowerShell 7 live harness with a safe
non-mutating default, an explicit mode for any authorized mutation, static harness-contract tests,
and a dated report under `docs/superpowers/acceptance/reports/`. Reuse is acceptable only when an
existing harness already exercises the exact milestone surface and records the milestone-specific
evidence. The report records the exact TIA version, project copy, PLC/CPU identity where
relevant, connection state, calls performed, mutations performed, restoration or discard result,
and evidence boundary.

Live acceptance uses a disposable project copy for every mutation. It does not save, close,
reconnect, start/stop a CPU, or change a project unless the exact action and target have been
authorized for that run.

## Deferred-work register

| Deferred item | Current behavior preserved | Reason and risk | Re-entry condition |
| --- | --- | --- | --- |
| PLC `start_plc` / `stop_plc` state binding and implementation | Tools remain advertised; current snapshot and reflected calls remain | Installed/live V21 evidence did not expose the assumed methods or authoritative state; calls may fail and tokens do not detect mode drift | Documented control and state APIs, exact target design, and mandatory live qualification |
| One self-previewing batch tool | Separate preview/apply tools remain | Public tool-name and client workflow migration is larger than the safety hardening | Separate compatibility design and protocol migration evidence |
| Compatibility-wrapper deletion | Wrappers remain and delegate | Their test/compatibility purpose must be resolved after registered coverage is established | Proven no consumers plus migrated coverage and separate approval |
| Dropping `confirm` | All current confirmation parameters remain | It is still the lifecycle/network phase selector and removing it changes public inputs | Separate per-tool contract analysis and migration approval |
| Generalized or predicted post-state diff | Only exact text replacements receive bounded current-versus-requested evidence | Host-side prediction would duplicate worker/Siemens semantics and can mislead | Operation-specific authoritative post-state model and separate design |
| Lifecycle and network preview diffs | Existing `diff: null` behavior remains | Not required to solve the confirmed block/type reviewability gap | Independent value case and operation-specific evidence design |
| Broader snapshot narrowing | Only the operation scopes named in milestones 5 and 6 change | Blanket narrowing risks omitted preconditions and silent overwrites | Per-operation conflict model, tests, and live evidence |
| Public `list_tag_tables` completeness semantics | Existing best-effort read behavior remains; `update_tag` safety adds a separate strict exact-target reader | Changing public partial-read behavior is a separate compatibility decision | Dedicated read-contract design, schema tests, and live compatibility evidence |
| Per-tag multilingual comment binding for `delete_tag` | Current omission remains; table deletion is separately bound to a complete table export | The current mutator does not edit comments, and a stable multilingual per-tag safety contract is not yet implemented; a concurrent comment-only edit can remain undetected before tag deletion | Documented comment extraction contract, exact-target serialization tests, and live comment-drift rejection evidence |
| Client-specific warning/prompt behavior | Server emits standards metadata only | MCP annotations are untrusted hints; UI behavior is outside this repository | Named client, reproducible version, and client-level acceptance scope |
| Physical PLC and plant acceptance | PLCSIM/live TIA is the minimum gate | These milestones do not claim production hardware behavior | Separately authorized target, commissioning plan, and plant acceptance |

Every pull request repeats the deferred entries relevant to its scope in its description and
documentation. Completing one milestone does not silently authorize a deferred item.

## Documentation impact

Each pull request updates the current authorities affected by its behavior:

- `docs/ARCHITECTURE.md` for write-safety or selector architecture;
- the relevant supported-operation reference for additive outputs or behavior;
- `docs/IMPROVEMENT_LOG.md` for completed and still-open work;
- `README.md` only when landing-page behavior materially changes; and
- both documentation indexes for each new plan or acceptance report.

Historical specs, plans, and acceptance reports remain evidence of their date. They do not replace
the current architecture or supported-operation documents.

## Acceptance criteria

- The six active milestones are delivered as six separate pull requests.
- Every pull request has a separately approved implementation plan and passes its mandatory live
  TIA Portal V21 acceptance gate before completion.
- Registered tools emit explicit, verified MCP annotations.
- Registered classes, not only compatibility wrappers, carry the behavioral safety evidence.
- Tag state includes every mutable external-access flag used by `update_tag`.
- Block/type previews provide deterministic bounded current-versus-requested evidence without
  changing token semantics.
- Narrow tag and tree selectors detect every proven relevant conflict while eliminating the
  specifically tested unrelated invalidations.
- Read deduplication never changes ordered per-operation state composition or crosses preview/apply
  phases.
- Existing binding, token, lease, confirmation, audit, access-mode, and canonical-network
  guarantees remain intact.
- PLC start/stop work and every other deferred item remain explicitly out of implementation scope.
