# Issue #30 PLC Block Header Metadata Design

**Date:** 2026-09-13

**Status:** Approved; implementation planning complete.

**Scope:** Expose the four typed TIA Portal PLC block header properties as optional
`browse_project_tree` v3 node details without changing the request, response envelope, pagination,
or worker/host architecture.

## Purpose

Resolve [issue #30](https://github.com/Czarnak/tia-portal-mcp/issues/30) by making PLC block header
metadata available in the project-tree snapshot. Today, `browse_project_tree` reports block identity,
type, number, programming language, and structural context, but it does not report the block header's
author, version, family, or name. A caller must otherwise export block content and parse whichever
representation is available, which is expensive and incomplete across block types and languages.

The issue records live TIA Portal V21 evidence from the reporter: 275 of 615 exported blocks returned
no author through the export workaround, while the desired typed metadata was available directly on
`PlcBlock`. It also records a whole-project timing comparison on a 1,810-block project that found no
measurable cost from reading `HeaderAuthor`. That evidence motivates the change, but it predates the
current v3 snapshot implementation and does not by itself verify all four approved properties. Fresh
offline verification is mandatory, and any new live TIA verification remains a separate authorization
gate.

## Approved public contract

Every project-tree node produced from a `PlcBlock` may add these entries to `details`:

| Detail key | TIA Portal source | Public representation |
| --- | --- | --- |
| `HeaderAuthor` | `block.HeaderAuthor` | Original non-blank string |
| `HeaderVersion` | `block.HeaderVersion` | `HeaderVersion?.ToString()` when non-blank |
| `HeaderFamily` | `block.HeaderFamily` | Original non-blank string |
| `HeaderName` | `block.HeaderName` | Original non-blank string |

Each entry is optional. Null, empty, or whitespace-only values are omitted; an omitted value is never
rendered as `null` or `""`. Non-blank strings are preserved exactly rather than trimmed, normalized,
or case-folded. `HeaderVersion` uses the typed property's ordinary string representation and receives
no independent parsing or reformatting.

The existing node `name` and the new `details.HeaderName` have different meanings:

- `name` is the engineering object's name and remains the value used in typed selectors;
- `details.HeaderName` is TIA block-header metadata and is never used for selection or identity.

The implementation must not substitute one for the other. In particular, a blank `HeaderName` does
not fall back to the node `name`, and a differing `HeaderName` does not rename the node.

The details are default-on. There is no `includeHeaders` argument. The public request shape, v3
envelope, flat parent-first node shape, cursor query, and `contractVersion` remain unchanged. The
existing `details` schema already permits arbitrary non-null string values, and the strict worker
decoder already preserves them while rejecting null values and the removed `Path` key.

Example block node:

```json
{
  "nodeId": "n17",
  "parentNodeId": "n9",
  "sequence": 17,
  "name": "MotorControl",
  "nodeType": "FB",
  "details": {
    "Number": "42",
    "ProgrammingLanguage": "SCL",
    "HeaderAuthor": "Example Controls Ltd.",
    "HeaderVersion": "2.3",
    "HeaderFamily": "Motion",
    "HeaderName": "Reusable motor control"
  }
}
```

JSON object members are not semantically ordered, but the producer adds the four keys in the stable
order shown above after `Number` and `ProgrammingLanguage`, and before contextual `SoftwareUnit` and
`IsSystemBlock` entries. This preserves deterministic canonical rendering. A block for which all four
normalized header values are absent retains its existing details document byte-for-byte.

## Architecture and data flow

All Siemens Openness access remains in the net48 worker. The shared block-node construction seam is
`ProjectTreeSnapshotWalker.BuildBlockNode`, which is used for user blocks, system blocks, blocks in
software units, and every supported functional block subtype. The implementation reads the four typed
`PlcBlock` properties at that seam and adds non-blank values to the existing details dictionary.

The existing flow then remains unchanged:

1. `ProjectTreeSnapshotWalker` produces typed `ProjectTreeNode` instances in the net48 worker.
2. Worker serialization emits the declared `ProjectTreeBrowseResultInfo` payload.
3. `ProjectTreeWorkerPayloadContract` rejects malformed payloads and accepts non-null string details.
4. `ProjectTreeFlattener` copies details into immutable-snapshot flat nodes.
5. `ProjectTreeBrowseCoordinator` caches the point-in-time snapshot and projects bounded pages.
6. `StructuredToolResult` exposes one byte-identical canonical document in MCP text and structured
   content.

The current v3 walker chooses the requested device before walking it, then applies the residual typed
selector and `depth` after materializing that selected device. An unselected request still walks all
devices. Header properties are therefore read once per block included in the initial worker walk, not
once per returned page. Cursor continuations remain zero-worker-call projections of the cached
snapshot and never re-read header metadata.

No host DTO, cursor codec, query hash, snapshot-store policy, page projection algorithm, dependency
injection registration, or MCP input/output schema type changes as part of this feature.

## Error handling and resource bounds

The walker uses only the typed properties on `PlcBlock`. It must not call `GetAttribute` for block
headers or attempt to extend the feature to `PlcType`. The issue's reported experiment showed that
missing dynamic attributes can throw across Openness remoting and turn a normal tree read into a
multi-minute timeout.

Property-access failures are not converted into an apparently successful omission. They flow through
the existing worker and read-only transport failure handling so callers can distinguish a failed
observation from a header that TIA actually reported as blank. This feature adds no new failure
category and does not change timeout guidance.

Header values are not truncated. They count toward the existing 4,000,000-character snapshot limit,
60,000-character response limit, and complete-node page projection. Existing bounded failures apply
if real data exceeds those limits. The implementation must not bypass those limits or split a node
across pages to accommodate metadata.

## Metadata semantics

The server reports header metadata as observed; it does not attest to its authorship or provenance.
`HeaderAuthor` may be absent, user-editable, inherited from a template, or otherwise insufficient on
its own to prove that a vendor supplied a block. Consumers may use it as one classification input for
inventory or SBOM generation, but maintained documentation must not describe it as verified vendor
identity.

Likewise, `IsSystemBlock: "true"` continues to mean membership in TIA's system-block hierarchy. It is
not derived from, and must not be changed by, any header value.

## Compatibility

This is an additive v3 output change:

- existing node names, node types, IDs, sequences, parent relationships, and detail values do not
  change;
- blocks with reported header metadata gain up to four string entries;
- blocks for which every approved value is absent retain their existing representation;
- cursors and cached snapshots keep their current semantics;
- callers that ignore unknown detail keys continue to work.

A caller that incorrectly assumes an exhaustive fixed set of detail keys will observe new members.
The maintained project-operations reference will identify `details` as extensible and document the
four keys explicitly. The v3 `contractVersion` is not bumped because its declared dictionary schema is
unchanged. NuGet/package versioning is a separate release decision.

## Testing strategy

Implementation follows a focused TDD sequence. The first producer-level test must fail because the
walker does not yet emit the approved fields, then pass after the smallest worker change.

Mandatory automated coverage includes:

- a block whose engineering `Name` differs from `HeaderName`, proving that both survive independently;
- exact preservation of populated `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName`;
- omission of null, empty, and whitespace-only header strings, plus a null header version;
- unchanged `Number`, `ProgrammingLanguage`, `SoftwareUnit`, `IsSystemBlock`, node type, and children;
- both user-block and system-block construction through the shared `BuildBlockNode` seam;
- real worker serialization followed by strict host decoding, proving that all four keys cross the
  declared worker boundary as strings;
- FakeWorker integration proving that header details survive IPC, snapshot flattening, canonical MCP
  rendering, and pagination without another worker call on continuation;
- unchanged output-schema shape and byte equality between MCP text and structured content;
- snapshot, item, and page budget behavior with added detail values; and
- a source contract that requires the four typed property reads and forbids `GetAttribute` in the
  project-tree walker.

The existing Siemens test doubles must model all four typed properties with the same CLR shapes used
by the compile-time TIA Portal V21 references. The existing negative `HeaderAuthor` source assertion
is replaced by the positive typed-access/no-`GetAttribute` contract. Generic detail-copy tests need
not be duplicated when the producer and end-to-end tests already prove the new keys are preserved.

Fresh offline completion evidence includes:

1. a serial Release reference-stub solution build;
2. the full serial Release test suite;
3. the repository coverage threshold for materially changed production logic;
4. package verification confirming no Siemens assemblies are included;
5. `git diff --check`; and
6. a clean, reviewed scoped diff.

The exact commands and commit boundaries belong in the later implementation plan, not this design.

## Documentation changes

Implementation updates maintained documentation rather than treating this historical spec as the
runtime authority:

- `README.md` adds the new metadata capability to the compact `browse_project_tree` description;
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md` documents each key, omission behavior,
  the distinction between node `name` and `HeaderName`, and the provenance caveat;
- `docs/ARCHITECTURE.md` records that shared block-node construction reads typed header properties in
  the worker and does not use dynamic attributes; and
- `docs/IMPROVEMENT_LOG.md` records completion and any remaining live-acceptance follow-up.

This specification is indexed from both `docs/README.md` and `docs/superpowers/README.md`. Historical
issue #30 measurements remain attributed to the issue and are not rewritten as current verification.

## Separately authorized live TIA Portal V21 acceptance

No live TIA Portal action is authorized by approval of this design or by later offline implementation.
If separately authorized after offline completion, live acceptance is read-only and does not save or
mutate the project. It should use a disposable or explicitly approved project and verify:

1. all four populated values against the corresponding TIA block header;
2. a deliberately different engineering name and header name;
3. omission behavior for genuinely blank values;
4. representative user, system, software-unit, and functional block subtypes;
5. complete paged results with no metadata loss on continuation; and
6. three-run initial-snapshot timings and response/snapshot sizes compared with a current-main
   baseline.

Correct values, unchanged tree identity/structure, successful bounded paging, and no new timeout are
the semantic acceptance criteria. Timings and size deltas are recorded as evidence rather than hidden
behind an invented numeric threshold; the user retains final acceptance authority if the observed
spread suggests a material regression.

## Rejected approaches and out-of-scope work

- **Export and parse block source:** rejected because format availability and header coverage vary by
  block type and language, and it requires one expensive call per block.
- **Dynamic `GetAttribute` reads:** rejected because missing attributes can throw and become extremely
  expensive across Openness remoting.
- **An `includeHeaders` request flag:** rejected because the values are small, the details dictionary
  is already extensible, and a flag would unnecessarily expand input validation, query hashing,
  cursor equivalence, worker requests, and documentation.
- **A new strongly typed public header object:** rejected because it would change the flat-node schema
  where the existing details dictionary already provides the intended extension seam.
- **Silent per-block exception suppression:** rejected because it recreates the issue's misleading
  under-reporting failure mode.
- **`PlcType` or UDT headers:** out of scope; they do not expose the same typed property surface.
- **Deeper selector pushdown, snapshot-limit changes, or pagination redesign:** out of scope; v3
  pagination already prevents repeated worker walks for continuation pages.
- **Authorship verification, vendor attestation, or SBOM policy:** out of scope; the feature reports
  metadata and leaves classification policy to consumers.
- **Transport failure taxonomy changes, writes, project saves, and live mutation testing:** out of
  scope.

## Current verification boundary

This document records the approved design only. The corresponding
[implementation plan](../plans/2026-09-13-issue-30-plc-block-header-metadata.md) defines the task-level
TDD, documentation, offline verification, and separately authorized live-acceptance gates. No
production code, tests, package version, or live TIA Portal state has been changed for issue #30.
