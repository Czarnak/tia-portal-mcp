# Network Phase 5 qualified IO-system contract

**Status:** PR 2 qualification contract, frozen from the 2026-09-26 TIA Portal V21
worker-only matrix. This is a design input for later public slices, not shipped
`network_write` behavior. See the [acceptance report](../acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md)
for evidence and its limits.

## Qualified field vocabulary

The exact target is an `ioSystem` selector with a nonblank `subnetId` and a
nonnegative `number`; extra selector fields are rejected. One request changes
one field. `Name` and `Number` are modeled fields also visible through dynamic
metadata. The other candidates are dynamic attributes. The table records the
current fixture's read and raw metadata, rather than a general Siemens API
guarantee. Private selectors and values remain in ignored evidence.

| Fixture | Field | Observed source; CLR / wire type | Availability and access | Tested alternate class and outcome | Verdict for later public planning |
| --- | --- | --- | --- | --- | --- |
| PN-B | `Name` | modeled and dynamic; `System.String` / `string` | available, read-write | Collision-free temporary string; one-field edit, post-read, hardware compile, and restoration observed | Include |
| PN-B | `Number` | modeled and dynamic; `System.Int32` / `integer` | available, read-write | Free integer chosen from current inventory; changed selector returned, then original selector restored | Include |
| DP-B | `Name` | modeled and dynamic; `System.String` / `string` | available, read-write | Collision-free temporary string; edit and restoration compiled with the same unrelated fixture warning | Include |
| DP-B | `Number` | modeled and dynamic; `System.Int32` / `integer` | available, read-write | Free integer chosen from current inventory; changed selector returned, then original selector restored | Include |
| PN-B | `UseIoSystemNameAsDeviceNameExtension` | dynamic; `System.Boolean` / `boolean` | available, read-write | Boolean toggle changed a linked PN device name as expected; reverse toggle restored it; both hardware compiles succeeded | Include, with linked-name safety evidence |
| PN-B | `MultipleUseIoSystem` | dynamic; `System.Boolean` / `boolean` | available, read-write | Boolean toggle committed, but the linked PN name became unavailable on post-read; compilation was not attempted | Exclude; safe postcondition and restoration were not proved |
| PN-B | `MaxNumberIWlanLinksPerSegment` | dynamic candidate; CLR / wire type unproved | `unknownAttribute` | No edit attempted | Exclude until a separate fixture proves metadata, safe value, and restoration |

The three named dynamic candidates were not approved for DP. DP-B reported
them as `unknownAttribute`, and the approved design did not admit a DP dynamic
slice. A writable metadata flag alone is not permission to add a field. No
general `Name` length, `Number` range, or collision rule beyond the tested
alternate classes is established by this matrix; later public validation must
be designed and tested explicitly.

## Exact owner and compile scope

For both fixtures the IO system had one verified controller interface owner:
the exact `DeviceItem` hosting its controller interface. The hardware compiler
target was a **different** `DeviceItem`, the deepest strict ancestor on that
owner path that directly hosted the unique `PlcSoftware` through
`SoftwareContainer` and exposed `ICompilable`. The containing `Device` had
exactly one PLC software instance. Owner and compiler paths, direct-host role,
service availability, and PLC software identity were re-proved around the
edit and compile. This is a hardware compile on the proven master PLC item;
the existing `compile_check` PLC-software operation is not this gate.

The future public operation must fail closed when the exact IO-system target,
owner, strict ancestor compiler target, direct PLC host, unique software role,
compile service, or pre/post continuity is absent, unreadable, or ambiguous.
It must never substitute the containing `Device` or another network participant.

## Planned public `network_write` contract

`update_io_system` is reserved for later Phase 5 slices. It is not registered
by PR 2. A later implementation must reuse the existing structured network
catalog, typed worker payload, canonical JSON result, pinned project-binding
lease, preview/token/apply gate, and audit path.

1. Preview binds the **old exact selector**, current requested-field value,
   applicable linked-name/owner/compile evidence, ordered operations, and
   current project state. Apply accepts the unchanged request and token, then
   re-reads and validates the current state before a setter. It may not fall
   back to a name or first match.
2. One transaction sets the one validated field. Commit precedes authoritative
   post-read and hardware compile. A `Number` edit changes the selector's
   `number`; return a freshly resolved canonical post-write selector so a
   restoration targets the new identity. `Name` is not part of this selector
   shape, but it is still returned from a fresh post-read with the applied
   value. Never reuse a stale precommit object reference for an identity edit.
3. A successful operation returns typed old/new field observations, the applied
   selector, bounded hardware compile state/counts/messages, and relevant
   linked PN name evidence. The PN extension field requires the linked-name
   dependency, before/after identity, and restoration checks demonstrated in
   this qualification.
4. If a post-commit read, identity proof, or hardware compile fails after a
   **known commit**, the operation fails with `postcondition_failed` and a
   bounded typed result carrying `mutationCommitted: true`, the applied state
   and post-write selector when known, and the available compile evidence.
   Unknown or missing evidence must stay explicit. Ordinary failures retain
   `result: null`. Do not report rollback, automatically retry, or hide the
   known committed mutation.
5. Transport loss before the worker response leaves the mutation outcome
   unknown. Tell the caller to inspect current state before any new preview or
   separately authorized restoration. Sequential batches stop at the first
   failure; prior committed operations remain committed and later operations
   are skipped. The public audit must record the truthful per-operation outcome.

No live failed hardware compile was deliberately induced. The known-committed
failure response still needs automated contract coverage and its own later
public-path live gate. This qualification made no project save, download,
commissioning, or PLC mode request. The temporary worker-only probe is not the
public operation and does not itself supply public audit or token semantics.
