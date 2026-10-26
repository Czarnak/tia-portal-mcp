# network_write native rejections and shared trusted verification — design

**Status:** draft for maintainer approval, 2026-10-26. Branch `refactor/network-write-native-rejections`,
cut from `main` at `8090ded` (merge of the PLC native-rejections PR 201).

**Predecessor:** the [PLC write native rejections plan](../plans/2026-10-25-plc-write-native-rejections.md)
and its [live verification](../acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md).
This design is the Network follow-up that plan left open, with one scope difference: it also moves
PLC onto a shared verifier so later HMI and VCI follow-ups have one place to land.

## 1. Intent and why

`network_write` checks the same facts up to three times. The host planner reads the whole hardware
configuration, then reads it again before every item after the first, then reads it once more
after the last item to compare against expectations it built along the way. The worker reads back
every attribute it just set and re-resolves the subnet it just updated three times. TIA Portal
itself is the third checker, and the only authoritative one: a non-throwing Openness call already
proves the write landed, and a throwing one already says why.

The cost is concrete. For a call of N items the host performs N+1 whole-project hardware reads
(87,512 characters on the test project) plus one `inspect_network_object` per item, before any
mutation and again after. `NetworkWriteVerifier` (286 lines), the worker's `NetworkMutationVerifier`
(194 lines) and the replan half of `NetworkWriteDomain`/`NetworkWritePlanner` exist to re-prove
what Portal already proved.

The one place Portal's own rejection is lost today is `configure_network_device`: each failed
`SetAttribute` or connect is caught, its Siemens message is flattened into `skippedSettings`, the
remaining settings are still applied, and the item fails with a generic `worker_operation_failed`.
The caller never sees the typed `engineering_rejected` / `engineeringError` that `plc_write` now
reports.

What Openness cannot provide, and the server keeps doing: telling a caller which nodes a
`delete_subnet` disconnects (`network_delete_connected_subnet`, info), and refusing to proceed when
that inventory is unreadable (`network_state_unverifiable`, block).

Maintainer decisions taken in the design discussion on 2026-10-26:

1. Scope includes the verification and replan cost, not only guards and error surfacing.
2. No collision checking remains in the product, including the name read-backs PLC kept as a
   targeted fix. Live probing decides what Portal covers; see §8.
3. `configure_network_device` stops at the first Portal rejection inside the item, exactly the rule
   the batch applies between items. `skippedSettings` leaves the contract; `appliedSettings` lists
   what landed before the rejection.
4. Approach B: PLC's `trusted`/`superseded` verification is generalized into `Safety/Pipeline` and
   PLC, Network and later HMI and VCI share it. Chosen for long-term maintainability.

## 2. Goals, non-goals, anti-goals

**Goals**

- Every Portal rejection inside a `network_write` item is a typed item failure:
  `engineering_rejected` for `EngineeringTargetInvocationException`, `worker_operation_failed` for
  any other `EngineeringException`, both with the verbatim `engineeringError`.
- One whole-project hardware read per call, in planning. No replan reads. No post-write reads.
- Completed items are `trusted`; an item a later item changed again is `superseded`. The verifier
  that says so lives in `Safety/Pipeline` and is the same one `plc_write` uses.
- Guards keep exactly what Portal cannot provide: the connected-delete consequence and the
  unreadable-inventory block, evaluated only where they apply.
- `network_write` moves to write contract `2.0`; `network_read` stays `1.0`.
- Before/after timing on the same project, live cases that capture every native rejection verbatim,
  and documentation written last from that evidence.

**Non-goals**

- Migrating `hmi_write` or `vci_write` onto the shared verifier. They stay as they are; the shared
  verifier is designed so they can follow, and the improvement log records that follow-up.
- Changing `network_read`, cursors, pagination, or any read contract.
- Changing lifecycle confirmation, elicitation or audit.
- Adding any new worker method or public operation. `ProjectCapabilityCatalog.OperationFor` and
  `OperationPolicyCatalog` are unchanged.

**Anti-goals** (what would make this a failure even if it works)

- A new server-side check that re-implements a Portal rule, in any layer. If live probing finds a
  collision Portal accepts silently and compile does not report, that finding goes to the
  improvement log as evidence; it does not become code in this branch.
- A `plc_write` document that changes shape. PLC migrates onto the shared verifier with its 2.0
  JSON members and check names byte-for-byte unchanged (message text may change; member names,
  check names and the `contentHash` member may not).
- A worker payload that hides a failure inside a success response. A rejected setting is a worker
  failure response, typed by exception type, never a success payload the host has to reinterpret.
- Any path that reaches Siemens before the binding gate, or a Siemens exception in planning that
  surfaces as `engineering_rejected` (reads keep their read categories).

## 3. Constraints

- Preserve the 8/19/19 tool surface, the `network_write(operations, dryRun=false)` arguments, the
  binding lease, access-mode gates, stop-at-first-failure, no rollback, audit v2, and the rule that
  the text block and `structuredContent` come from one `CanonicalJson.Serialize` call.
- New conditional JSON members are registered in `ConditionalMemberRegisterTests`; new worker
  payload members write explicit nulls and decode through the worker-payload reader; null-policy
  changes update `WorkerPayloadNullPolicyRegisterTests`.
- Tests link host sources by file: a new `Safety/Pipeline` file is picked up by the glob; a new or
  renamed `Network/` file must be added to `TiaMcpServer.Tests.csproj`.
- Build with `-m:1`; run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` after each
  task; packaging tests only if a product `.csproj` changes.
- Commit after each task with a short one-line message; push only to this branch.
- Timing baseline is measured before any code change and repeated unchanged on the candidate.
- Documentation is the last task and is written from the live evidence, not before it.

## 4. What exists today (analysis baseline `8090ded`)

Three layers, as in PLC before its refactor:

| Layer | Where | What it does today | Verdict |
| --- | --- | --- | --- |
| Host planning read | `NetworkWritePlanner.PlanAsync` | one `read_hardware_config`; per item: discovery-complete gate, selector resolution, `inspect_network_object` for `currentSettings`, PnDeviceName-not-settable pre-check, delete-subnet affected-node inventory, root device count | keep the read, the gate, the resolution and the delete inventory; drop the per-item inspection, the pre-check and the root count |
| Host replan | `NetworkWritePlanner.ReplanAsync`, `NetworkWriteDomain.ReplanAsync`, `_initial`/`_current`/`_prepared` | every item after the first declares `DependsOn` the previous item, so each triggers a second whole-project read before its own mutation | drop entirely |
| Host final verification | `NetworkWriteVerifier` (286), `NetworkFinalCheck`, `NetworkWritePayloadBudget` evidence/final-check omission | builds device/node/subnet expectations from immediate evidence, re-reads the whole hardware configuration, inspects PROFIBUS attributes and the auto-generation flag again, compares | drop; replace with the shared trusted verifier |
| Worker immediate verification | `NetworkMutationVerifier` (194), `NetworkPostconditionChecks.Compare/Complete`, `NetworkMutationVerificationInfo` | re-resolves the created device, the configured node and the subnet; reads back every applied attribute; `affectedNodesPreserved`, `affectedConnectionsRemoved`, `networkDeviceCountUnchanged` | drop; the result types lose their `verification` member |
| Worker duplicated preconditions | `SubnetLifecycleService.Update` (three `ResolveExactSubnetOrThrow`, `ResolveCurrentTypeIdentifierOrThrow`, PROFIBUS applicability), `Delete` (`CaptureAffectedNodes`, device count) | repeat host checks or feed the dropped verification | resolve once inside the transaction; drop the rest |
| Worker error flattening | `NetworkDeviceConfigurator.ApplyNodeAttribute/ConnectSubnet/ConnectIoSystem` | catch `EngineeringException`, write `ex.Message` into `skippedSettings`, continue | rethrow typed, stop the item |
| Host result validation | `NetworkPayloadContract.ValidateAddedDevice/ValidateConfiguration/ValidateSubnetMutation`, `requireVerification` | rejects a created device whose reported name differs from the request (`protocol_error`), validates the worker verification object, fails any skip | accept what Portal reports; validate applied ⊆ requested; no verification object |

Guards: `network_delete_connected_subnet` (info) and `network_state_unverifiable` (block). There is
no collision guard to retire. `add_network_device` and the subnet lifecycle already let Siemens
exceptions escape to `WorkerExecution.Execute`, so `engineering_rejected` already flows for them.

## 5. Approaches considered

**A. Trim the Network domain in place, PLC task order.** Same removals as below, but Network keeps
its own small verifier type. Smallest diff; keeps live verification focused on Portal behavior.
Trade-off: when HMI follows, there are two near-identical trusted verifiers and the extraction
waits for a third.

**B. Extract a shared trusted verifier into `Safety/Pipeline` now, Network as the first new
consumer, PLC migrated onto it.** Same removals, plus one shared implementation of "attempted items
become trusted, superseded, or a domain read-back". Trade-off: touches PLC code in a Network
branch and adds a pipeline type; PLC's 2.0 shape must be proved unchanged by its existing tests
and the output-schema snapshot.

**C. Fold the three worker result types into one.** Dropped: `AddDeviceResultInfo`,
`ConfigureNetworkDeviceResultInfo` and `SubnetLifecycleResultInfo` describe three different
outcomes (created identity, applied settings, subnet identity), and the 2.0 bump already covers the
member changes they need.

**Picked: B**, by maintainer decision, for long-term maintainability: one verifier to read, test
and reason about across the four write tools, with HMI and VCI as named follow-ups.

## 6. Decided design

### 6.1 Error surfacing in the worker

- **Type-based classification becomes a shared helper.** `WorkerExecution` gains
  `Classify(EngineeringException ex, bool mutates)` returning the category the existing shared
  catch uses (`engineering_rejected` for `EngineeringTargetInvocationException` under a mutation,
  `worker_operation_failed` otherwise). The shared catch and every wrap site call it, so the rule
  exists once. `Describe(ex)` is unchanged.
- **`configure_network_device` stops at the first rejection.** `ApplyNodeAttribute`,
  `ConnectSubnet` and `ConnectIoSystem` no longer catch and continue. The first
  `EngineeringException` is wrapped as
  `WorkerOperationException(Classify(ex, mutates: true), message, engineeringError: Describe(ex), configureOutcome: result)`
  where `result` carries the settings applied before it. This is the first producer of the
  existing `WorkerOperationException.EngineeringError`, closing that open follow-up for Network.
  Setter order is unchanged: `Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`,
  `Subnet`, `IoSystem`.
- **Partial-outcome carrier.** `WorkerOperationException`, `WorkerResponse` and `WorkerCallResult`
  gain a conditional `ConfigureNetworkDeviceOutcome` member of type `ConfigureNetworkDeviceResultInfo`
  (the result type itself; a failed and a succeeded configure describe the same thing). It follows
  the existing `HmiWriteOutcome`/`VciWriteOutcome` pattern and is registered as a conditional
  member: "present only when a `configure_network_device` item was rejected after at least zero
  settings were applied".
- **Host projection.** `NetworkPayloadContract.Project` builds a failed configure item as
  `status: failed`, `result` decoded from the outcome (`deviceName`, `appliedSettings`, `messages`),
  `failure {category, message, engineeringError}`; the shared `WriteRun.WithEngineeringError`
  already copies the member. A failed item of any other Network operation has `result: null` as
  today.
- **Pre-flight failures inside the worker stay typed refusals.** A requested IO-system attach on an
  interface with no IO connector fails `target_not_found` before the first setter (today it is a
  skip). The "requested subnet was not connected, so IO was skipped" case cannot occur: a failed
  subnet connect stops the item.
- **Why stop inside the item.** The batch already promises "stop at the first failure, never roll
  back" between items. Applying that rule inside the item means one rejection, one verbatim
  Portal message, and no second contract (`skippedSettings`) describing a different failure model
  for one operation. `appliedSettings` on the failure keeps the recovery evidence the old contract
  promised.

### 6.2 Retired checks

| Check | Layer | Why it goes | Replacement / live case |
| --- | --- | --- | --- |
| PnDeviceName-not-settable pre-check (`PnDeviceNameNotSettable`) | host planner | re-implements Portal's rule from a planning inspection | Portal's rejection, verbatim; the `pnDeviceNameAutoGeneration:false` hint moves to the tool description; live case L6 |
| `currentSettings` from `inspect_network_object` | host planner | one extra worker round trip per item, only for preview metadata (`source`/`access`) | the preview keeps current values taken from the hardware snapshot already in hand, as plain strings |
| PROFIBUS applicability on `update_subnet` | host resolver and worker `SubnetLifecycleService.Update` | re-implements Portal's rule from the planning read; Portal rejects the attribute on an Ethernet subnet inside the transaction, which then rolls back | Portal's rejection; live case L7. The same check on `create_subnet` is request-shape validation (no project read) and stays in the catalog and the worker entry point |
| `ResolveCurrentTypeIdentifierOrThrow` and triple `ResolveExactSubnetOrThrow` | worker | host already resolves the target and its supported type from the snapshot; the worker needs the object once | one resolve inside the transaction; a vanished or duplicated id stays `postcondition_failed` (late drift) |
| `networkDeviceCountUnchanged`, `RootDeviceCount`, `rootUncertain` | worker and host | probe-era paranoia; nothing Portal can do to a subnet changes `project.Devices.Count` | none |
| `CaptureAffectedNodes` before delete, `affectedNodesPreserved`, `affectedConnectionsRemoved` | worker | fed only the dropped verification | the planning snapshot's connection evidence still drives the info guard |
| `ValidateAddedDevice` request-equality (`protocol_error` on a different reported name) | host | a collision check in disguise: a Portal-renamed device is a Portal decision, not a protocol fault | the result reports the identity Portal created; live case L1 records what Portal does with a duplicate name |
| `requireVerification` and `ValidateVerification` | host | no verification object exists | none |
| Replan: `DependsOn` chaining, `ReplanAsync` reads, `_initial`/`_current`/`_prepared` copies | host | later items that reference objects created earlier are resolved by the worker at mutation time, as PLC does; a `delete_subnet` that follows a same-call connect under-lists the just-connected node in its info guard, a documented limitation (the guard already says to inspect the effect for exact identities) | `IWriteDomain.ReplanAsync` stays on the interface; Network's implementation fails loudly because no Network plan declares a dependency |

### 6.3 Planning

One `read_hardware_config` per call. Per item, in order:

| Operation | Reads beyond the snapshot | Resolution and effect |
| --- | --- | --- |
| `add_network_device` | none | request-derived identity, as today; `requestedSettings` = deviceName, deviceItemName, typeIdentifier |
| `configure_network_device` | none | exact node resolution (`target_not_found`/`target_ambiguous`), current values of the requested keys from the snapshot node (`Address`, `SubnetMask`, `PnDeviceName`, `Subnet`, `IoSystemSubnet`, `IoSystemNumber`; `PnDeviceNameAutoGeneration` is not in the snapshot and reads as null) |
| `create_subnet` | none | request-derived name and type |
| `update_subnet` | none | exact subnet resolution; current `Name` from the snapshot; `HighestAddress`/`TransmissionSpeed` current values null (not in the snapshot) |
| `delete_subnet` | none | exact subnet resolution; affected nodes from the subnet's `ConnectionEvidence`, upgraded to qualified identities from the same snapshot, as today |

Guards:

- `network_state_unverifiable` fires only for `delete_subnet` whose connection inventory is
  incomplete (unreadable or duplicate identities). The discovery-incomplete gate stays a call-level
  planning failure (`worker_operation_failed`) as today.
- `network_delete_connected_subnet` is unchanged.

`NetworkWriteEffect` for 2.0: `Operation`, `Target`, `RequestedSettings`, `CurrentSettings`
(`IReadOnlyDictionary<string, string?>`, explicit nulls), `AffectedNodes`, `ConnectionsComplete`.
`RootDeviceCount` is removed. Preconditions reported per item: `networkDiscoveryComplete`, and for
`delete_subnet` only, `networkConsequencesReadable`.

### 6.4 Shared trusted verification (`Safety/Pipeline`)

New files `WriteVerification.cs` (records) and `TrustedWriteVerifier.cs`; the test project's
pipeline glob links them automatically.

```csharp
/// One post-write entry for one attempted item.
public sealed record WriteOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? Message);

public sealed record WriteVerification(bool Success, IReadOnlyList<WriteOperationVerification> Operations,
    StructuredOperationOmission? Omission);

public static class WriteVerificationChecks { public const string Trusted = "trusted"; public const string Superseded = "superseded"; }

/// What a domain tells the shared verifier about its objects and its read-backs.
public interface IWriteVerificationPolicy<TItem>
{
    /// Normalized object keys the item owns (asLater:false) or offers to later items (asLater:true).
    /// Segments are joined with "\n"; a container's key prefixes its members' keys.
    IReadOnlyList<string> ObjectKeys(TItem item, bool asLater);
    /// True when the item has a domain read-back (PLC content hash); such items are read back even when they failed.
    bool HasReadBack(TItem item);
    Task<WriteOperationVerification> ReadBackAsync(TItem item, StructuredOperationFailure? failure);
}

public sealed class TrustedWriteVerifier<TItem>(IReadOnlyList<TItem> items, IWriteVerificationPolicy<TItem> policy)
    where TItem : IOperationBatchItem
{
    public Task<WriteVerification> VerifyAsync(StructuredOperationBatch batch);
}
```

Rules, lifted from today's `PlcWriteVerifier` without change of meaning:

1. Attempted items are those whose batch status is `succeeded` or `failed`; skipped items get no
   entry.
2. A succeeded item that a later attempted item changed again is `superseded`. "Later changed it"
   means a later item's offered key equals or is a `"\n"`-prefix container of one of the item's
   own keys, and the later item's failure, if any, is not a precondition refusal
   (`WorkerFailureCategories.IsPreconditionRefusal`). `engineering_rejected` is deliberately not a
   precondition refusal: a rejected configure may have applied settings first, so it still
   supersedes, and the message says the later operation failed and may have changed the object.
3. Otherwise an item with a read-back is read back (succeeded or failed).
4. Otherwise a succeeded item is `trusted`: "the Openness call completed without rejection;
   nothing was read back."
5. A failed item without a read-back has no entry; the item's failure is its record.
6. `Success` is the conjunction of entry successes.

Domain policies:

- **PLC** (`PlcVerificationPolicy`): `ObjectKeys` is today's `Keys`/`BlockKeys` logic moved
  verbatim; `HasReadBack` is `update_block_logic`/`update_type_content`; `ReadBackAsync` is today's
  `VerifyContentAsync` returning `Check: "contentHash"`, `Actual: hash`. `PlcWriteDomain.VerifyAsync`
  maps each shared entry to `PlcOperationVerification`, setting `ContentHash = Actual` for
  `contentHash` entries and null otherwise. The `plc_write` document members and check names are
  unchanged; `PlcWriteVerifierTests` and the output-schema snapshot prove it. `PlcWriteVerifier.cs`
  is deleted.
- **Network** (`NetworkVerificationPolicy`): keys are `device\n{NAME}` for `add_network_device`,
  `device\n{NAME}\nnode\n{nodeId}` for `configure_network_device`, `subnet\n{subnetId}` for
  `update_subnet`/`delete_subnet`, none for `create_subnet` (its id does not exist before the
  call). Device names are upper-cased (the resolver is case-insensitive); ids are ordinal. No
  read-backs. `NetworkWriteDomain`'s `TVerification` is the shared `WriteVerification` directly.
- **HMI and VCI**: not migrated here. HMI's `superseded` plus per-expectation read-backs fit rule 3
  with a policy whose `ReadBackAsync` runs today's `VerifyOne`; VCI's `notVerified`/
  `notStatusVerified`/`mappedLeaves` fit the same hook. Recorded as the open follow-up.

Why a policy interface and not a base class: the pipeline already composes domains through
`IWriteDomain` and small records; a policy keeps the shared class sealed and testable with a fake
policy, and each domain's object-key rules stay next to that domain's models.

### 6.5 Worker result contracts (Network 2.0)

| Type | Members after | Removed |
| --- | --- | --- |
| `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]` (identity as Portal reports it after creation) | `verification` |
| `ConfigureNetworkDeviceResultInfo` | `deviceName`, `appliedSettings`, `messages[]` | `verification`, `skippedSettings` |
| `SubnetLifecycleResultInfo` | `subnetId`, `name` | `verification`, `networkDeviceCount`, `networkDeviceCountUnchanged` |
| `NetworkMutationVerificationInfo`, `NetworkVerificationCheckInfo`, `NetworkMutationIdentityInfo` | deleted | — |

Host validation per operation: decode as the declared type; `appliedSettings` ⊆ requested with
equal values; nothing else. A payload that fails to decode is `protocol_error` as today.

### 6.6 Contract version, response shape and registers

- `NetworkWriteContractVersion.Current = "2.0"` (new, next to `NetworkContractVersion` which stays
  `"1.0"` for reads), mirroring `PlcWriteContractVersion`.
- Root envelope unchanged: `tool`, `contractVersion`, `phase`, `success`, `error`, `warnings`,
  `guards`, `effects`, `batch`, `verification`, `omission`. `verification` is the shared
  `{ success, operations[{operationId, success, check, expected, actual, message}], omission }`.
  `finalChecks`, per-item `identity`/`evidence` and `immediateEvidence` are gone.
- `NetworkWritePayloadBudget` shrinks to effect omission (an `affectedNodes` list can be large) and
  the shared verification omission; it keeps the `omission: null when complete` rule.
- `verification.success:true` with root `success:false` remains by design (a rejected item has no
  verification entry; the earlier items are trusted).
- Registers: `ConditionalMemberRegisterTests` (+`ConfigureNetworkDeviceOutcome` on the response
  types), `WorkerPayloadNullPolicyRegisterTests` if any touched payload type changes policy, the
  `ToolOutputContractConformanceTests` probes for `network_write` 2.0, the regenerated
  `output-schemas.json` snapshot (PLC rows unchanged), FakeWorker scenarios (`TiaMcpServer.FakeWorker/Program.cs`
  references the deleted verification types and needs a rejected-configure scenario with an outcome).
- Tool description (`NetworkWriteTools`): drop `skippedSettings`, describe stop-at-first-rejection,
  `engineering_rejected`, `trusted`/`superseded`, and carry the PROFINET auto-generation hint.

### 6.7 Timing protocol

Measured in `full` mode against one disposable project copy, before any code change and again on
the candidate, same project, same mode, warm worker, three runs, median. Metric source is the audit
v2 record (`durationMs` root and per item), with MCP Inspector wall-clock as the secondary figure,
exactly as `inputs/2026-10-25-plc-write-timing-before.md` did.

| Case | Call | Why |
| --- | --- | --- |
| N1 | one `configure_network_device` setting `Address` | smallest write; fixed planning cost |
| N2 | ten items: `create_subnet`, `add_network_device`, six `configure_network_device` on existing nodes (addresses, masks, one connect to the new subnet, one PROFINET name with auto-generation off), `update_subnet` rename, `delete_subnet` of the new subnet | typical multi-item call; shows per-item replan and verification cost |
| N3 | one `delete_subnet` of a subnet with at least two connected nodes | the consequence path whose reads stay |
| N4 | N2 with `dryRun:true` | planning alone |
| N5 | one `add_network_device` | create path; catalog instantiation dominates |

The maintainer adapts the exact selectors to the disposable project and records its size (devices,
nodes, subnets). The first call after server start is discarded. Restoration between runs is by
explicit inverse operations, never automatic.

### 6.8 Live probing protocol (three outcomes)

Each case is run in `full` mode on the disposable copy and classified into one of three outcomes:

- **O1 Openness throws.** The item is `engineering_rejected` (or `worker_operation_failed` for a
  non-target-invocation Siemens exception) with the verbatim `engineeringError`. Capture the
  document; it becomes a FakeWorker fixture and a documentation example.
- **O2 Openness accepts, `compile_check` reports it.** Run `compile_check` on every PLC touched;
  capture the diagnostics. The server adds nothing: the caller's normal compile step surfaces it.
- **O3 Openness accepts, compile is silent.** Evidence for the improvement log, with the exact
  operation and project state. Not a code change in this branch.

For every case, a `network_read` (`inspect_network_object` on the exact target, or a filtered
`read_hardware_config`) records whether the write landed, so a silently dropped write (the PLC L7
pattern) is told apart from an accepted collision. The Portal UI language is noted; captured
messages are in that language.

| Case | Operation | What it probes |
| --- | --- | --- |
| L1 | `add_network_device` with an existing device name | duplicate device name: throw, auto-rename (result reports the new name), or duplicate |
| L2 | `create_subnet` with an existing subnet name | duplicate subnet name |
| L3 | `update_subnet` renaming to an existing subnet name | duplicate by rename; landed or silently dropped |
| L4 | `configure_network_device` `Address` equal to another node on the same subnet | duplicate IP address; expected O2 |
| L5 | `configure_network_device` `PnDeviceName` equal to another device on the same IO system, auto-generation off | duplicate PROFINET name; expected O2 |
| L6 | `configure_network_device` `PnDeviceName` with auto-generation still on | the retired host pre-check: throw or silent drop |
| L7 | `update_subnet` `HighestAddress` on an Ethernet subnet | the retired applicability check; expected O1 and transaction rollback (Name unchanged if set in the same item) |
| L8 | `configure_network_device` with a malformed `Address` | plain Portal rejection; expected O1 |
| L9 | `configure_network_device` connecting a PROFINET node to a PROFIBUS subnet | incompatible connect; expected O1 |
| L10 | `configure_network_device` with `ioSystem` on an interface without an IO connector | worker pre-flight `target_not_found`, no setter reached |
| L11 | `configure_network_device` with `Address`, `SubnetMask` and a rejected `PnDeviceName` in one item | stop-at-first: `appliedSettings` lists `Address` and `SubnetMask`, failure `engineering_rejected`, later batch items `skipped` |
| L12 | happy path: `create_subnet`, `add_network_device`, connect a node, rename the subnet, `delete_subnet` | `applied`, all `trusted` except the superseded rename, info guard on the delete |
| L13 | L12 with the connect replaced by L8's rejection | `success:false`, `error:null`, `isError:false`; items before `applied`, the rejected one typed, the rest `skipped` |
| L14 | `dryRun:true` of L13 | phase `preview`, effects with current values, no mutation, no `engineering_rejected` |

A `postcondition_failed` read-back is added only if an L case shows O3 with a silently dropped
write, by maintainer decision at that point, as PLC R14 did. The default is that nothing is added.

### 6.9 Documentation (last, from evidence)

- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`: the `network_write` section rewritten
  for 2.0 (phases unchanged, guards, stop-at-first inside configure, `appliedSettings` on failure,
  `engineering_rejected` with one captured example, `trusted`/`superseded`, the three-outcome table
  from §6.8 with what compile reports).
- `AGENTS.md`: the Network writes bullet and the "Network read/write roots declare
  `contractVersion:"1.0"`" paragraph.
- `docs/ARCHITECTURE.md`: §7a failure categories, the Network write seam paragraph that names
  `NetworkWriteVerifier`, and a paragraph on the shared `TrustedWriteVerifier` in the pipeline
  section.
- `docs/IMPROVEMENT_LOG.md`: completed entry with removed line counts and the before/after timing
  table; the open PLC follow-up loses its Network item and gains "HMI and VCI onto the shared
  verifier"; the content-importer and truncation-marker items stay.
- `docs/README.md`: rows for this design, its plan and the acceptance report.
- `docs/roadmap/network-operations.md` and `docs/roadmap/json-contract.md`: version note.

## 7. What is left to the builder

- Exact names and file placement of the shared records, the policy interface and the two domain
  policies, within the shapes in §6.4.
- How `NetworkWriteDomain.ReplanAsync` fails loudly (it is never called).
- Whether `NetworkPostconditionChecks` survives with `SelectCreatedItem`,
  `ClassifySelection` and `ClassifyDependencySelection` or those move next to their callers.
- The exact wording of `trusted`/`superseded` messages, shared across PLC and Network (PLC's
  current texts may change; its member and check names may not).
- How the budget code shares effect omission between PLC and Network, if at all.
- Test reshaping: `NetworkGuardedWriteOrderingTests` (448 lines, mostly replan ordering),
  `NetworkMutationVerificationTests` (409), `NetworkGuardedWriteBudgetTests` (660) and the
  FakeWorker scenarios need to be rewritten against 2.0; which assertions survive is the builder's
  call, with the rule that every retired behavior gets a test proving its absence (for example, one
  hardware read per call, no second read before item two).
- The order of the N2 items and the exact disposable project.

## 8. Risks and how the design handles them

| Risk | Handling |
| --- | --- |
| Portal silently drops a write (PLC L7 pattern) | every live case records whether the write landed; a read-back is added only on evidence, by maintainer decision |
| Portal auto-renames a duplicate device | the result reports the created identity; the host no longer treats a different name as `protocol_error`; L1 captures the behavior |
| PLC regression from the shared verifier | PLC's existing verifier tests and the output-schema snapshot are the acceptance gate; PLC member and check names are frozen in §2 |
| Partial configure after a rejection | stop-at-first inside the item is the documented rule; `appliedSettings` on the failure lists what landed; no rollback, as between items |
| A replan-dependent scenario regresses | the only consumer was the connected-delete info guard after a same-call connect; documented as a limitation, like PLC's "later items may depend on objects created earlier" |
| Live cases hit `portal_lost` | existing worker mapping and binding invalidation; recorded as in the PLC report |
