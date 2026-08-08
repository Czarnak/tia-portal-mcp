# VCI Workspace Operations Phase 1 Live Characterization and Contract Baseline Design

Status: Approved on 2026-08-08. Implementation and live execution have not started.

## Objective

Phase 1 establishes an evidence-backed contract and safety baseline for TIA Portal V21 Version
Control Interface (VCI) workspace operations. It does not publish `workspace_read` or
`workspace_write`.

The phase must:

- characterize successful and unsuccessful V21 VCI calls against a live TIA project;
- keep compile-time, vendor-free, and live evidence distinct;
- characterize read-only behavior before designing mutation probes;
- characterize mutations only in disposable project copies and confined workspace roots;
- record deterministic project, VCI, exception, session-health, and filesystem evidence;
- derive selectors, public error categories, typed envelopes, and write-safety inputs from that
  evidence; and
- preserve explicit gaps wherever behavior is not proved live.

The user confirmed that the installed V21 API and the prior metadata findings remain current.
Metadata establishes the available API shape, but it does not establish runtime return values,
exceptions, side effects, transaction behavior, or repeatability.

## Relationship to the Roadmap

This design implements Phase 1 of `docs/roadmap/workspace-operations.md`: lock the VCI domain
contract and safety baseline before delivering public operations.

The roadmap's approved public direction remains unchanged:

- `workspace_read` will eventually contain ordered, typed VCI read operations.
- `workspace_write` will eventually contain ordered, typed, preview-before-apply VCI mutations.
- Both tools will reuse the shared canonical JSON, structured-result, operation-batch, and
  canonical write-safety infrastructure.

Neither public tool is registered during this phase. Operation-specific public members are locked
only in the later phase that introduces the corresponding operation.

## Approved Decisions

- Use internal worker probes rather than a standalone Openness executable or provisional public
  tools.
- Implement and execute the read-only probe first.
- Review read-only live evidence before writing the mutation probe implementation plan.
- Implement and execute the mutation probe second.
- Review mutation live evidence before implementing the public contract foundations.
- Include positive and negative cases in both live lanes.
- Keep safe runtime-invalid calls in the read-only lane.
- Keep state-dependent invalid calls in the disposable mutation lane.
- Treat deliberately observed method exceptions as probe results, not automatically as probe
  failures.
- Use one worker request per case so completed evidence survives a later worker or TIA failure.
- Do not expose arbitrary reflection, method names, or user-defined call sequences.
- Do not call `Project.Save` from either probe.
- Never automatically retry an uncertain VCI mutation.
- Split Phase 1 into three implementation plans separated by live-evidence review gates.

## Approaches Considered

### Internal worker probes — selected

The probes run through the real net48 Openness worker and installed Siemens assemblies. This uses
the production session, project, access-policy, serialization, and failure boundaries without
prematurely defining public MCP schemas.

### Standalone net48 probe executable — rejected

A standalone executable would be quick to build but would duplicate assembly loading, project
lifecycle, exception handling, and evidence normalization outside the eventual implementation
path. Its results could drift from worker behavior.

### Provisional public tools — rejected

Publishing temporary `workspace_read` or `workspace_write` operations would force schemas and
error semantics to be invented before the live behavior is known. That reverses the required
evidence-first ordering and creates public migration work.

## Scope

Phase 1 includes:

- the VCI service and workspace system-group entry point;
- nested workspace groups and workspace discovery;
- workspace properties and supported-format queries;
- mapped-object properties, individual status, child status, and comparison details;
- group, workspace, mapping, export, connect, synchronize, and delete characterization;
- both `ProjectToWorkspace` and `WorkspaceToProject` directions;
- positive, safe negative, and state-dependent negative cases;
- transaction-availability and rollback characterization;
- `Project.IsModified`, session-health, and filesystem-side-effect evidence;
- normalized repeatability comparisons;
- an installed-V21 capability matrix; and
- unregistered typed foundations for future public envelopes, selectors, errors, paths, and
  safety snapshots.

Phase 1 excludes:

- registering either public workspace tool;
- Git, indexing, search, rich diffs, or arbitrary workspace-file editing;
- automatic project save or compile;
- Multiuser, project-server, Teamcenter, or Add-In provider workflows;
- deliberate TIA process termination;
- access-right or credential manipulation;
- operating-system fault injection;
- automatic cleanup of live evidence; and
- runtime claims derived only from stubs, FakeWorker tests, metadata, compilation, or prose.

## Negative-Test Layers

Negative coverage is split by what can safely and meaningfully reach the installed Openness API:

- Contract-invalid inputs, including wrong CLR/JSON types, unknown fields, unknown cases, and
  missing required request members, are rejected in vendor-free contract tests. A strongly typed
  Openness method cannot characterize an argument that cannot be constructed for its signature.
- Runtime-invalid but safety-confined inputs are invoked live and recorded. Examples include null
  values accepted by the signature, unknown names, unsupported engineering objects, invalid enum
  values, collisions, stale mappings, and malformed files beneath the disposable workspace root.
- Boundary-escaping inputs, including absolute filenames outside the root, traversal, drive roots,
  profile roots, repository roots, and symlink or reparse-point escapes, are rejected by the
  harness before Openness invocation. These prove the application boundary, not Siemens behavior.

A path-invalid live case may reach Openness only when its canonical resolution remains inside the
dedicated run boundary. The evidence identifies whether a negative result came from request
validation, harness confinement, worker validation, or the Siemens method.

## Delivery and Review Gates

### Gate 1: Read-only probe implementation

Add the typed read-probe contracts, closed case catalogue, worker service, dispatch, access-policy
classification, evidence normalization, PowerShell harness, and vendor-free tests. Tests are
written and observed failing before production changes.

### Gate 2: Read-only live evidence

Run the probe twice in independently started worker sessions against the same unchanged project.
An empty VCI hierarchy is valid evidence. Review the complete evidence before designing the
mutation implementation.

The gate fails when `Project.IsModified` changes, a discovered workspace file changes, evidence
is incomplete, the session outcome is uncertain, or the normalized repeatability comparison has
an unexplained difference.

### Gate 3: Mutation probe implementation

Write a separate implementation plan after Gate 2. It must select concrete objects, formats, and
preconditions from the approved read-only capability evidence. Implement the mutation probe with
the same TDD, typed-contract, closed-catalogue, and safe-harness requirements.

### Gate 4: Mutation live evidence

Run the approved cases against independent disposable project copies and confined workspace
roots. Run an equivalent second set for normalized repeatability. Rerun the read-only probe after
mutation to verify postconditions.

The gate fails on a filesystem escape, automatic retry, unrecorded cleanup, missing case result,
unexplained repeatability difference, or unresolved uncertain outcome.

### Gate 5: Contract and safety baseline

Write the third implementation plan after Gate 4. Add the capability matrix and unregistered
typed public-envelope, selector, error, path, and safety-snapshot foundations. Every supported
claim must cite live case evidence.

## Probe Operations and Access Policy

The worker exposes two internal operation names:

- `probe_vci_read_contract`, classified as `OperationCapability.Observe`;
- `probe_vci_mutation_contract`, classified as `OperationCapability.ProjectMutation`.

The mutation operation requires worker-level `confirm=true`. Both operations remain absent from
the public MCP catalog and generic batch-operation catalog.

`WorkerRequest` receives one optional nested `VciProbeRequestInfo` property rather than a new set
of flat VCI fields. The nested request includes the schema version, run ID, closed case ID,
selectors, workspace boundary, budgets, and typed case inputs. Unknown fields, versions, case
IDs, and inapplicable inputs fail closed.

## Harness Modes and Authorization

The mutation harness supports:

- `Describe`: the default; reports the schema, inputs, cases, acknowledgement, and safety rules
  without opening TIA Portal;
- `Inventory`: validates the disposable project, selectors, workspace boundary, candidates, and
  ordered case plan without mutation;
- `Apply`: performs the approved mutations.

`Apply` requires:

- an explicit disposable project path;
- a dedicated, canonicalized, run-specific workspace root;
- `-AllowMutation`;
- the exact typed acknowledgement defined by the harness contract;
- display of the resolved project, root, selected objects, ordered plan, and plan hash; and
- interactive confirmation unless a separately explicit non-interactive acceptance switch is
  supplied.

The harness rejects drive roots, user-profile roots, repository roots, unrelated existing
directories, ambiguous project identities, path traversal, and symlink escapes.

The read-only harness has `Describe` and `Run` modes. `Run` accepts any user-selected project. A
missing VCI service, empty group tree, absent workspace, absent mapping, or absent candidate type
is recorded rather than treated as an infrastructure failure.

## Mutation Isolation

- The mutation harness never opens the original project.
- Each state-dependent scenario family receives an independent disposable project copy and
  workspace root.
- The user supplies the disposable copies; the harness does not guess how to clone a TIA project.
- Generated files remain available until post-state evidence is complete.
- Cleanup is a separate explicit action confined to paths created by the run. Retention is the
  default.
- The probe records `Project.IsModified` but never calls `Project.Save`.
- A timeout, lost TIA process, incomplete filesystem snapshot, or indeterminate VCI state stops
  the current scenario family.
- Transaction behavior is measured operation by operation and is never assumed to cover
  workspace-file effects.
- The workspace `Create(name)` overload without an explicit root remains compile-time-only unless
  a later separately approved probe can prove and authorize its resolved filesystem boundary.

## Evidence Bundle

Each live run writes an uncommitted evidence bundle beneath:

`artifacts/live-vci-phase1/<run-id>/`

The bundle contains:

- `manifest.json`: probe version, run ID, UTC times, TIA and project versions, Siemens assembly
  versions and hashes, process mode, culture, project-copy identity, workspace-root identity, and
  isolation mode;
- `cases.jsonl`: one flushed record per invocation;
- `snapshot-before.json` and `snapshot-after.json`: normalized VCI hierarchy, workspace
  properties, mappings, comparison states, project modification state, and session health;
- `filesystem-before.json` and `filesystem-after.json`: confined relative paths, sizes, and
  SHA-256 hashes; and
- `summary.json`: outcome counts, invariant violations, omissions, repeatability differences, and
  overall status.

Absolute local paths, timestamps, durations, and process IDs are volatile provenance. Normalized
comparison payloads use stable project and workspace placeholders. Evidence remains local unless
the user explicitly selects an acceptance report or sanitized evidence for version control.

## Case Result Contract

Each `VciProbeCaseResultInfo` records:

- stable case ID, probe kind, method, input category, and sanitized arguments;
- preconditions and safety invariants;
- outcome: `returned`, `returned_null`, `not_observable`, `threw`, `timed_out`, or
  `process_lost`;
- runtime return type and normalized value description;
- CLR exception type, Siemens exception category, HRESULT when available, and structured
  diagnostic details;
- raw localized messages as observations only;
- project and filesystem state before and after the call;
- a post-call canary read; and
- omissions with explicit reasons.

A deliberately invoked Siemens method that throws can still produce a successful worker response:
the probe successfully captured the behavior. Malformed evidence, worker protocol failure,
project/session loss outside the characterized case, or an invariant violation fails the probe
operation itself.

When the worker times out or is lost before returning a typed result, the harness writes the
terminal `timed_out` or `process_lost` record from its transport evidence and stops the scenario
family. It never invents missing Siemens return or exception details.

## Read-Only Case Matrix

The read-only service never invokes creation, deletion, connection, export, synchronization,
property setters, or project save.

### Positive cases

- `R-SVC`: obtain `VersionControlInterface`, read `WorkspaceGroup`, and record nullability and
  runtime types;
- `R-GRP`: recursively enumerate system and user groups, parents, ordering, duplicates, and
  counts;
- `R-WS`: enumerate workspaces and read every exposed property and mapped-object count;
- `R-FMT`: call `GetSupportedFileFormats` for a bounded inventory of available engineering-object
  types and record runtime collection type, values, casing, ordering, duplicates, and repeat-call
  stability;
- `R-MAP`: read mapped-object properties, `Status`, `GetStatus()`, and `GetChildStatus()` without
  reducing the result to a boolean;
- `R-REP`: repeat selected successful calls in the same session; and
- `R-CANARY`: perform a harmless project and VCI read after every negative call.

### Safe negative cases

- find nonexistent groups and workspaces;
- use empty, whitespace, and null names where the installed signatures permit them;
- call `GetSupportedFileFormats(null)`;
- pass a compile-time-compatible but unsupported engineering object;
- optionally pass an object from a separately supplied secondary project;
- observe pre-existing missing or inaccessible mapped files without creating that state; and
- read when optional VCI configuration, global-library files, groups, workspaces, or mappings are
  absent.

Unavailable preconditions produce `not_observable`, not fabricated results or probe failures.
Enumerations are bounded and report complete counts, returned counts, omissions, and reasons.

## Mutation Case Matrix

### Positive cases

- `M-GROUP`: create a user group and nested group, then verify return types, hierarchy, ordering,
  and project modification state;
- `M-WORKSPACE`: create workspaces using explicit-root and explicit-root-plus-language overloads,
  then read back all installed writable properties;
- `M-EXPORT`: query live-supported formats, export the selected object, and capture method result,
  mapping, complete generated file set, hashes, comparison state, and project state;
- `M-DISCONNECT`: invoke the installed mapped-object deletion operation and independently
  determine its mapping and file effects;
- `M-CONNECT`: connect retained or separately seeded workspace content and inspect the mapping and
  status;
- `M-P2W`: make a controlled project-side fixture change, synchronize `ProjectToWorkspace`, and
  inspect status and filesystem changes;
- `M-W2P`: make a controlled valid workspace-file change, synchronize `WorkspaceToProject`, and
  verify the corresponding project object and status;
- `M-DELETE`: delete mappings, workspaces, and groups in dependency-safe order; and
- `M-TX`: exercise representative VCI mutations inside non-committing transactions and determine
  which calls are rejected, rolled back, or leave external filesystem effects.

The mutation implementation plan must select the controlled project object and valid file edit
from Gate 2 evidence. Phase 1 does not assume a universal object type or format.

### State-dependent negative cases

- null, empty, whitespace, duplicate, and invalid group or workspace names;
- relative, nonexistent, conflicting, and file-valued workspace paths whose canonical resolution
  remains inside the run boundary;
- null or invalid language and global-library values where accepted by the installed signatures;
- null, unsupported, foreign-project, disposed, already-mapped, and deleted engineering objects;
- null, empty, unsupported, incorrectly cased, and mismatched file formats;
- invalid filenames, absolute filenames, traversal attempts, collisions, and partial file sets;
- connect against missing, malformed, wrong-object, or incomplete multi-file content;
- synchronize with missing, malformed, unchanged, project-only, workspace-only, both-sides-changed,
  and invalid-enum inputs; and
- delete non-empty containers, delete the same mapping twice, and access a stale mapped-object
  proxy after deletion.

Multi-file negative cases expand only after a live export proves that the selected format creates
multiple files.

## Components

The implementation uses focused components:

- shared netstandard2.0 request, result, snapshot, return-description, exception-evidence, and
  omission DTOs in `TiaMcpServer.Contracts`;
- a read-only case catalogue and `VciReadContractProbeService` in the net48 worker;
- a mutation case catalogue and `VciMutationContractProbeService` in the net48 worker;
- a shared deterministic VCI evidence normalizer;
- thin `Program.cs` dispatch methods that validate, acquire the session/project, and call one
  typed service;
- `OperationPolicyCatalog` entries consumed by both host and worker authorization;
- separate PowerShell read and mutation harnesses; and
- focused contract, dispatch, access-policy, service, path-boundary, normalization, and script
  tests.

The detailed implementation plans select exact filenames after mapping the existing worker and
test organization. Production Openness code remains in the net48 worker.

## Per-Case Data Flow

1. The harness validates paths, authorization, plan hash, and preconditions.
2. It starts the real worker and establishes one session for the scenario family.
3. It requests the pre-case VCI snapshot.
4. It sends one closed-catalogue case request.
5. The worker resolves engineering objects afresh, invokes the exact installed-V21 member,
   captures its outcome, runs the canary read, and returns a typed result.
6. The harness immediately flushes the result to `cases.jsonl`.
7. The harness captures the confined filesystem state and decides whether execution may continue.

The harness owns artifact writes. The worker writes neither evidence files nor arbitrary paths.
Worker stderr is inherited or handled without PowerShell callback script blocks; JSONL stdout
remains protocol-only.

## Capability Matrix

Each method, object type, and format combination receives one status:

- `compile_time_only`;
- `live_positive_confirmed`;
- `live_negative_confirmed`;
- `not_observable`;
- `unsupported`;
- `inconsistent`; or
- `uncertain`.

Every row links return shape, exception behavior, side effects, repeatability result, and evidence
case IDs. Only live-confirmed behavior can enter the supported public contract.

## Selector Rules

- Group selectors contain the complete parent chain plus evidence that distinguishes duplicate
  siblings.
- Workspace selectors contain the group selector, workspace identity, and canonical root
  identity.
- Engineering-object selectors use the stable V21 object identifier where supported; otherwise
  they use a typed structural path plus a current object fingerprint.
- Mapping selectors contain the workspace selector, engineering-object selector, normalized
  relative directory, filename, and format.
- Names alone never identify a write target.

Exact selector fields are locked from live identity and duplicate-name evidence during Gate 5.

## Write-Safety Snapshot

The future `workspace_write` safety token binds:

- project identity and current project-state fingerprint;
- group and workspace identity and relevant properties;
- engineering-object and mapped-object identity;
- comparison and mapping state;
- canonical workspace root and confinement evidence;
- the complete affected file set with relative paths and content hashes;
- requested operation, format, direction, and ordered inputs; and
- expected project, VCI, mapping, and filesystem effects.

Apply must fail when any bound evidence changes. Phase 1 does not claim that TIA and filesystem
effects are atomic.

## Public Error Mapping

Observed failures are mapped to deterministic public categories only when the live evidence
supports a stable distinction. Candidate categories include validation, not found, unsupported
object or format, conflict, filesystem conflict, access denied, state changed, protocol error,
and uncertain outcome.

Raw Siemens types, HRESULTs, and localized messages remain diagnostic evidence. An unstable or
unclassified live failure maps to a conservative general VCI failure rather than an invented
specific category.

## Vendor-Free Verification

The automated suite covers:

- typed request and result serialization;
- unknown version, operation, case, field, and inapplicable-input rejection;
- read-only access approval and mutation denial;
- mutation confirmation enforcement;
- dispatch to exactly one typed service;
- case-catalogue closure;
- project and workspace path confinement;
- deterministic normalization and omission behavior;
- synthetic return and exception evidence;
- canary and uncertain-outcome control flow;
- harness `Describe`, safe defaults, validation ordering, and acknowledgement checks;
- absence of VCI public-tool registration; and
- unchanged read-only and read-write public tool counts; and
- proof that ordinary tests never invoke either live harness.

The solution is built serially against stubs and against the installed V21 assemblies. These
builds and tests remain compile-time and vendor-free evidence, not live acceptance.

## Live Acceptance

Read-only acceptance requires two independently started runs against the same unchanged project,
unchanged project and workspace state, complete case coverage or explicit `not_observable`
results, healthy post-negative canaries, and an explained normalized comparison.

Mutation acceptance requires approved disposable assets, successful `Describe` and `Inventory`
gates, explicit authorization, complete positive and negative evidence, post-case canaries,
post-run read verification, confined file effects, no automatic retry, and an explained normalized
comparison across equivalent independent runs.

Every live gate ends with user review. No later plan begins until that review is approved.

## Documentation Deliverables

Phase 1 maintains:

- this approved design;
- a separate read-only implementation plan;
- a read-only live acceptance report;
- a mutation implementation plan written after the read evidence;
- a mutation live acceptance report;
- a contract/safety-baseline implementation plan written after mutation evidence;
- the installed-V21 capability matrix; and
- current documentation updates required by the final unregistered contract foundations.

Historical specs, plans, and acceptance reports are indexed through `docs/superpowers/README.md`.
Current supported-operation documentation must continue to state that VCI operations are not
publicly available until a later delivery phase registers them.
