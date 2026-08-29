# Issue #31 Project Completeness and Hardware Pagination Design

**Date:** 2026-08-28
**Last updated:** 2026-08-29

**Status:** PR 1 implemented; PR 2 design approved for implementation planning
**Scope:** Complete grouped-device and system-block enumeration, then add opt-in,
budget-aware pagination to `read_hardware_config`

## Goal

Resolve the two related problems reported in
[issue #31](https://github.com/Czarnak/tia-portal-mcp/issues/31):

1. Project reads omit devices stored under device groups and omit PLC system-block groups.
2. A complete hardware snapshot can exceed the structured-operation result budget once those
   omissions are fixed.

The work is intentionally delivered as two separately reviewable pull requests:

- **PR 1 — traversal completeness:** enumerate grouped devices and system blocks without changing
  the public hardware-read request contract.
- **PR 2 — hardware pagination:** add opt-in, cursor-based, budget-aware pagination after the
  complete entity set is available on `main`.

The PR 2 implementation branch starts from the completed PR 1 tip, so pagination operates on the
corrected data set. PR 2 remains logically dependent on PR 1 and must be rebased or retargeted onto
updated `main` after PR 1 merges. This keeps the traversal repair independently reviewable without
making PR 2 redevelop or temporarily duplicate it.

## Non-goals

- Adding device-folder nodes to the public project tree.
- Inferring vendor, library, or author provenance from system-block location.
- Enriching blocks with `HeaderAuthor` or another authorship field.
- Changing the existing best-effort per-item error policy in PR 1.
- Adding a diagnostics envelope to `browse_project_tree`.
- Automatically switching legacy unpaged callers to partial results.
- Providing a transactionally frozen TIA Portal snapshot across pages.
- Paginating inside a device, subnet, warning, or other entity.
- Changing write-safety, project-selection, or access-mode rules.
- Adding a second public MCP tool or public operation name for paged hardware reads.
- Migrating the existing worker-local `NetworkObjectCursorCodec` used by other network operations.
- Caching TIA objects, descriptors, tag indexes, or cursor state across calls.
- Allowing cursor-bound filters or detail flags to change within one page sequence.

## Current failure modes

### Grouped devices are invisible

`HardwareConfigReader.SelectDevices` and `ProjectTreeWalker.Walk` enumerate only
`project.Devices`. They do not recurse through `project.DeviceGroups`, so a device placed under a
group is missing from both `read_hardware_config` and `browse_project_tree`.

The customer project cited in issue #31 demonstrates the practical size of the omission: the
current path reports three devices while a recursive traversal finds 95.

### System blocks are invisible

`ProjectTreeWalker.WalkBlockGroup` walks user block groups but not
`PlcBlockSystemGroup.SystemBlockGroups`. The system-group type does not inherit from
`PlcBlockGroup`, so the existing recursive method cannot simply be reused through polymorphism.

### A complete hardware read can exceed the result budget

Structured operation batches impose a 60,000-character limit per operation result and a
180,000-character limit on the full batch document. The complete customer hardware snapshot
reported in issue #31 is approximately 1.7 million characters. Returning the complete set in one
result therefore remains impossible even after traversal is corrected.

## Delivery structure

### PR 1: traversal completeness

PR 1 changes only collection completeness and the documented meaning of returned system-block
nodes. It preserves the current unpaged hardware response behavior, including existing
whole-result omission when the structured result exceeds its item budget.

### PR 2: hardware pagination

PR 2 adds an explicitly requested paged mode to `read_hardware_config`. Unpaged calls retain the
post-PR-1 behavior. Paged calls receive complete top-level entities plus continuation metadata,
with every successful page kept within the 60,000-character item budget.

## PR 1 architecture

### One shared device enumeration path

Introduce one worker-local ordered device enumerator and use it from both
`HardwareConfigReader` and `ProjectTreeWalker`. The enumerator yields:

1. devices directly under the project; and
2. devices found by recursively walking every nested device group.

The helper preserves the TIA collection order during traversal. Public consumers continue to
apply their established presentation ordering where they already do so. Centralizing discovery
prevents the two read tools from acquiring different definitions of a complete project later.

Grouped devices remain ordinary flat `Device` nodes in `browse_project_tree`. PR 1 does not expose
`DeviceFolder` nodes or a folder hierarchy, so existing consumers do not need to understand a new
node type merely to see all devices.

### Separate system-block traversal

Add a dedicated recursive walker for `PlcBlockSystemGroup` rather than forcing the type into the
user-block-group method. For each system group:

- return a folder node with `nodeType: "SystemBlockFolder"`;
- recurse through its nested `SystemBlockGroups`; and
- return each contained block with its existing functional node type, such as `FB`, `FC`, or
  `GlobalDB`.

Each returned system block adds `details.IsSystemBlock: "true"`. The marker means that the block
was enumerated through the TIA system-block hierarchy. It does **not** claim that Siemens authored
the block or establish any other provenance.

### Diagnostics boundary

PR 1 retains the current best-effort behavior:

- `read_hardware_config` continues to return available warnings through
  `HardwareConfigInfo.Messages`;
- per-item project-tree failures continue to be reported to worker stderr while
  `browse_project_tree` returns its existing bare array; and
- traversal continues past an item that the current code already treats as recoverable.

Adding caller-visible project-tree diagnostics would require a public response-envelope change
and is outside this repair.

## PR 2 public contract

### Opt-in request

`read_hardware_config` reuses the existing request fields `pageSize` and `cursor`:

- `pageSize` is valid from 1 through 200;
- supplying `cursor` without `pageSize` uses a default maximum page size of 50;
- omitting both fields selects the unpaged contract; and
- `pageSize` is a maximum, not a promise that the page will contain that many entities.

The page size is not part of the cursor query binding. A caller may change it between pages
without changing which logical snapshot the cursor addresses.

The remaining query fields are cursor-bound: `deviceName`, `plcName`, `includeIoDetails`, and
`includeTagMatches`. Changing any of them starts a new sequence without the old cursor. Paged mode
preserves explicit-project reads while the host is otherwise unbound: the first call may supply a
`projectPath`, a continuation may omit it because the cursor carries the worker-resolved path, and
an explicitly repeated path must resolve to that cursor-bound path. Issuing or consuming a cursor
never silently changes the ordinary host project binding.

### Response shape

Unpaged responses do not contain pagination metadata. Paged `HardwareConfigInfo` responses add an
optional nested object:

```json
{
  "pagination": {
    "totalDevices": 95,
    "totalSubnets": 4,
    "returnedDevices": 3,
    "returnedSubnets": 0,
    "nextCursor": "opaque-or-null"
  }
}
```

The existing `devices`, `subnets`, and `messages` properties retain their meanings. Messages are
per-call observations; a consumer reconstructing a larger result may concatenate them or
deduplicate them according to its needs.

### Pagination unit and ordering

Treat a hardware snapshot as one ordered stream of complete top-level entities:

1. devices, ordered by the existing ordinal device-name rule; then
2. subnets, ordered by ordinal `SubnetId`.

Stable source traversal order resolves any equal-key tie. A page may end in the device segment or
cross into the subnet segment. Devices and subnets are never repeated within one valid cursor
sequence. A consumer reconstructs the snapshot by concatenating each page's device list and each
page's subnet list in page order.

Messages are not cursor-addressed entities and do not affect the logical entity offset.

The worker assigns every descriptor an internal structural locator derived from collection
positions. Examples are `devices/0`, `deviceGroups/0/groups/2/devices/1`, and `subnets/3`.
Descriptor identity combines the entity kind, structural locator, and sortable public identity.
The locator distinguishes duplicate or unreadable display names without relying on undocumented
Siemens object identifiers. It is internal evidence and is never exposed in the public page,
cursor omission subject, or error text. Inserting, regrouping, or reordering an entity changes the
locator evidence and therefore invalidates the stable-set hash as intended.

## PR 2 worker/host seam

Use a cooperative worker/host implementation with a dedicated internal paged method. The public
`read_hardware_config` operation remains unchanged by name. The host routes unpaged calls through
the existing worker method and projection, while paged calls route through a
`HardwarePaginationCoordinator` and the internal worker method
`read_hardware_page_candidates`. The internal method is dispatched only by the host client and
worker switch; it is not registered in the public operation catalog.

### Worker responsibilities

The net48 worker:

1. recursively enumerates the complete ordered top-level entity descriptors;
2. computes a stable-set hash from their ordered identities and an ordering-version marker;
3. validates typed continuation evidence for the query, expected session, snapshot, and offset;
4. materializes at most the requested candidate range starting at the cursor offset; and
5. returns exactly one declared internal contracts-layer payload containing the candidates,
   counts, starting offset, ordering version, stable-set evidence, and scoped messages.

Descriptor identity includes the entity kind and a deterministic TIA container path or key so
that grouped devices with the same display name remain distinguishable. Deep hardware attributes
are excluded from the stable-set hash.

The candidate payload separates page-level messages from per-candidate messages. The host exposes
page-level messages plus messages belonging only to entities actually returned. Messages for a
trimmed candidate are emitted when that candidate is returned on a later call. Unpaged mode
continues to flatten its observations in the established order.

The candidate payload does not duplicate worker session identity. The existing
`WorkerResponse.SessionIdentity` / `WorkerCallResult.SessionIdentity` envelope remains the single
authority for the complete worker-observed identity, and continuation requests use the existing
`ExpectedSessionIdentity` guard. A missing or conflicting success identity fails closed.

### Host responsibilities

The net8 host:

1. validates request-owned evidence and authenticates a continuation cursor before worker access;
2. invokes `read_hardware_page_candidates` with typed continuation evidence and, when necessary,
   the cursor-carried expected session identity and resolved project path;
3. decodes the internal payload as exactly the declared candidate type and validates its counts,
   offsets, kinds, ordering evidence, and response-envelope identity;
4. builds the public `HardwareConfigInfo` document through the canonical JSON seam;
5. measures the complete canonical result against the 60,000-character item limit;
6. removes only trailing complete entities and their scoped messages until the result fits; and
7. emits a next cursor whose offset advances by the number of entities actually returned.

Candidates trimmed by the host are not lost. Because the cursor advances only by actual progress,
the worker materializes those candidates again on the next call.

The text content and `structuredContent` must continue to derive from the same
`CanonicalJson.Serialize` result. The host must not estimate size from worker JSON or maintain a
second serialization path.

After the coordinator produces a normal public `HardwareConfigInfo`, the existing
`NetworkPayloadContract`, structured batch execution, canonical final rendering, and independent
180,000-character batch-document budget remain authoritative. This keeps the new seam isolated
from unpaged callers and from the lightweight hardware snapshots used by network write safety.

### Why the seam is split

A host-only design would require the worker to materialize and transfer the complete snapshot on
every page. A worker-only design cannot authoritatively enforce a limit measured on the host's
canonical structured-operation envelope. Splitting enumeration from final budget sizing avoids
both failure modes.

## Cursor and consistency model

### Cursor binding

The cursor is a versioned, self-contained, unpadded-base64url document authenticated with HMAC-SHA256
under a process-local random key. The host validates its exact member set and signature before
ordinary worker access. The cursor is not encrypted: its format is unsupported and opaque as an
API contract, but a caller could decode its identity fields. Host restart rotates the key and
intentionally invalidates outstanding cursors, which already cannot outlive the host binding and
worker-session evidence they contain.

The cursor binds:

- a cursor schema version and hardware-ordering version;
- the worker-resolved project path and complete worker-observed session identity from the first
  page;
- the host project-binding ID and revision observed for that page, including an unbound snapshot;
- the query shape: `deviceName`, `plcName`, `includeIoDetails`, and `includeTagMatches`;
- the ordered top-level entity stable-set hash; and
- the next entity offset.

The first page must return a complete worker-observed identity before a continuation cursor can be
issued. Issuing a cursor does not silently change the host's project binding. A continuation can
therefore remain pinned to the first worker-observed identity even when the host was unbound: the
host supplies the cursor identity as `ExpectedSessionIdentity` and uses the cursor's resolved
project path when the caller omits it. Continuations verify both the current host binding snapshot
and the new worker response identity. A host binding revision, resolved-path, or worker-session
change invalidates the sequence. Page size is deliberately absent from the cursor query hash.

### Stable-set, non-transactional guarantee

The cursor guarantees a stable ordered entity set, not a frozen deep snapshot.

Adding, removing, renaming, regrouping, or reordering a device or subnet changes the descriptor
hash and invalidates continuation. A deep attribute change that leaves the ordered descriptor set
unchanged may appear on a later page. This trade-off avoids rereading and hashing the full deep
hardware graph solely to validate every continuation.

## Validation and failure behavior

Validate request fields and cursor structure before ordinary worker access where the necessary
evidence is host-owned. Use the closed failure vocabulary as follows:

| Boundary | Condition | Category |
| --- | --- | --- |
| Host request | Invalid page size or detail-field dependency | `validation_error` |
| Host cursor codec | Malformed encoding, unsupported version, or bad signature | `invalid_cursor` |
| Host continuation | Cursor-bound query fields differ | `cursor_filter_mismatch` |
| Host/worker continuation | Binding revision, resolved path, or worker session differs | `cursor_binding_mismatch` |
| Worker enumeration | Stable descriptor set or ordering version changed | `cursor_snapshot_mismatch` |
| Worker enumeration | Offset is outside the current entity set | `cursor_out_of_range` |
| Host projection | Identity is missing or the typed DTO is malformed or incoherent | `protocol_error` |

`cursor_binding_mismatch` is the only new failure category. Its safe error text may distinguish a
host-revision, path, or worker-identity mismatch without creating more public categories.

No failure returns a partial page or advances the cursor.

The host may trim only trailing complete entities. It must never cut serialized JSON, an entity,
or a message in half. Removing a candidate also removes only its scoped messages. A malformed
typed candidate payload produces `protocol_error`; rejected worker-shaped data is not echoed to
the caller.

If the canonical result containing one entity cannot fit within the item budget, return the
operation as `omitted` with reason `hardwarePageEntityExceededItemCharLimit`. Add an optional,
machine-readable omission `subject` containing only the entity `kind`, display `name`, and public
`identifier` when one exists; a subnet uses `SubnetId`, while a device has no additional
identifier. The subject never exposes the structural locator, worker identity, or cursor fields.
Other omission paths omit `subject`, preserving their current shape.

If page-level diagnostics alone cannot fit, return `omitted` with reason
`hardwarePageDiagnosticsExceededItemCharLimit` and no subject. Neither oversized case returns an
empty successful page, skips an entity, or advances the logical offset. The retry guidance is:
"Retry the unchanged request at the same cursor, or start a new sequence with narrower filters or
fewer detail options." On the first page, retrying the unchanged request means repeating the same
cursor-less request. Narrowing a filter or changing a detail flag never reuses the old cursor,
because doing so would violate its query binding and create inconsistent reconstructed pages.

Paged mode guarantees that a successful operation result is within the 60,000-character item
limit. The existing 180,000-character batch-document limit still applies. A caller that batches
too many otherwise valid pages may therefore receive whole-operation omission and should retry
with a smaller batch.

## TDD and verification

### PR 1 RED-GREEN cycles

Start with failing tests that prove the current omissions:

- net48 source-contract tests for recursive `DeviceGroups` traversal and the separate
  `SystemBlockGroups` walker, following the repository's existing worker-test pattern;
- FakeWorker-backed public-shape tests proving grouped devices remain flat `Device` nodes;
- tree-shape tests for `SystemBlockFolder`, functional block node types, and
  `details.IsSystemBlock: "true"`; and
- regression tests for direct project devices, user block groups, filtering, ordering, and
  recoverable item failures.

After the narrow implementation, run the serial stub build, focused and full test suites, and
repository diff checks.

A separately authorized read-only live TIA acceptance run should use a representative V21
project containing nested device groups and system blocks. It must record counts and selected
identities proving those entities are visible. Static source-contract tests and FakeWorker tests
are implementation evidence, not proof that the real Openness collections behave as expected.

### PR 2 RED-GREEN cycles

Add failing tests before each production slice for:

- public request/response contracts: the 1–200 page-size range, cursor-only defaulting, optional
  pagination metadata, strict unknown-field rejection, detail dependencies, and byte-compatible
  unpaged JSON;
- the host cursor codec: deterministic round trips under an injected test key, exact member
  validation, tamper and process-key rejection, query mismatch, binding or session mismatch,
  snapshot mismatch, and out-of-range offsets;
- worker descriptors: recursive structural locators, deterministic device/subnet ordering,
  duplicate-name tie-breaking, segment crossing, totals, snapshot hashing, and deep-attribute
  exclusion;
- the typed worker candidate payload: scoped messages, contiguous offsets, declared kinds and
  counts, complete response-envelope identity, and `protocol_error` rejection without payload
  echo;
- host budget shrinking: exact canonical thresholds, removal of trailing candidates and their
  messages, actual-offset advancement, final-page behavior, a single oversized entity, and
  diagnostics-only overflow;
- FakeWorker multi-page reconstruction in which every top-level entity appears exactly once,
  including bound and unbound sequences, variable page sizes, and query/binding/session/snapshot
  drift;
- canonical text/structured-content identity and the independent batch-document budget; and
- regressions for unpaged hardware shape, network safety snapshots, access-mode rules, existing
  generic omissions, and every other network operation.

Run a separately authorized read-only live TIA acceptance harness against a representative large
project. It should iterate every page, reconstruct the full device and subnet sets exactly once,
and verify that each successful canonical operation result is at most 60,000 characters. Record
timing separately from correctness; acceptable duration is an operational assessment, not a
substitute for completeness evidence.

For each PR, finish with:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

Live acceptance requires a separately authorized TIA Portal project and is reported independently
from static, stub-build, and FakeWorker verification.

## Documentation changes

PR 1 updates the current operation reference and relevant architecture text to describe:

- recursive grouped-device completeness;
- flat grouped-device presentation in the project tree;
- `SystemBlockFolder` and the exact meaning of `IsSystemBlock`; and
- the existing unpaged payload limitation and available filter/detail narrowing guidance.

PR 2 updates this authoritative design, the operation reference, tool description and package
README, architecture seam, troubleshooting or retry guidance, engineering log, and documentation
index where required. The documentation describes the opt-in request, page reconstruction,
process-lifetime cursor invalidation, unbound cursor-pinned sequences, strict query binding,
budget behavior, and both oversized-entity and diagnostics-only omissions.

## Acceptance criteria

### PR 1

- Both hardware and tree reads discover devices recursively through all device-group levels.
- Both reads use the same device-discovery helper.
- Grouped devices remain flat `Device` nodes.
- Every nested system-block group and contained block is returned.
- System folders and blocks use the approved node and details semantics without provenance claims.
- Existing filters, ordering, best-effort diagnostics, and unpaged result-budget behavior remain
  intact.
- Automated and stub-build evidence passes, with live Openness evidence reported separately when
  authorized.

### PR 2

- Paging is opt-in and unpaged callers retain the post-PR-1 contract.
- Every valid page contains complete entities and fits the per-result character budget.
- Concatenating a valid page sequence reconstructs every top-level device and subnet exactly once.
- Cursor validation detects query, binding/session, stable-set, and offset drift without returning
  partial data.
- Signed cursors support both bound and explicit-project unbound sequences without silently
  changing the host binding, and a host restart invalidates them.
- A single oversized entity and diagnostics-only overflow are reported explicitly without
  skipping an entity or advancing the cursor.
- Worker payloads are typed, worker identity has one response-envelope authority, and host text
  and structured content remain one canonical document.
- Unpaged reads, network safety snapshots, and unrelated cursor implementations remain unchanged.
- The batch-document limit remains enforced independently and is documented for callers.
