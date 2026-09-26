# Open Bug Parallel Pull-Request Delivery Design

**Date:** 2026-09-26

**Status:** Written design awaiting review

**Source:** Review of every open GitHub issue carrying the `bug` label at repository commit
`22a04a8`, followed by the approved parallel-delivery discussion on 2026-09-26

**Delivery:** A fan-out/fan-in train of separately reviewable pull requests. Independent lanes may
be implemented and reviewed concurrently from the same verified `main`; dependent or
file-overlapping lanes start only after their predecessor merges and its evidence is reviewed.
Merges, shared-document reconciliation, and live TIA Portal acceptance remain serialized.

## Goal

Correct the currently known open bugs before continuing roadmap work, without forcing independent
repairs through one long serial queue and without creating overlapping pull requests that compete
for the same production or test seams.

Success means:

- every independently actionable defect slice is assigned to exactly one owning pull-request
  lane, while an umbrella issue may span several explicitly named slices;
- umbrella issues #78 and #82 remain traceable across their separate slices;
- concurrently active implementation pull requests have disjoint production and test ownership;
- safety, selector, protocol, and result contracts are stabilized before dependent repairs use
  them;
- each behavior change has focused automated evidence and proportionate integration coverage;
- current-main integration is reverified after every merge; and
- live TIA Portal evidence is clearly separated from stub, FakeWorker, static, and offline evidence.

This design governs delivery and dependency boundaries. It does not itself authorize
implementation, GitHub writes, project mutation, PLC control, or any live TIA Portal run.

## Reviewed issue scope

The 2026-09-26 snapshot contained thirteen open issues with the `bug` label. Issue #82 contains
eight independently actionable checklist items, so the delivery program covers twenty defect
units in total.

| Issue | Reviewed defect | Owning lane |
| --- | --- | --- |
| #70 | `open_project(forceRebind=true)` can discard unsaved changes | L1 |
| #71 | Nested compiler diagnostics are omitted | P1 |
| #72 | Block update can commit before verification failure without truthful final-state evidence | I2 |
| #73 | Cross-reference reads can return false-clean success | P1 |
| #74 | PLC start/stop uses the wrong service owner and an unqualified API | O5 |
| #75 | Generated OB source omits a deterministic block number | B2 |
| #76 | Network-device station and item names are reversed | N1 |
| #77 | Network configuration can report success after a requested setting is skipped | N2 |
| #78 | Write previews omit material target and change information | L1, B2, N3, G4, O5 |
| #79 | A batch cannot preview a supported consumer of an object created earlier in that batch | G4 |
| #80 | Node selectors advertised by reads cannot reliably be used for writes | N3 |
| #81 | Lifecycle previews can issue tokens for operations that cannot apply | L1 |
| #82.1 | `get_type_content` binding policy and guidance are inconsistent | T2 |
| #82.2 | Public `plcName` can contain a device name instead of the PLC software name | P1, O5 |
| #82.3 | Short block paths parse but are rejected by write-safety resolution | B2 |
| #82.4 | Expected missing hardware attributes produce excessive duplicate messages | N3 |
| #82.5 | PROFIBUS addresses are emitted as IP addresses | N3 |
| #82.6 | Network-node evidence omits the connected subnet | N3 |
| #82.7 | Per-item batch failures lose `failureCategory` | K1 |
| #82.8 | Source export can be stale and can weaken source-write drift detection | S3 |

Issue state, comments, linked pull requests, and the exact `main` commit must be refreshed before
implementation begins. This historical issue snapshot is not evidence that GitHub state remains
unchanged after 2026-09-26.

## Selected delivery model

Three delivery shapes were considered:

1. **One fully serial queue.** This minimizes coordination but unnecessarily blocks unrelated
   lifecycle, PLC-read, batch-contract, and network-creation repairs.
2. **One branch per defect, all at once.** This maximizes apparent concurrency but collides in
   shared safety, selector, worker-dispatch, FakeWorker, DTO, test, and documentation files. It also
   lets dependent PRs encode contracts that a predecessor is still changing.
3. **Parallel dependency waves with explicit ownership.** This keeps independent work concurrent,
   groups defects that share one semantic seam, and serializes only real code or contract
   dependencies.

The third option is selected. Wave numbers express dependency order, not a requirement to wait for
every unrelated PR in an earlier wave. A lane becomes eligible when its direct prerequisites have
merged, their evidence has been reviewed, and its detailed plan proves a disjoint file boundary
from every other active lane.

## Delivery invariants

1. Every implementation PR declares an allowlist of production, contract, test, fixture, and
   maintained-documentation files it owns.
2. A concurrently active PR must not edit another active PR's allowlisted file. Line-level
   separation inside one file is not considered sufficient isolation.
3. If investigation shows that a lane needs a file owned by another active lane, the lane pauses.
   It is rebased and replanned after the owning PR merges; the conflict is not worked around with
   duplicate helpers or parallel contract mechanisms.
4. Shared hotspots have one owner at a time. Important examples include `TiaPortalSession.cs`,
   `WorkerRequest.cs`, worker `Program.cs`, `OpennessWorkerClient.cs`, `BatchSafetySnapshot.cs`,
   `BatchWorkerInvoker.cs`, `WriteBatchTools.cs`, `HardwareConfigReader.cs`,
   `NetworkDeviceConfigurator.cs`, `TiaMcpServer.FakeWorker/Program.cs`, `README.md`, and
   `docs/ARCHITECTURE.md`.
5. New focused test files are preferred when that avoids artificial collisions in broad existing
   test classes. The test project file is itself a shared hotspot when explicit linked includes are
   required.
6. Each branch starts from the exact merged `main` required by its dependencies. Unrelated lanes in
   the same wave may share a base commit; dependent lanes are not pre-stacked on an unmerged PR.
7. Implementation and review may run concurrently, but merges are one at a time. After each merge,
   affected ready branches refresh from `main` and rerun their relevant gates.
8. No PR absorbs opportunistic refactoring or an adjacent roadmap feature. Unexpected defects are
   reported and scheduled explicitly.

## Wave 1 - urgent and foundational repairs

Four implementation PRs may start from the same verified `main` because their intended production
ownership is disjoint.

### L1 - Lifecycle rebind and preview safety

**Issues:** #70, #81, and the lifecycle portion of #78.

This PR owns project lifecycle preview/apply behavior. It rejects a modified worker-owned source
project before issuing an `open_project` rebind token, binds the source modified state into preview
and apply validation, repeats the guard immediately before close, rejects impossible open/archive
previews, and displays the resolved source and destination consequences.

Primary seams are `ProjectWriteTools`, `OpennessWorkerClient`, `TiaPortalSession`, and, if needed,
`ProjectRebindCloseGuard`. No generic batch-token redesign is planned.

The modified-source contract is fail-closed: the server neither implicitly saves nor silently
discards source changes. The live acceptance boundary, if separately authorized, uses two exact
disposable projects and proves that stale or modified state prevents close.

### K1 - Per-item batch failure categories

**Issue:** #82.7.

This small response-contract PR preserves the already validated worker failure category through
`OperationBatchExecutionEngine`, `OperationBatchResult`, and `OperationBatchResultFormatter`.
It explicitly decides and tests whether the additive field appears on both read and write failures,
and rechecks payload budgets.

This PR does not change preview tokens, worker operations, mutation policy, or rollback behavior.
It provides a stable failure envelope for later generic-batch fixes.

### P1 - PLC compile and cross-reference read integrity

**Issues:** #71, #73, and the compile/cross-reference portions of #82.2.

This PR gives one owner to the PLC read/diagnostic reference document while keeping two focused
implementation commits:

- recursively flatten bounded compiler messages in deterministic order while preserving compiler
  totals and signalling omissions; and
- query cross-reference services from documented source-object owners, distinguish a successful
  zero-result query from a query that never ran successfully, and expose the actual PLC software
  name separately from device identity.

Primary worker seams are `CompileChecker` and `CrossReferenceReader`. The shared
`PlcSoftwareLocator.DeviceName` meaning remains hardware identity and is not globally redefined.

### N1 - Network-device creation names

**Issue:** #76.

This PR corrects the `CreateWithItem` station/item argument mapping and verifies station, CPU item,
and PLC software identity with deliberately different requested names. It owns
`NetworkDeviceCreator` and focused creation tests only.

It should merge before later live network acceptance uses `add_network_device` to construct a
fixture. Existing correctly stated public documentation need not be rewritten merely to generate a
diff.

## Parallel qualification tracks

Read-only API and source qualification may run while Wave 1 is implemented. These tracks do not
become behavior-changing PRs until their decisions are reviewed:

- **Q75:** qualify the explicit caller-supplied OB-number contract by determining CPU/event-class
  ranges and collision behavior. A fixed magic number or automatic allocator is not acceptable.
- **Q77:** determine the documented V21 owner and auto-generation behavior for the PN device-name
  setting, then select the public partial-commit result policy.
- **Q74:** establish whether V21 provides a documented standalone PLC RUN/STOP operation. Download
  callbacks are not accepted as a substitute.
- **Q82.8:** establish a non-mutating freshness signal or an authoritative additional fingerprint
  for source-form safety reads. Automatically compiling during `get_block_content` is excluded.

Any live probe remains outside the authority of this design and requires fresh exact-target
authorization immediately before execution.

## Wave 2 - independent mutation and policy repairs

Each Wave 2 lane starts after all of its direct prerequisites shown in the dependency graph have
merged and any qualification track applicable to that lane has been approved.

### B2 - Deterministic block-creation contract

**Issues:** #75, #82.3, and the block/group target portion of #78.

This PR introduces one deterministic OB-number contract, validates it before import, makes the
accepted canonical block-path form explicit, and ensures previews identify the exact target,
number, and creation/deletion consequence. If shorthand remains unsupported for writes, schema,
error text, and maintained documentation say so consistently.

Likely ownership includes the block source generator and validator, block mutation coordination,
the relevant batch/worker request fields, structural safety snapshots, and the block/group branch
of `BatchSafetySnapshot`. The detailed plan implements a required explicit caller-supplied
`obNumber`, including the ranges and collision behavior established by Q75; it does not introduce
automatic allocation.

### I2 - Truthful block-import outcomes

**Issue:** #72.

This PR reports the known import and verification stages, bounded compile information from P1,
whether a mutation is known to have committed, and what an independent final-state read actually
establishes. An unavailable or unreliable post-read produces `unknown`, not an assertion that the
old or new block is present.

The minimum accepted repair is truthful outcome reporting under the existing non-atomic batch
contract. Automatic rollback is not part of this PR. Any later compensation design must describe
restore success, restore failure, and transport ambiguity separately and cannot claim atomicity.

Primary seams are `BlockImporter`, `BlockPostconditionEvidence`,
`BlockPostconditionVerifier`, and source/XML coordination tests. Raw Siemens exceptions and local
paths remain redacted.

### N2 - Network configuration partial outcomes

**Issue:** #77.

After Q77 identifies the correct setter surface, this PR ensures an unmet requested setting cannot
be hidden by an earlier successful subnet/IP/mask change. The result distinguishes requested,
applied, skipped, post-read, and committed state and stops later batch items according to the
reviewed policy.

It owns `NetworkDeviceConfigurator` and the corresponding typed result/payload contract. It does
not redesign selectors; N3 follows after this result contract stabilizes.

### T2 - Type-content binding policy

**Issue:** #82.1.

This bounded PR explicitly classifies `get_type_content` in `OperationPolicyCatalog`, aligns it
with the intended `get_block_content` policy, and corrects misleading binding guidance. It starts
after L1 because both may need `TiaPortalSession`; they must not edit that file concurrently.

## Wave 3 - source safety and network selector consolidation

### S3 - Block-source freshness and source-write drift safety

**Issue:** #82.8.

This PR implements the result of Q82.8. A read warning alone is insufficient if the same stale
source is used as both preview and apply state. The design must either fail source-form writes
closed when freshness is stale or unknown, or bind an independently authoritative fingerprint
while retaining source text for the human-readable diff.

The PR must preserve the read-only character of `execute_read_batch` and `get_block_content`; it
must not auto-compile. Likely seams include `BlockExporter`, a block-specific warning/freshness
helper, and source-write current-state acquisition.

### N3 - Network node selector and read-contract repair

**Issues:** #80, #82.4, #82.5, #82.6, and the network portion of #78.

This PR gives one owner to the overlapping node-read, selector, preview, and configuration seams.
It defines an indexed owner/node selector for duplicate node IDs, keeps the legacy short selector
only when it is unique, preserves null built-in type-identifier evidence without treating missing
identity as safe, and makes hardware discovery, network listing, inspection, preview, and apply
agree on selectability.

The same PR:

- suppresses or deduplicates expected unsupported hardware-attribute messages without hiding
  selector degradation;
- separates Ethernet IP addressing from neutral or PROFIBUS address representation;
- carries connected-subnet evidence consistently; and
- builds network preview targets/diffs from the final verified selector and applied-setting
  semantics established by N2.

Likely ownership includes `HardwareConfigReader`, `NetworkObjectIndexReader`,
`NetworkObjectSelectorResolver`, selector DTO/factory/decoder code, `NetworkIdentityResolver`,
`NetworkDeviceConfigurator`, network worker invocation, and the network operation reference.

S3 and N3 are intended parallel lanes, but they launch together only if their detailed plans prove
disjoint ownership of worker dispatch, `WorkerRequest`, `OpennessWorkerClient`, FakeWorker, and
shared documentation. If either plan requires the same file, S3 merges first and N3 is refreshed
from that `main` before implementation.

## Wave 4 - generic batch reviewability and dependencies

### G4 - Remaining generic previews and dependent-item rejection

**Issues:** the remaining generic-batch portion of #78 and the narrow fail-closed repair for #79.

This PR runs after B2 and S3 so it can use final block-path, numbering, and source-state semantics.
It adds deterministic requested-value summaries and bounded typed current-to-requested diffs for
tag, constant, block, group, and other supported generic operations that have authoritative
snapshots.

For #79, the approved bounded behavior detects known producer-to-consumer combinations before
state reads, returns an actionable validation error naming both operation IDs, and issues no token.
It documents the split-round workflow. It does not claim that every batch item must be independent,
because supported sequences such as child-before-parent deletion can remain valid. A future symbolic
sequential-state projection is a separate architectural feature.

This PR owns the generic `BatchSafetySnapshot`, `BatchPreviewDiff`, dependency analysis, and the
corresponding `WriteBatchTools` validation seam after earlier users of those files have merged.

## Wave 5 - PLC online capability resolution

### O5 - Fail-closed or safely qualified PLC mode control

**Issues:** #74, the online portion of #82.2, and the start/stop portion of #78.

This PR follows G4 because both may edit generic batch preview and safety-snapshot code. Its shape
is determined by Q74:

- if a documented standalone V21 RUN/STOP API with a verifiable state model exists, implement it
  against an exact unique CPU `DeviceItem`, bind the route and current state into preview/apply,
  reject ambiguity and drift, and return the actual PLC software identity; or
- if no such API is established, fail closed and correct the advertised capability. Do not invoke
  download merely to obtain start/stop callbacks.

Any enabled control requires separate authorization for one exact disposable CPU or PLCSIM target
and authoritative before/after state evidence. Offline tests and installed XML metadata cannot
prove a physical operating-mode effect.

## Dependency graph

```text
L1 -------------------------------> T2
K1 ----+--------------------------> I2
       +--------------------------> G4 ----------------> O5
P1 -------------------------------> I2
N1 -------------------------------> N2 ----------------> N3
B2 -------------------------------> G4
S3 -------------------------------> G4
Q74 ---------------------------------------------------> O5
Q75 -------------------------------> B2
Q77 -------------------------------> N2
Q82.8 -----------------------------> S3
```

The urgent L1 PR may merge as soon as it passes review and verification; it does not wait for other
Wave 1 lanes. The same applies to any independent lane.

## Detailed planning cadence

Approval of this specification authorizes detailed planning only after the written document itself
is reviewed. It does not authorize implementation.

Each implementation lane receives its own detailed plan. Wave 1 plans may be prepared together
because their boundaries have already been reviewed and are intended to be disjoint. Plans for a
later lane are written from the exact merged `main` after its direct predecessors and evidence are
reviewed. This preserves just-in-time technical detail without returning to a fully serial delivery
queue.

Every lane plan must include:

- exact production, contract, test, fixture, and documentation allowlists;
- explicit files reserved by other active lanes;
- the first focused RED test and expected failure;
- the smallest GREEN behavior and non-goals;
- public-schema, access-mode, safety-token, audit, budget, and compatibility effects;
- offline, stub-reference, real-reference, packaging, and documentation checks appropriate to the
  scope;
- whether live V21 evidence is required, read-only, or mutating; and
- merge, issue-reference, and post-merge integration gates.

If a merged predecessor changes a later lane's assumptions, discard the stale plan and write a new
one. Do not mechanically rebase an invalid design.

## Testing and integration strategy

Behavior changes follow focused TDD where feasible:

1. add the narrowest deterministic test for the reported behavior;
2. observe the expected RED caused by the missing or incorrect behavior;
3. implement the smallest GREEN within the lane's allowlist;
4. refactor only with the focused tests green;
5. run the relevant integration/FakeWorker/protocol slice;
6. run serial solution build and full tests with TIA reference stubs;
7. run real-V21-reference builds where installed assembly compatibility is affected;
8. review payload budgets, schemas, failure categories, safety hashes, pinned leases, audit, and
   postcondition behavior; and
9. run `git diff --check` and review the exact scoped diff.

The TDD RED is never a live TIA mutation.

After every merge, run a current-main integration gate before merging the next ready PR. At the end
of each wave, reconcile shared maintained documentation and rerun the complete serial offline gate.
A clean PR test result from a pre-merge base is not sufficient current-main evidence.

## Documentation and issue lifecycle

Each PR updates maintained documentation that it exclusively owns. When two concurrently prepared
PRs need the same maintained document, neither edits competing sections. A short fan-in PR from the
new merged `main` reconciles that shared document before the wave is considered complete.

The principal shared documentation hotspots are `README.md`, `docs/ARCHITECTURE.md`,
`docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, and
`docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`.

Individual slice PRs use `Refs #78` or `Refs #82`. They do not use `Closes` for either umbrella
issue. The last PR that completes and verifies every checklist slice may close the umbrella issue.
A single-issue PR may use `Closes` only when its complete contract, maintained documentation, and
required evidence are present.

This historical design remains indexed from `docs/README.md` and `docs/superpowers/README.md`.
Current behavior remains authoritative only in code, `docs/ARCHITECTURE.md`, and the supported
operation references.

## Live TIA Portal safety and evidence

Parallel source branches and worktrees do not isolate the external TIA Portal process, open
project, PLC, PLCSIM instance, network device, or filesystem destination. Therefore all live TIA
work is serialized even when implementation is parallel.

- A detailed plan or approved PR does not authorize a live operation.
- Obtain fresh authorization immediately before any mutating run, naming the exact disposable
  `.ap21`, selector, requested change, and restoration or discard route.
- Read-only live checks still use an explicitly identified project and must not be described as
  mutation evidence.
- Capture baseline state before mutation and authoritative post-read, audit, project status, and
  restoration/discard evidence afterwards.
- A timeout, worker crash, or lost response after a possible mutation is an unknown outcome.
  Inspect before retrying and never replay automatically.
- Stub, FakeWorker, static source, reference builds, and historical acceptance reports remain
  offline or historical evidence; they are never relabelled as current live proof.
- PLC RUN/STOP, download, save, and physical device effects receive their own exact-target approval
  and cannot borrow authorization from a project-file mutation.

## Non-goals

- Continuing Network roadmap Phase 5/6 implementation before the relevant bug program has reached
  a stable boundary.
- Combining all bugs into one release, one branch, or one large pull request.
- Replacing existing canonical JSON, operation-batch, safety-token, binding-lease, or audit
  infrastructure without a defect that requires it.
- Introducing automatic saves, downloads, project closes, retries, or rollbacks.
- Treating best-effort compensation as an atomic transaction.
- Adding undocumented Siemens APIs or using download as a PLC mode-control workaround.
- Supporting dependent generic-batch items through fabricated preview state in G4.
- Auto-compiling during a read-only block-content operation.
- Running or publishing any live harness, GitHub PR, merge, release, or NuGet package as part of
  this design-only step.

## Current verification boundary

This document records the approved in-chat delivery design in durable form. The issue audit was
read-only; no production code, tests, GitHub issue state, TIA Portal project, PLC, or PLCSIM state
was changed. No automated build or test run is claimed by this design-only change.

The next gate is user review of this written specification. After approval, the writing-plans
workflow may produce separate Wave 1 implementation plans for L1, K1, P1, and N1. Implementation
does not begin until those plans and their execution method are reviewed and approved.
