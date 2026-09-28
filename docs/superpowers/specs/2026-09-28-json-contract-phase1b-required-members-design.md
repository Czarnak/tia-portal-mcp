# JSON Contract Phase 1b — Required-Member Enforcement Design

**Date:** 2026-09-28

**Status:** Approved design; implementation plan next

**Source:** [JSON contract roadmap](../../roadmap/json-contract.md), Phase 1b, and the design
discussion on 2026-09-28 at `main` `269f9b9`. That discussion started as Phase 2; the user chose to
deliver Phase 1b first, keeping the roadmap order.

**Delivery:** One pull request from branch `refactor/json-contract-normalization-phase1b`. A live
run against a locally installed build of the branch is required before the pull request is opened.

## Goal

Replace the three hand-written required-member validators (Network, hardware page, project tree)
with one generic mechanism in the host's strict reader. Switch the network payloads to explicit
nulls in the same change, so the reader and the wire agree.

Success means:

- a missing member of a worker payload that writes explicit nulls is rejected by one generic rule,
  not by per-type member lists;
- an explicit `null` in a member declared non-nullable is rejected;
- every rejection the hand-written layer makes today is still a rejection;
- no tool output changes, and no safety-token hash changes;
- the `RequiredMemberEnforcement` null-omission reason no longer exists; and
- a live run on a real TIA Portal project shows zero `protocol_error` results.

## Current State

- Required members are enforced in three ways:
  - `NetworkPayloadContract`: about 380 lines of JSON-shape checks run before the typed decode.
    They are `RequireJsonMembers`, both overloads each of `ValidateRequiredPathIndexMembers` and
    `ValidateRequiredHardwarePathMembers`, `ValidateRequiredListMembers`,
    `ValidateDeviceItemJson`, `ValidateIoDetailsJson`, `ValidateRequiredAttributeValueMembers`,
    the `JsonDocument` pre-pass in `DecodeSubnetLifecycleResult`, and the member probing in
    `ValidateEnumValue`.
  - `HardwarePagePayloadContract`: `ValidateRequiredJsonShape` and its helpers.
  - `ProjectTreeWorkerPayloadContract`: `ValidateRequiredJsonShape`, `ValidateNodeJson`, and
    their helpers.
- `CanonicalJson.Deserialize` rejects unknown members, but it does not check for missing ones.
- Nine worker payload roots carry `[LegacyNullOmission(RequiredMemberEnforcement)]`:
  - seven are decoded by the host: `HardwareConfigInfo`, `HardwarePageCandidateResultInfo`,
    `NetworkObjectInspectionInfo`, `CatalogEntryInfo`, `AddDeviceResultInfo`,
    `ConfigureNetworkDeviceResultInfo`, and `SubnetLifecycleResultInfo`;
  - two are worker-only diagnostic probe results: `NetworkAttributeProbeInfo` and
    `SubnetLifecycleMutationProbeResult`.
- The members that are present only when they apply already carry
  `[JsonIgnore(Condition = WhenWritingNull)]`: `DeviceItemInfo.IoDetails`,
  `HardwareConfigInfo.Pagination`, `HardwarePaginationInfo.NextCursor`, and
  `WorkerResponse.BlockImportOutcome`.
- `TiaMcpServer.Contracts` and the worker both compile with `<Nullable>enable</Nullable>`.
  Contracts references System.Text.Json 10.

## Decisions

### Enforce in the reader, not in the types

The roadmap proposed moving the older mutable Contracts classes to records and relying on
`RespectRequiredConstructorParameters`. This design instead adds a type-info modifier to a
dedicated worker-payload reader.

Reasons:

- **Whether a member is required belongs to a decode path, not to a CLR type.** It depends on the
  null policy of the payload root being decoded, and one type can travel under both policies. For
  example, `CompileCheckReport` is the `compile_check` payload root, which Phase 2 moves to
  explicit nulls. It is also nested in `BlockImportOutcomeInfo`, which travels inside the
  null-omitting worker envelope for `update_block_logic`. A type-level requirement on
  `CompileCheckReport` would make that envelope decode fail whenever `BlockPath` is null, which
  would tie Phase 2 to the batch redesign. A reader-level rule avoids that.
- **A small change on the worker side.** The worker diff is only marker removals. Converting about
  29 network and project-tree classes would touch about 66 worker, 47 FakeWorker, and 194 test
  construction sites, as measured on 2026-09-28. The worker sites are net48 code that only a live
  TIA run exercises.
- **Easy to get right when adding features.** A new member is required with no extra step. Making
  a member optional is a visible, deliberate act.

The cost is that the rule is a convention rather than a property of the type, so it is documented
and pinned by a register test. The reader also gives no compile-time help: if the worker never
sets a member, its default value still passes, exactly as today. The roadmap therefore records a
later layer: C# `required init` members, not positional records, adopted per type once every
decode path of that type writes explicit nulls, together with test-data builders. That layer adds
compile-time completeness on top of the reader; it does not replace it.

### Live verification uses a locally installed build

The Phase 1b pull request is not opened until the branch build, installed as the `tia-mcp` global
tool, passes the live run in [Live Verification](#live-verification). No new live script is added.

## Design

### The worker-payload reader

`TiaMcpServer/Json/CanonicalJson.cs` gains a second strict read configuration next to
`StrictRead`. It has the same rules — camelCase, case-sensitive names, unknown members rejected, no
comments, no trailing commas — plus:

- `RespectNullableAnnotations = true`: an explicit `null` in a non-nullable member or constructor
  parameter is rejected.
- A `DefaultJsonTypeInfoResolver` modifier. For every object type, it sets `IsRequired = true` on
  every deserializable member, meaning one with a setter or one bound to a constructor parameter.
  Members that carry `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` are left
  optional: they are the declared conditional members.

New entry points, the counterparts of today's methods:

- `CanonicalJson.DeserializeWorkerPayload<T>(string json)`
- `CanonicalJson.NormalizeWorkerPayload<T>(string json, Action<T>? validate = null)`

Both throw `InvalidOperationException` when `WorkerJson.OmitsNullMembers(typeof(T))` is true. A
root that still omits nulls cannot satisfy a required-member check, so that call is a programming
error, not a runtime failure mode.

Two limits of the System.Text.Json features are accepted and handled explicitly:

- `RespectNullableAnnotations` does not inspect collection elements, so null elements are checked
  by the typed validators (see [What moves](#what-moves)).
- A missing member is a required-member failure, not a nullability failure, and the modifier
  covers it.

### Rule

> A host decode of a worker payload whose root writes explicit nulls goes through the
> worker-payload reader. Every member is required unless it is declared conditional with
> `[JsonIgnore(Condition = WhenWritingNull)]`.

### Callers

Switch to the worker-payload reader:

- `NetworkPayloadContract`: every operation decode, `DecodeHardwareConfig` (used by
  `network_write` token binding), and the nested `NetworkEnumValueInfo` decode;
- `HardwarePagePayloadContract`;
- `ProjectTreeWorkerPayloadContract`; and
- `ProjectRebindStatePayloadContract`, which has decoded an explicit-null root since Phase 1a.

Stay on `CanonicalJson.Deserialize`:

- the batch safety-snapshot contracts under `TiaMcpServer/Batch/`, because the batch tools are
  excluded and their roots still omit nulls; and
- `AuthenticatedCursorProtector`, because it decodes the host's own cursor, not a worker payload.

### Worker wire

- Remove all nine `RequiredMemberEnforcement` markers.
- Remove `LegacyNullOmissionReason.RequiredMemberEnforcement`.
- `WorkerPayloadNullPolicyRegisterTests` shrinks to the `BatchRedesign` and `ToolMigration`
  entries.

The two probe results follow the marker removal with no script change.
`live-test-network-phase5-qualification.ps1` reads only `sessionIdentity` from the
`probe_network_object_attributes` response, and nothing in the repository reads the
`probe_subnet_lifecycle_mutations` payload.

### What is deleted

Only the JSON-shape layer listed in [Current State](#current-state) is deleted. The semantic
validators stay: selector-kind rules, enum vocabularies, SHA-256 hash format, offsets and counts,
tree node types, and selector equivalence.

A `RequireNotNull` check that only restates a non-nullable annotation is also removed, because the
reader now enforces it. A check on a nullable member (for example, a selector member that one kind
requires) stays.

### What moves

These rules cannot be expressed as "member present", so they move into the typed validators:

1. **`ioDetails` depends on the request.**
   - `includeIoDetails: true`: every device item, recursively, has non-null `IoDetails`.
   - `includeIoDetails: false`: every device item has null `IoDetails`.
   - Not specified, which is the token-binding decode: either is accepted.

   One accepted loosening: an explicit `"ioDetails": null` on an unrequested read is now accepted,
   where today any member named `ioDetails` is rejected. The worker cannot produce it, because the
   member is `WhenWritingNull`, and callers' output drops it either way.
2. **`attributes[].value` is non-null when `availability` is `available`.**
3. **Collections have no null elements** wherever a deleted check rejects one today:
   - `ioDetails.addresses[].controllerNames[]`;
   - on the hardware page: `messages[]`, `deviceCandidates[]`, `subnetCandidates[]`, and each
     candidate's `messages[]`;
   - in the project tree: `startSelector[]`, `roots[]`, and every `children[]`, unless the
     existing `ValidateNode` null handling already rejects a null there.

   The characterization tests in [Testing](#testing) establish exactly which null elements are
   rejected today.

### Invariant: no change callers can see

- Every rejection already becomes a fixed `protocol_error` message, and the rejected payload is
  never echoed back. Exception text reaches only the network stderr diagnostic, so the new
  System.Text.Json messages are visible nowhere else.
- Successful results are re-rendered from the typed value by `CanonicalJson`, which already writes
  explicit nulls. Worker-side explicit nulls therefore change no byte of tool output.
- `network_write` token hashes are computed from that canonical typed value, so they do not change
  either.

## Testing

The work is test-first, in this order:

1. **Characterization tests, against the current code.** For each deleted check, add one payload
   that is rejected today with `protocol_error`. These tests stay green through the whole change.
2. **Reader unit tests** (`CanonicalJsonWorkerPayloadTests`), one case each:
   - rejected: a missing member at the top level, in a nested object, and inside an array element;
     an explicit `null` in a non-nullable member; an unknown member;
   - accepted: `null` in a nullable member; a conditional member that is absent; a conditional
     member that is present;
   - special shapes: a `[JsonIgnore(Never)] object?` member, which is required but nullable;
     positional-record members, including a conditional constructor-bound member
     (`HardwarePaginationInfo.NextCursor`); and an array root (`CatalogEntryInfo[]`);
   - refused: a still-marked root throws `InvalidOperationException`.
3. **Moved-rule tests:**
   - `ioDetails` with `includeIoDetails` true, false, and unspecified;
   - an available attribute with a null value;
   - a null element at each collection site listed in [What moves](#what-moves).
4. **Registers:**
   - `WorkerPayloadNullPolicyRegisterTests` loses the seven network entries;
   - a new conditional-member register pins every `[JsonIgnore(WhenWritingNull)]` member in
     Contracts, so adding one is a reviewed, deliberate act.
5. **Output stability:**
   - existing network and project-tree protocol tests pass unmodified;
   - the FakeWorker renders explicit nulls for these roots through the production policy. A
     fixture that holds `null` in a non-nullable member is corrected, or the contract member
     becomes nullable if that null is legitimate.
6. **Build and suite:** the full test suite, a stub build (the CI path), and a build against the
   real V21 assemblies.

## Live Verification

This is the definition of done for the pull request.

1. Install the branch build as the `tia-mcp` global tool, following
   [packaging](../../development/packaging.md): a PowerShell pack against the real V21
   assemblies, `verify-doctor-package.ps1`, and a `tia-mcp --version` check.
2. The user reconnects the `tia-portal` MCP server.
3. Call the live tools.
   - Reads, on `SimpleProject`:
     - `network_read`: `read_hardware_config` (default, `includeIoDetails: true`, and a paged
       read), `search_equipment_catalog`, `list_network_objects`, and `inspect_network_object`,
       including an object with a null-valued attribute;
     - `browse_project_tree`: a first page and a continuation.
   - Writes, on a scratch copy made with `save_project_as`: a `network_write` preview and apply
     for `add_network_device`, `configure_network_device`, `create_subnet`, `update_subnet`, and
     `delete_subnet`.
4. Pass criteria:
   - zero `protocol_error` results;
   - every call returns its declared shape;
   - the results and the installed version string are recorded in the plan's acceptance notes.

The automated tests prove that tool output does not change. The live run proves that real Openness
data satisfies the stricter contract. The main risk is an Openness API returning `null` into a
member declared non-nullable: Openness carries no nullability annotations, so the compiler cannot
warn about it. The fix for any such finding is to make that contract member nullable.

## Documentation

- `docs/roadmap/json-contract.md`: mark Phase 1b complete and update Current State. Rewrite the 1b
  section around the reader, and add the "Later: compile-time completeness" entry described in
  [Decisions](#enforce-in-the-reader-not-in-the-types).
- `docs/ARCHITECTURE.md` §7a: describe the worker-payload reader, its selection rule, and the
  conditional-member convention.
- `AGENTS.md`:
  - add the [Rule](#rule) to "Structured JSON contract rules", together with the
    conditional-member register;
  - remove `RequiredMemberEnforcement` from the "Worker payload JSON" convention.
- `docs/IMPROVEMENT_LOG.md`: add a completed-work entry with the live-run outcome.
- `docs/README.md` ("Latest process entries") and `docs/superpowers/README.md`: list this spec and
  its plan.

No change is needed in `README.md` (the NuGet readme) or in `docs/SupportedOperations/`, because
callers see no change. `ARCHITECTURE.md` line 687 describes the batch project-tree safety snapshot,
which stays on the existing reader, so it remains accurate.

## Out of Scope

- Phase 2 (`get_project_status`, `compile_check`) and Phase 3 (the lifecycle tools), including
  their `ToolMigration` markers.
- The batch tools and their safety-snapshot decodes.
- Converting Contracts classes to records, or adopting `required` members.
- Reducing the size of `NetworkPayloadContract` beyond the deleted layer; it remains above 800
  lines.

## Risks

| Risk | Mitigation |
| --- | --- |
| Openness returns `null` into a member declared non-nullable, and the operation now fails with `protocol_error` | Live run on a real project; make the member nullable where the null is legitimate |
| The modifier mishandles a member shape (get-only property, constructor-bound member, `object?`) | A reader unit test for each shape |
| A deleted check covered something the reader does not | Characterization tests are written before the deletion |
| FakeWorker fixtures hold nulls that the real worker also sends | Parity tests and fixture corrections; the live run confirms them against real data |
