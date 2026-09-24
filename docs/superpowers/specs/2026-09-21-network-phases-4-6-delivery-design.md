# Network Phases 4-6 Completion and Delivery Design

**Date:** 2026-09-21

**Status:** Written design approved; implementation planning proceeds pull request by pull request

**Source:** Phase 4 implementation audit and the approved Phase 5/6 design discussion on
2026-09-21

**Delivery:** One serial, just-in-time train of separate pull requests. Only PR 1 receives a
detailed implementation plan now. Every successor is planned from the exact merged `main` after
the preceding pull request and its acceptance evidence have been reviewed. Release follows only
after every planned pull request has merged into `main`.

## Goal

Finish the current Network Phase 4 implementation, add typed IO-system editing in Phase 5, add
strictly allowlisted scalar network-attribute writes in Phase 6, prove each shipped feature against
TIA Portal V21, and retire the repository's PowerShell live-test harnesses only after durable
evidence and an installed-package acceptance run exist.

The work preserves the existing two-tool public surface:

- `network_read` remains the read and inspection tool;
- `network_write` remains the only network mutation tool; and
- no new public MCP tool is added.

## Approved delivery requirements

1. The work is divided into separate, sequential pull requests. A later pull request starts from
   the latest merged `main` and does not absorb an earlier milestone merely because files overlap.
2. Every executable behavior change uses strict red-green-refactor TDD. The focused automated test
   must be observed failing for the expected reason before production code is changed.
3. Every pull request that ships a new or changed Openness behavior, public schema, safety binding,
   worker dispatch path, or packaged runtime behavior requires scope-specific live TIA Portal V21
   confirmation before it is merge-ready.
4. Offline tests, FakeWorker tests, source-contract tests, stub builds, and builds against installed
   V21 reference assemblies are necessary but are not live TIA evidence.
5. Live mutations use a freshly identified disposable project copy and require fresh explicit
   authorization for that exact target immediately before execution. Approval of this design or a
   later implementation plan does not authorize a live run.
6. The user, not the implementing agent, will restore the saved Phase 4 live harness from the
   external copy. The implementation must not recreate or substitute that script.
7. No feature pull request may publish a release, create a release tag, or publish a NuGet package.
   Release work begins only after the final planned pull request has merged into `main`.
8. The last pull request removes all repository live-test PowerShell scripts and their
   harness-specific tests, preserves durable evidence, and validates the exact candidate package
   through a locally installed MCP executable.

Documentation-only changes use link, format, and consistency checks rather than invented TDD
cycles. Executable harness changes are test-first: add or update the narrow parser/contract test,
observe the intended RED, then make the smallest script change.

## Planning cadence

This design fixes the delivery order and the durable product/safety decisions. It does not attempt
to predict every task in later pull requests before live evidence exists.

A successor pull request receives a detailed implementation plan only when all of these are true:

1. its immediate predecessor has merged into `main`;
2. the predecessor's final diff, acceptance report, and unresolved findings have been reviewed;
3. the new plan is based on that exact current `main`; and
4. every fixture, selector, allowlist entry, compile target, and restoration constraint required by
   the successor is known from merged evidence rather than assumption.

Every later plan must state its exact scope and non-goals, focused RED test, implementation seams,
offline gates, scope-specific live-TIA gate, durable-evidence requirement, and merge exit criteria.
No production or harness implementation starts until that plan is reviewed and approved. Plan
approval never authorizes a live TIA run; the exact disposable target still requires fresh
authorization immediately before the live activity.

If live evidence uncovers a product defect, stop the active plan. Create and merge a dedicated TDD
repair pull request from current `main`, review its evidence, and then write a fresh successor plan
from the repaired `main`. Do not carry a now-stale downstream plan forward.

## Shared architecture and safety constraints

- The net10.0 host never loads Siemens Openness assemblies. All Siemens calls remain in the net48
  worker over newline-delimited JSON.
- Reuse `StructuredToolResult`, `StructuredOperationBatch`, `CanonicalJson`, and
  `CanonicalWriteSafety`. Do not introduce a second canonical JSON, batching, token, or audit
  mechanism.
- Public text content and `structuredContent` come from the same canonical serialization.
- Worker success payloads are strictly typed. Missing required members, extra members, wrong
  types, and unrepresentable values become `protocol_error` and are not echoed to clients.
- Preview and apply use unchanged ordered operations. Apply rereads fresh state under the pinned
  project-binding lease before consuming the single-use token.
- Batches remain sequential and non-atomic. They stop at the first failed operation; earlier
  committed operations remain committed and later operations are skipped.
- Every mutation resolves an exact Phase 3 selector. Name-only selection and first-match fallback
  remain forbidden.
- Mutations use one `ExclusiveAccess` scope and one transaction where the API permits it.
  `CommitOnDispose()` is requested only after all preconditions and setters succeed.
- Project save, download, commissioning, PLC mode control, online-path configuration, and plant
  acceptance remain outside this program.
- Ambiguous transport failure after a mutation is an unknown outcome. Return inspect-before-retry
  guidance and never retry automatically.

## Pull-request train

The pull request numbers below are program ordinals. A conditional pull request may be omitted when
qualification evidence proves that it has no safe supported scope; later ordinals then move
forward. Any inserted repair follows the planning cadence above rather than being patched
opportunistically inside an evidence-only step.

### PR 1 - Finish Phase 4 and establish current-revision live acceptance

This pull request closes the known static discrepancies and restores reproducible current-revision
acceptance.

The user first restores `scripts/live-test-network-phase4-subnets.ps1` from the saved external
copy. Before any adaptation, record its source provenance and SHA-256. The implementing agent may
review and update the restored file only after a failing harness-contract test demonstrates the
current incompatibility.

Production fixes:

1. Require raw presence of all four subnet-lifecycle success members, including
   `networkDeviceCount`, before typed normalization.
2. Require exact ordinal `target.kind == "subnet"` for update and delete.
3. Resolve delete once inside the transaction, validate Ethernet/PROFIBUS applicability, capture a
   nonblank name from that same object, delete it, and then commit.
4. Normalize late worker zero/multiple target drift to the documented public failure category, or
   document and test a deliberately different category.
5. Correct roadmap and supported-operation statements that claim no known discrepancy or obscure
   the existence and current status of historical Phase 4 evidence.

The restored harness retains a safe non-mutating default, Inventory and Preview modes, and a
double-gated Apply mode. Apply requires both an explicit mutation switch and the exact destructive
confirmation phrase. It must use the public MCP route, redact tokens, record the tested commit and
TIA version, and prove that subnet lifecycle operations do not change the root device count.

Merge gate:

- focused RED/GREEN evidence for each production defect;
- focused and full test suites;
- serial reference-stub and real-V21-reference builds;
- static harness-contract tests;
- current-revision public Inventory, Preview, and separately authorized Apply evidence on a
  disposable fixture; and
- a durable Phase 4 acceptance report.

### PR 2 - Qualify Phase 5 fixtures and freeze the compile contract

**Planning entry gate:** PR 1 is merged, its Phase 4 acceptance report is reviewed, and the plan is
written from that exact `main`.

**Guidelines:** This is an evidence-and-contract pull request, not a public feature. Qualify real
PROFINET IO-system and DP master-system selectors, metadata, safe mutation/restoration values, and
the narrowest hardware compile target. Modeled `Name` and `Number` remain the baseline. The only
non-deferred PROFINET dynamic candidates are `MultipleUseIoSystem`,
`UseIoSystemNameAsDeviceNameExtension`, and `MaxNumberIWlanLinksPerSegment`, and each remains
excluded unless live qualification proves its exact metadata, applicability, safe value, and
restoration. No additional DP dynamic attribute enters scope without a spec amendment.

**Merge exit gate:** Current-main fixture, metadata, selector, compile, and restoration evidence is
durably recorded; every candidate is explicitly approved or excluded; and the exact Phase 5
contract is reviewed before planning the first public slice.

### PR 3 - Add the Phase 5 PROFINET modeled vertical slice

**Planning entry gate:** PR 2 is merged and its qualification evidence is reviewed.

**Guidelines:** Add `update_io_system` under `network_write` for modeled PROFINET `Name` and
`Number`, using the exact Phase 3 IO-system selector. Changing a modeled identity must return the
new canonical selector while safety remains bound to the exact old selector and current values.

Compile cannot run inside an Openness transaction. Therefore a compile failure may occur after the
mutation commits. The durable contract is a failed operation with bounded typed evidence including
`mutationCommitted: true`, the post-write selector, applied before/after state, bounded compile
counts/messages, and `postcondition_failed`. There is no compensating rollback or automatic retry.
Transport loss before the worker response remains an unknown outcome and requires inspection.
Ordinary failures continue to have `result: null`; only this reviewed known-committed failure class
may carry a typed result beside `failure`.

**Merge exit gate:** Strict TDD, full offline/reference gates, current-main public preview/apply and
post-read, stale-state rejection, compile-success and bounded known-committed compile-failure
evidence, batch-stop/audit proof, and restoration or disposable-copy discard all pass.

### PR 4 - Add the Phase 5 DP modeled vertical slice

**Planning entry gate:** PR 3 is merged, its PROFINET evidence is reviewed, and a safe real DP
fixture exists.

**Guidelines:** Extend the same operation and failure semantics only with DP applicability and
constraints proven by PR 2. Do not simulate missing DP evidence or widen the selector contract.

**Merge exit gate:** The DP slice passes strict TDD and its own public-path live gate, including
restoration and PROFINET regression. Without a safe DP fixture, this pull request does not merge and
DP support remains explicitly pending.

### PR 5 - Add the Phase 5 PROFINET dynamic attribute slice when qualification permits

**Planning entry gate:** PR 4 is merged and reviewed, and PR 2 approved at least one non-deferred
PROFINET dynamic attribute. If none was approved, record the exclusion and close Phase 5 after the
modeled slices; this pull request is omitted.

**Guidelines:** Add only the exact approved attributes through a closed scalar vocabulary. Modeled
`Name` and `Number` stay outside the dynamic collection. All requested attributes and current
metadata/values are safety-bound and prevalidated before the first setter; one transaction applies
the set, and hardware compile follows commit with the PR 3 failure semantics.

**Merge exit gate:** Every shipped attribute has strict TDD and its own current-main public live
case for applicability, type/access rejection, stale state, all-or-none precommit validation,
compile outcome, and restoration. Aggregate testing supplements but never replaces each feature's
live gate.

### PR 6 - Qualify and freeze the Phase 6 allowlist

**Planning entry gate:** Phase 5 is closed, all shipped Phase 5 pull requests are merged, and their
acceptance evidence is reviewed.

**Guidelines:** Rerun current-main metadata discovery for `networkInterface`, `node`, `subnet`, and
`communicationConnection`. Metadata `ReadWrite` is necessary but not sufficient. The policy is a
positive closed allowlist of independently readable, normalizable, safety-bindable, settable, and
read-back-verifiable leaf scalars. Exclude identities, relationships, selectors, endpoints, fields
owned by typed operations, timing/watchdog/real-time/synchronization/isochronous fields,
connection lifecycle/partner reassignment, and online/runtime state. A kind may legitimately have
zero entries. Communication connections remain excluded while selector or endpoint identity is
incomplete.

**Merge exit gate:** A current-main matrix records each approved tuple's exact kind, ordinal name,
CLR/wire type, complete selector, semantic safety rationale, typed-operation conflicts, fixture,
safe requested value, and restoration procedure; every other candidate is explicitly excluded.

### PR 7 through PR N - Ship Phase 6 as independent vertical slices

**Planning entry gate:** PR 6 is merged and reviewed. Each slice is planned separately from the
current merged `main` for exactly one approved object-kind/attribute family.

**Guidelines:** The public operation is `set_network_attribute` under `network_write`; one
operation writes one allowlisted scalar to one exact Phase 3 selector. `inspect_network_object`
remains the read API. The first slice establishes the shared strict codec, safety snapshot,
transactional setter, post-read, typed payload, canonical preview/apply, audit, budgeting, and
stop-on-first-failure seams. Later slices extend only the frozen closed policy. Product operations
do not automatically save, download, commission, or compile; live acceptance compiles separately
only when qualification requires it.

**Merge exit gate:** Each slice passes its focused RED/GREEN cycle, full offline/reference gates,
and its own current-main public inspection/preview/stale-token/apply/post-read/audit/unrelated-target
comparison/restoration gate. No slice borrows another slice's live evidence.

### Final PR - Retire live harnesses and validate the locally installed MCP

**Planning entry gate:** Every feature pull request is merged, every acceptance report is reviewed,
and the exact current repository inventory is used to derive the deletion manifest. Do not freeze
that file list earlier because later work may add or remove harness-only artifacts.

**Guidelines:** Delete every `scripts/live-test-*.ps1`, every program-created
`scripts/live-probe-*.ps1`, their harness-only tests and fixtures, and active documentation links.
Before deleting a contract test, migrate any general protocol, schema, safety, or normalization
invariant into an ordinary C# test. Retain ordinary unit/integration/FakeWorker/protocol/package
checks and the non-live `verify-doctor-package.ps1` and `verify-coverage-threshold.ps1` scripts.
Keep immutable evidence and Git history, but do not preserve an executable harness copy under
another extension.

Pack the exact candidate, install it to an isolated `--tool-path`, and validate version, doctor,
MCP initialization, tool metadata/schema, and benign reads from that installation. With separate
authorization, run the complete Phase 4-6 matrix through a normal MCP client without creating a
replacement reusable harness. This final matrix supplements, and never replaces, each feature
pull request's earlier live gate.

**Merge exit gate:** The deletion/invariant-migration audit is complete; full offline, coverage,
stub, real-reference, documentation, package-layout, isolated-install, protocol, and authorized
installed-package live gates pass; and a traceable report proves the launched host/worker came
from the exact package. Any failure produces a separate repair pull request and blocks release.

## Feature pull-request state machine

Every behavior-changing pull request follows this order:

1. write one focused behavioral test;
2. run it and observe the expected RED caused by missing behavior;
3. implement the smallest GREEN;
4. rerun the focused test;
5. refactor only while focused tests remain green;
6. run the relevant integration slice and complete test suite;
7. run serial stub and real-V21-reference builds;
8. review schema, access-mode, safety, typed-payload, audit, and payload-budget effects;
9. run the scope-specific live public-path acceptance on the final implementation tree;
10. commit only sanitized evidence/documentation that does not change production, tests, or the
    harness; and
11. review the final diff before the pull request becomes merge-ready.

A live mutation is never the TDD RED. RED is an automated unit, contract, integration, or
FakeWorker test.

Any production, automated-test, or harness-source change after a live run invalidates that run for
merge acceptance. Evidence-only documentation changes are allowed only when a tree audit proves
the tested implementation and harness are unchanged. Rebasing a feature branch onto a newly
merged prerequisite requires the relevant offline and live gates to run again.

## Live test safety and evidence

- Inventory and Preview are the default modes.
- Apply requires explicit authorization naming the disposable project and exact selectors.
- Tests never target a production project.
- Baseline state is captured before mutation.
- Successful mutation is followed by authoritative post-read and restoration or disposable-copy
  discard.
- An ambiguous timeout or worker crash triggers inspection, not replay.
- Secrets, safety tokens, local credentials, and private machine details are redacted.
- Historical evidence is labelled with its tested commit and never promoted to current evidence.

For Phase 4, deleting connected subnets retains the approved exception: the operation does not
delete devices. The acceptance run proves the root device count is unchanged.

For Phase 5, compilation is part of the product operation after transaction commit. For Phase 6,
compilation is acceptance evidence where applicable, not an automatic public-operation side
effect.

## Documentation and retained evidence

Each feature pull request updates the current documentation it changes:

- `docs/ARCHITECTURE.md` for host/worker, safety, transaction, compile, or policy seams;
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`;
- a focused Phase 5 or Phase 6 supported-operation reference;
- `docs/roadmap/network-operations.md`;
- `docs/IMPROVEMENT_LOG.md`; and
- `docs/README.md` whenever a new document is added.

Historical specs, implementation plans, and acceptance reports remain historical. Current
behavior is described only by architecture and supported-operation documentation.

## Release gate

No release branch, version bump intended for publication, release tag, NuGet publication, or
GitHub release is created before the final cleanup pull request has merged into `main`.

After the final merge:

1. record the exact `main` merge SHA;
2. rerun the complete serial offline verification on that SHA;
3. pack and install the exact local package from that SHA;
4. repeat non-mutating installed-package startup/protocol checks;
5. confirm its hash matches the candidate selected for release; and
6. only then create the release tag and publish.

The release is blocked if any planned pull request remains unmerged, any required live report is
missing or stale, the exact-main package differs from the accepted candidate, or post-merge
verification fails.

## Explicit non-goals

- No automatic saving of the TIA project.
- No download, commissioning, online-path selection, PLC mode control, or runtime assertion.
- No generic write fallback for unknown metadata-writable attributes.
- No Phase 6 structural/topology/lifecycle mutation disguised as a scalar write.
- No transfer-area, address/channel, timing/watchdog, real-time, synchronization, send-clock, or
  isochronous implementation.
- No connection creation/deletion or endpoint reassignment.
- No promise that an offline, stub, FakeWorker, reference build, or historical artifact proves
  current live behavior.
- No release as part of a feature or cleanup pull request.

## Acceptance criteria

- Phase 4's known static defects are fixed with RED/GREEN evidence.
- The user-restored Phase 4 harness is reviewed, contract-tested, and used for current-revision
  public live acceptance.
- Phase 5 ships modeled PROFINET and DP editing plus only live-qualified non-deferred PROFINET
  dynamic attributes.
- Phase 5 reports post-commit compile failure truthfully with `mutationCommitted: true` and bounded
  applied-state/compile evidence, without rollback or retry.
- Phase 6 exposes one strict `set_network_attribute` operation backed only by a reviewed closed
  allowlist and ships each attribute family in a separate live-qualified pull request.
- Every behavior-changing pull request follows strict TDD and passes its own live TIA gate.
- Durable acceptance reports remain after all live-test PowerShell scripts and their
  harness-specific tests are removed.
- The final pull request validates an exact locally installed package through the MCP protocol and
  the complete authorized live feature matrix.
- All planned pull requests merge into `main` before release work begins.
