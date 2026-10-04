# Network discovery and interface-qualified node identity repair

Date: 2026-10-04
Status: written spec approved by the user on 2026-10-04; implementation plan awaiting review.
Branch: `feature/network-contract-write-safety`. Keep this repair in the existing Network PR.

## 1. Purpose and authority

Valid hardware reads must remain usable when optional metadata is unavailable or different interfaces independently report the same node ID. Network writes must select the intended node within its owning interface and preserve that identity through preparation, mutation, verification and recovery evidence.

This amends the [Network JSON contract and guarded write design](2026-10-03-network-json-guarded-write-design.md), principally its ordinary-read evidence, exact selectors and postconditions. The [original offline qualification](../acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md) remains evidence for its recorded candidate, rather than live acceptance of this repair. The approved constraints continue to apply: no Network elicitation or acknowledge guard, connected deletion informational, unknown consequences blocking, sparse applied/skipped settings, ordered stop on failure, pinned verified binding, no rollback or replay.

## 2. Observed failure and corrected interpretation

The installed version `3.0.1-local.193.gfdabdc2` was tested against the already-open disposable `SimpleProject_copy.ap21` on 2026-10-04. The source candidate was `fdabdc2a088d8a6b3297520d2ac49ac6328760af`. All five write operations refused both preview and actual calls with `worker_operation_failed`: "Network target discovery is incomplete." No mutation occurred; final hardware inventory exactly matched the baseline and project status remained unmodified.

The seven-device, three-subnet, eleven-node inventory contained 134 root diagnostics: 83 unsupported Address reads, six unsupported get_Address reads, eight unsupported node SubnetMask/PnDeviceName reads and 37 null device-item type identifiers. There were 37 unavailable device-item selectors and seven unavailable interface selectors. All eleven node selectors and their typed connection observations were readable.

The existing host `DiscoveryComplete` predicate conflates those metadata/selector limitations with structural discovery loss. The final verifier shares that predicate. Removing its root-message check alone would leave the selector veto and would also discard genuine skipped-object evidence.

The recorded S7-1500 station has these distinct valid interface-local node identities:

| Device | Owning item path | Position path | Interface | Node name | Node ID | Address |
| --- | --- | --- | --- | --- | --- | --- |
| S7-1500/ET200MP station_1 | PLC_DP / PROFINET interface_1 | 1 / 32768 | PROFINET interface_1 | X1 | E1 | 192.168.12.2 |
| S7-1500/ET200MP station_1 | PLC_DP / PROFINET interface_2 | 1 / 33024 | PROFINET interface_2 | X2 | E1 | 192.168.13.20 |

The user's correction is authoritative for the intended domain model: node IDs are scoped to their owning interface. These rows are valid hardware, not an invalid duplicate-ID inventory. Device/node-only selectors emitted today are insufficient to distinguish them. Configuration currently rejects the interface/path qualifiers that could supply the missing scope; affected-node DTOs and verification also retain only device name and node ID.

Local run artifacts are retained under `TestResults/network-live-20261004/` in the main checkout, including `report.md`, `assembled-baseline.json` and `hardware-page3-read-independence.json`. They are ignored local evidence, not portable links or committed live acceptance. Preserve the captured observations and this corrected interpretation; never infer recorded sibling indices from the presentation array order.

## 3. Decisions

| Concern | Decision |
| --- | --- |
| Actual traversal failure | Continue refusing every guarded Network write on an incomplete ordinary project traversal in this repair. Operation-specific tolerance of actual traversal loss is deferred. |
| Optional metadata | Retain diagnostics and null/unavailable values; they do not determine traversal completeness. |
| Unusable generic item/interface selector | Keep its actual selectability and diagnostics. It does not invalidate a separately qualified node selector. |
| Exact target namespace | Prove uniqueness and required identity readability within the namespace the operation selects. |
| Repeated node ID across interfaces | Accept and distinguish by owning interface path. |
| Bare device/node request | Preserve compatibility only when exactly one node is provably selected across the device. Otherwise return a clear ambiguous-selection refusal directing the caller to qualified read selectors. |
| Unknown connection consequences | Preserve the non-overridable `network_state_unverifiable` block guard. |
| Public tool shape | Keep the two tools, operation names, `operations`, actual-by-default `dryRun`, access tiers and contract version 1.0. Add qualified selector/evidence members through the existing typed contracts. |

## 4. Typed traversal evidence

Add conditional `discoveryEvidence` to the existing `HardwareConfigInfo` payload, using shared Siemens-free Contracts types. The evidence contains required `scope`, `complete` and `failures` members. Each failure has required `stage` and `message`.

- `scope:"project"` applies only to an ordinary unfiltered full hardware read.
- `scope:"device"` identifies a filtered ordinary read. It never proves a complete project snapshot.
- Paged/legacy payloads may omit this conditional member. A page and an assembled public-page result never substitute for the guarded domain's ordinary full read.
- `complete:true` means every relevant structural traversal and materialization completed without hiding an object; it does not certify optional attributes or every generic selector.
- Failure stages distinguish device enumeration/materialization, device-item enumeration/materialization, interface discovery, node enumeration/materialization, subnet enumeration/materialization and IO-system enumeration/materialization. Filter selection failure is reported separately as `deviceSelection` in device-scoped evidence.
- A thrown collection access/iteration, an interface-service discovery exception or an exception causing a device/item/interface/node/subnet/IO system to be skipped makes traversal incomplete. A known null service means that item has no such interface, not a traversal failure.
- Optional scalar reads and unavailable type identifiers preserve their diagnostics without changing this structural flag. Required selector identities are assessed separately.
- Root-device-count availability remains a separate typed requirement for the operations and final checks that use it. A missing count cannot be synthesized from the all-scope device count.

The worker records evidence at the producing traversal/catch site. Do not classify failures by matching diagnostic strings, empty message arrays or synthesized "healthy" flags. Preserve root/grouped/ungrouped enumeration authority and all required-read exceptions.

The host strictly validates the evidence. Guarded writes require unpaged project-scoped evidence with `complete:true`. Missing evidence is unknown and refuses preparation; malformed/contradictory evidence is a protocol error. Ordinary compatible reads can still return a legacy payload. Both preparation/replanning and final verification use the same structural predicate.

## 5. Interface-qualified selectors

### 5.1 Preferred form

Add `target.interfacePath` for node targets. It is a nonempty array of shared path segments with required nonblank `name` and required nonnegative `positionNumber`. Optional `typeIdentifier` is captured only when available and must be nonblank when supplied. Use a corresponding strict host request shape and the existing worker `NetworkTarget` selector representation.

The path runs from the device's root item composition to the device item providing the selected NetworkInterface service. Each level resolves exactly one sibling by ordinal name and position number. Missing/duplicate matches or unreadable required sibling evidence refuse selection. This is an explicit qualified selector, not a fallback from a failed type identifier or positional index.

Once the path resolves, obtain that item's NetworkInterface service and select exactly one node by ordinal `nodeId` within it. An optional `interfaceName` is an extra exact constraint on the service name, rather than a device-wide selector. An optional `nodeIndex` is an extra consistency constraint on the already uniquely selected node; it never authorizes first-match selection.

Example for X1:

```json
{
  "kind": "node",
  "deviceName": "S7-1500/ET200MP station_1",
  "interfacePath": [
    { "name": "PLC_DP", "positionNumber": 1 },
    { "name": "PROFINET interface_1", "positionNumber": 32768 }
  ],
  "interfaceName": "PROFINET interface_1",
  "nodeId": "E1"
}
```

X2 instead names `PROFINET interface_2` at position 33024 in the second segment. Neither interface requires a fabricated type identifier. When a caller supplies available type evidence, it must still match the live item; an unreadable supplied constraint cannot silently disappear.

### 5.2 Existing forms and validation

Preserve existing strict `itemPath + nodeIndex` inspection selectors and their full metadata validation. Allow that existing qualified form for configuration as well, resolving the same owning interface and requiring unique node identity there before mutation. Keep the existing generic deviceItem/networkInterface/communicationConnection path rules intact.

Reject simultaneous `interfacePath` and `itemPath`; do not silently choose one. Legacy `itemPath` still requires `nodeIndex`. `interfaceName` or `nodeIndex` without an owner path is rejected. Bare `deviceName + nodeId` remains supported when complete traversal and readable candidate identities prove exactly one match. Repeated IDs in different interfaces make that bare request underspecified, not the hardware invalid. No server elicitation is introduced to clarify it.

Device-name comparison retains existing ordinal-ignore-case behavior; path names, interface-name constraints and node IDs use ordinal comparison. No new failure category is necessary: retain the existing distinction between unreadable discovery and missing/ambiguous selector outcomes, with actionable messages.

Emit preferred qualified selectors from both `read_hardware_config` and `list_network_objects`, and return them from inspection. Read-owned node selectability uses usable owner-path/device/node evidence, independently of unavailable generic parent-selector metadata. The new selector must round-trip directly into inspect and configure without transformation. Read results remain valid even when an individual object cannot be selected.

### 5.3 Worker boundary

Carry the complete selector through the existing Network invoker, client and `WorkerRequest.NetworkTarget`; do not flatten it back into device/node-only arguments. The worker resolves the same owner and node immediately before setters. If legacy top-level device/node fields are also supplied, they must agree with the selector rather than overriding it.

Use one worker resolver for configuration, immediate postconditions and affected-node verification. Normalize a successful legacy request to the same qualified identity. Keep Siemens access in net48 and preserve validation/preflight before the first setter.

## 6. Qualified connection and verification identity

Extend `NetworkNodeIdentityInfo` with conditional `interfacePath` and optional `interfaceName`, using the same path-segment type. New ordinary relationship producers capture the owner from the actual node's parent/service hierarchy and prove that it resolves back to that node. Never infer the owner from node name, address, subnet membership or the first matching E1.

The canonical node identity is device name, the ordered owner-path name/position pairs and node ID. Optional type identifiers, interface-name evidence and indices are consistency constraints, not alternative identity keys. Use structural equality or canonical serialization of typed tuples, not slash-concatenated strings that can collide.

Carry that identity through subnet connection inventories, planned affected nodes, initial/replanned observations, immediate verification identity, final expectations and public recovery evidence. Scalar verification dictionaries may encode the typed owner path canonically, with strict decoding and comparison; they must not lose its scope.

Both E1 nodes must coexist in sets, maps, guard effects and final checks. Updating one must not overwrite the other's expected state. Deleting a connected subnet verifies preservation/removal of the exact qualified affected nodes and their subnet/IO tuples, while retaining the root project.Devices.Count invariant. Root count still does not prove grouped/ungrouped node preservation.

Legacy relationship identities without an owner path can be upgraded only by a fresh complete read that proves one exact node; otherwise consequences are unknown and blocking. Identity/relationship read failure, owner ambiguity and genuine duplicate matches within a qualified namespace remain conservative refusals. These cases never invalidate otherwise correct read output.

## 7. Host integration and compatibility

Update the catalog, strict worker-payload validators, conditional-member registers, schema/conformance fixtures and FakeWorker scenarios together. New conditional members describe exactly when they appear; their absence never defaults to known complete evidence.

Remove whole-project item/interface-selector vetoes from the structural predicate. Target resolution still proves all candidate device names, node IDs or subnet/IO identities needed to rule out hidden matches in the selected namespace. Missing required identities do not become benign just because structural traversal completed. Optional unrelated attributes/type identifiers do not stand in for those identities.

Use that same distinction after mutation. Verify applied scalar fields and required relationship/preservation claims; unavailable unrelated metadata must not turn a correctly evidenced write into verification failure. Retain truthful applied/skipped maps and operation failures.

Keep existing canonical JSON, typed payload readers, text/structured parity, audit v2 and response budgets. Extend identity comparisons and budget/admission tests for long/escaped owner paths. After target preparation and before any mutation, refuse a call if its required qualified recovery/verification core cannot fit; do not execute first and then omit the interface discriminator. Existing earlier input/core admission checks remain in place.

Older host/worker combinations are not a supported guarded-write deployment. Preserve compatible reads and safe unknown-evidence refusal; ship matching host, Contracts and worker binaries. Public envelopes remain 1.0 with additive qualified evidence; callers that forbid additional fields must refresh their declared schema.

## 8. Regression and live acceptance requirements

Before product edits, observe failing regressions for the real metadata and interface-scope cases. Test producing worker helpers and the host/FakeWorker path; static source checks or clean synthetic snapshots alone are insufficient.

| Case | Required result |
| --- | --- |
| Unsupported optional Address/SubnetMask/PnDeviceName and null type identifiers | Retained diagnostics; genuine full traversal remains complete; valid qualified writes and final checks proceed. |
| Two interfaces each containing E1 | Distinct read selectors; X1 and X2 inspect/configure independently. |
| Configure X1, then configure X2 | Both expectations survive; each node retains its requested value and the other interface remains unchanged after each step. |
| Bare device/E1 request against those two interfaces | Clear pre-mutation underspecified-selection refusal; no first-match dispatch. |
| Unavailable generic parent type identifier | Qualified node owner-path selection remains usable; other selector kinds retain their declared rules. |
| Wrong owner path, name/position, supplied type, interface-name constraint or node index | Refused without retargeting to another interface. |
| Hidden candidates, skipped materialization, unreadable collections, grouped/ungrouped enumeration failure | Project evidence incomplete; all guarded writes refused in this repair. |
| Missing/malformed evidence or required selected-namespace identity | Unknown/protocol refusal; no implied completeness or uniqueness. |
| Unknown subnet/IO consequences | Existing block guard in both writable modes; dry run exposes the block where target identity is resolvable. |
| Connected-subnet deletion involving either/both E1 nodes | Informational guard with qualified affected identities; exact preserved nodes/connections verified afterward. |
| Sparse requested skip after a successful setting | Retain applied state, fail the item/call, skip later items and preserve scoped verification; no rollback/replay. |
| Binding revision change or late degradation | Refuse dispatch or retain truthful failed verification/partial state, according to whether mutation started. |
| Output/audit/schema budgets | Qualified identities preserved; one audit per entered call; matching canonical text/structured document. |

Execution must commit after each implementation/fix step and retain RED/GREEN evidence. Network and Multiuser build/test runs must never overlap; use the existing shared verification lock and serial build/xUnit/VSTest settings. After final source changes, freeze the candidate, independently review it and run the relevant full offline/coverage/installed-reference/package gates. Documentation-only follow-ups do not trigger another full suite.

Live acceptance must use matching corrected installed binaries and freshly verify the authorized disposable target. Exercise all five writes, qualified X1/X2 configuration, connected deletion, sparse partial failures, ordered outcomes and exact restoration. Inspect actual state after any failed/unknown mutation before planning restoration or retry. Preserve the historical failed run and do not convert it to a pass. The live mutation gate is part of the later approved implementation plan; writing this spec does not start live tools.

## 9. Scope and next artifact

This repair covers the observed traversal/selector coupling and interface-local node identity throughout Network. It does not add a new snapshot worker operation, SafetyRead entry, token surface, automatic undo, performance redesign, PLC control or Multiuser runtime behavior.

Following written-spec approval, produce a separate task-level implementation plan on this same branch. The plan assigns Contracts/worker producer and resolver changes, host integration, fixtures/harness/documentation migration, independent review and serialized final acceptance with concrete commit boundaries.
