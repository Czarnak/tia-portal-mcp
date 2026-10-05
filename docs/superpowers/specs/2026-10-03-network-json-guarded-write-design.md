# Network JSON Contract and Guarded Write Design

**Date:** 2026-10-03

**Status:** Spec and [implementation plan](../plans/2026-10-03-network-json-guarded-write.md) approved on 2026-10-03; implementation and offline qualification completed on 2026-10-04 at corrected code/package head `b97b9ee9173905fa9c19649a73d4f8a29bd6c682`. The [validation report](../acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md) records one whole-branch review, both Important fixes and one scoped re-review, final 5,174-test/coverage/package evidence, and the deferred unmeasured traversal-cost Minor. Exact-target live TIA V21 authorization and acceptance remain pending.

**Source baseline:** `main` at `baf811789893f706b2c7d398afc13e9e6c0e2a72`.

**Delivery:** One Network PR combining JSON contract alignment and guarded write migration, on `feature/network-contract-write-safety`. Multiuser PR 1 continues separately.

**Policy revision:** The user removed server approval elicitation for Network on 2026-10-03. Connected-subnet deletion now reports an informational guard, superseding the earlier acknowledge-guard decision. Undo feasibility is a separate future investigation, not a guarantee on which this PR depends.

## 1. Purpose and agreed decisions

Bring the existing Network tools onto the response envelope and shared guarded write pipeline already implemented for lifecycle tools. Network uses no server approval elicitation; lifecycle's mode-derived confirmation remains separate. Network already has typed canonical JSON; this work evolves that seam rather than introducing another serializer or batch engine.

The agreed decisions are:

- Keep the existing four read and five write operations.
- Deliver Network JSON alignment and write-safety migration together in one PR.
- Replace the public `confirm`/`safetyToken` flow with `dryRun`. Actual Network calls run without server approval elicitation in read-write and full.
- Allow connected-subnet deletion with an informational guard describing its consequences. Block guards remain non-overridable.
- Keep configuration results sparse: report applied settings and requested settings that were skipped, without enumerating unrequested settings.
- Verify settings actually applied. A requested setting that was skipped makes the operation and whole call unsuccessful and stops later operations. Applied changes remain in place.

The implementation must follow the current [JSON contract roadmap](../../roadmap/json-contract.md), [architecture](../../ARCHITECTURE.md), and [Network reference](../../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md). This design specializes Network's phase of the [write-safety redesign](2026-09-29-write-safety-redesign-design.md), sharing the binding, access, and audit foundation of the [lifecycle and binding design](2026-10-01-lifecycle-tiers-and-project-binding-design.md) while giving Network its own prompt-free execution policy. Earlier references to a public `acknowledge` argument or `--confirm-with-user` do not apply.

## 2. Scope

| Tool | Retained operations | Change |
| --- | --- | --- |
| `network_read` | `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, `inspect_network_object` | Align the root envelope; preserve selectors, pagination, typed item results, and read continuation after item failure |
| `network_write` | `add_network_device`, `configure_network_device`, `create_subnet`, `update_subnet`, `delete_subnet` | Guarded single-call execution, sparse partial outcomes, typed effects and verification, audit v2 |

Keep the existing maximum of 50 operations, unique operation IDs, ordered execution, one project per write call, exact selectors, and stop-on-first-failure behavior. Writes use a verified binding to an already-open project. Reads do not bind or switch the project. An explicit project path is a target constraint, not permission to open or adopt a project.

Network writes continue to operate on project engineering data. Opening, binding, saving, compiling, downloading, and PLC control retain their separate tools. New Network operations, public references to earlier operation outputs, and generic-batch migration are outside this PR.

## 3. Public input and access policy

`network_read(operations)` retains its current input shape.

`network_write(operations, dryRun = false)` replaces its old public signature. `confirm`, `safetyToken`, and public `acknowledge` are absent. Legacy argument names must be rejected before worker activity: silently ignoring an old `confirm:false` could turn a legacy preview into an actual write. Registered-tool tests must prove rejection through the real MCP argument binding path; an output-schema change alone is insufficient.

| Mode/call | Behavior |
| --- | --- |
| Read-only | Network reads remain available; Network writes are denied before project preparation or worker calls |
| Read-write, `dryRun:true` | Validate, verify binding, resolve effects and guards, return preview; no elicitation or mutation |
| Read-write, actual call | Resolve the complete ordered call and execute without server elicitation, subject to access, binding, validation, and block guards |
| Full, `dryRun:true` | Same non-mutating preview; no elicitation |
| Full, actual call | Execute without server elicitation, subject to the same Network guards and verification |

Elicitation capability is not required for Network, and no Network path sends an elicitation request. Client-side permission prompts and MCP write annotations retain their normal role. Network registers only informational and block guards, so setting `ConfirmsEveryCall` to false cannot be undermined by an acknowledge guard that still triggers the shared pipeline. Block guards stop actual writes in every mode. A dry run reports block guards without executing them; preview success means planning completed, not that an actual call would be allowed.

## 4. Response contracts

Both tools use `contractVersion: "1.0"`, their first explicitly versioned Network envelope. Migrating the write inputs and preview document is a breaking change from the previous unversioned contract; migration instructions must state this clearly.

| Member | `network_read` | `network_write` |
| --- | --- | --- |
| `tool` | `network_read` | `network_write` |
| `contractVersion` | `1.0` | `1.0` |
| `success` | Whole call completed and delivered all requested results | All requested changes completed, required verification passed, and results were delivered |
| `error` | Rejection-only typed error or explicit null | Rejection-only typed error or explicit null |
| `warnings` | Always an array | Always an array |
| `batch` | Existing `StructuredOperationBatch`, or null on rejection | Typed attempted-operation outcomes, or null before apply |
| `phase` | Absent | `preview`, `applied`, `blocked`, or `error` |
| `guards` | Absent | Always an array |
| `effects` | Absent | Ordered typed consequences paired with operation IDs |
| `verification` | Absent | Typed verification after apply, otherwise null |

Retain `batch` for these multi-operation tools rather than wrapping it in a standalone tool's `result`. Each batch item retains `operationId`, `operation`, `status`, typed `result`, `failure`, `omission`, `skipReason`, and `warnings`.

Rejection before apply has top-level `error`, `success:false`, and MCP `isError:true`. Once the call enters apply, mutation, worker, projection, or verification failure belongs to the typed batch/verification outcome: `phase:applied`, `success:false`, `error:null`, `isError:false`. A failed item may retain a contract-valid typed result describing partial mutation. Malformed worker payloads are never retained or echoed.

Text and `structuredContent` must come from the same canonical document through `StructuredToolResult`. Preserve strict required-member decoding and explicit nulls for declared nullable members. Sparse dictionary entries are not missing declared DTO members. Any genuinely conditional DTO property must use the existing annotation convention and be registered with its appearance condition in `ConditionalMemberRegisterTests`.

## 5. Shared execution seam

Implement a Network domain on `IWriteDomain` and `WriteExecution`, with `ConfirmsEveryCall = false` and no Network acknowledge guards. Use the normal verified-project binding gate; Network does not need lifecycle's strategy for opening an unbound project. Preserve lifecycle's `ConfirmsEveryCall = true` behavior and its existing confirmation tests.

The existing pipeline owns validation, lease acquisition, planning, ordered dispatch, stop-on-failure, final verification, canonical response construction, and audit v2. Its confirmation machinery remains available for lifecycle and other domains; Network does not use it. The Network domain owns exact target resolution, operation-specific effects, guards, mutation invocation, typed payload projection, and verification rules.

Use existing ordinary Network reads for planning and post-read evidence. Refactor the existing Network token snapshot helper into the domain where appropriate; do not add a new snapshot worker operation, `SafetyRead` entry, or token-bound surface. Necessary identity/completeness evidence is added to existing typed read payloads and their schema/fixtures, or captured by the existing mutation handler's operation-specific post-read. Siemens API access remains in the net48 worker.

Planning, mutation, verification, response formation, and audit execute under one pinned binding lease. Resolve targets from fresh observations and re-plan dependent items immediately before dispatch. A binding identity/revision change rejects dispatch with `binding_conflict`. The lease serializes host activity but does not prevent TIA UI edits, so exact worker selectors, fresh consequence checks, and postconditions remain necessary without an approval wait.

## 6. Effects, guards, and ordered interactions

Effects describe concrete targets and requested consequences, not a token or whole-project state hash. Include only relevant settings and relationship changes, preserving caller order and operation IDs.

| Operation | Planned effect |
| --- | --- |
| `add_network_device` | Requested device/item names and exact catalogue type identifier |
| `configure_network_device` | Exact device/node selector; requested settings and relationship targets; current values for those fields where reliably readable |
| `create_subnet` | Requested name, network type, and supplied PROFIBUS attributes |
| `update_subnet` | Exact subnet identity; current and requested values for supplied changes |
| `delete_subnet` | Exact subnet identity/name; affected node connection evidence; existing root device count |

An unknown current setting value must be identified as unreadable; it must not be synthesized as a known null or silently omitted from consequence analysis. Unrequested settings are outside the effect.

Register `network_delete_connected_subnet` with severity `info`. Fire it when the exact targeted subnet connects nodes. Its message names the subnet and describes the connections that deletion will remove. Device and node identity must be used where available; connected node names are display evidence, not unique selectors. Deterministic ordering keeps the evidence stable and auditable. This guard neither asks for acknowledgement nor blocks deletion.

Register `network_state_unverifiable` with severity `block` for a resolved target whose required consequence evidence cannot be read reliably. An unreadable connected-node inventory must not become an empty inventory. Target absence, ambiguity, invalid input, and malformed worker payloads retain their existing closed failure categories.

Overlapping operations on existing targets must be analyzed in order, including root-device-count changes caused by earlier device creation. A later affected item is re-planned immediately before its mutation through the shared dependency hook. Preserve planned consequences and dependency information in the first plan to describe the ordered call.

Late informational guards report fresh consequences and allow execution; late block guards stop both modes. Earlier changes remain applied and later items are skipped after a failure or block. For example, connecting a node and then deleting its now-connected subnet can proceed in one ordered call if exact resolution and verification permit it; deletion reports the newly observed connections as an informational guard. Keep the shared late-acknowledgement rule unchanged for other domains; Network has no acknowledge guards.

No new public output-reference syntax is added. Callers obtain generated node/subnet identities through existing typed results and reads before submitting another call. Existing repeated-target requests remain ordered; invalid dependencies fail with typed evidence rather than dispatching against a guessed target.

## 7. Sparse configuration outcomes

Keep the existing typed `ConfigureNetworkDeviceResultInfo` vocabulary:

- `appliedSettings` contains only settings actually applied, using the existing key names.
- `skippedSettings` contains only requested settings that could not be applied, with a concise reason.
- `messages` contains relevant additional diagnostics.
- Unrequested setting keys are absent. Empty dictionaries remain valid; there is no catalogue of null-valued settings in the public result.

Host-only normalization is allowed after contract validation. It must not make a missing required worker member appear valid, infer successful mutation, or treat absence as a request to clear a setting.

For example, this is the setting fragment of a partially completed configuration result, not the entire tool response:

```json
{
  "appliedSettings": { "Address": "192.168.0.10" },
  "skippedSettings": { "IoSystem": "No IO connector available" }
}
```

This item has `status:failed` and a `worker_operation_failed` failure explaining the skipped request. Its typed result remains present. Whole-call `success` is false, top-level `error` is null, and later operations have `skipReason:earlierOperationFailed`. Successful settings are not rolled back.

Preserve typed evidence for both partial application and the all-requested-settings-skipped case. The latter currently throws away the result while forming a worker failure; the migration must return a contract-valid attempted outcome so the host can classify it and retain its skipped map. Unexpected exceptions, crashes, timeouts, or malformed payloads may still lack trustworthy setting evidence and must report uncertainty.

Verify only the keys in `appliedSettings`. A skipped setting remains an execution failure, not a fabricated verification mismatch. Verification may pass for the applied subset while the item and whole call remain unsuccessful.

## 8. Verification

Verification records are typed, ordered, keyed by operation ID, and tied to exact target identity. Emit checks only for outcomes relevant to the attempted operation. Each check distinguishes passed, failed, and unverified evidence; missing reads do not pass a check. Include observed values and concise failure diagnostics, with existing closed categories such as `postcondition_failed` and `protocol_error`.

| Operation | Required postconditions |
| --- | --- |
| `add_network_device` | The returned device/item identities resolve uniquely and agree with the created name/type evidence |
| `configure_network_device` | Readable values for applied settings match; applied subnet and IO-system attachments match exact requested identities, including subnet-scoped IO-system number |
| `create_subnet` | Exactly one subnet has the returned nonblank ID, requested name/type and supplied attributes; root device count unchanged |
| `update_subnet` | Exact subnet identity survives and supplied attributes match; root device count unchanged |
| `delete_subnet` | Exact subnet is absent with no unreadable identity treated as absence; affected node connections no longer reference it; affected devices/nodes remain; root device count unchanged |

The existing subnet worker already performs exact identity/attribute postconditions and a root-device-count check. Extend the evidence needed for affected connections rather than using a count-only test probe as production verification. `networkDeviceCountUnchanged` refers to root `project.Devices.Count`; it does not prove grouped/ungrouped device preservation. Relationship verification needs exact identities and reliable discovery of the affected scopes.

Capture operation-specific postconditions before a later operation can supersede them. Preserve that evidence in the domain's per-call state for the final `VerifyAsync` result; a known postcondition failure must classify the item as failed and stop later dispatch. Final batch checks compare the effective state after the attempted prefix, accounting for deliberate later changes to the same target. Do not report an earlier successful setting as failed merely because a later successful item intentionally changed it.

A failed mutation's known applied subset is still inspected. Skipped batch items receive no verification claim. Worker timeout/crash, invalid payload, or unreadable state retains an unverified outcome and inspection guidance. Never automatically replay an uncertain mutation. Verification is bounded engineering evidence; it does not establish saved persistence, compilation, or plant acceptance.

## 9. Audit and payload budgets

Each entered `network_write` invocation produces one audit v2 record, including previews, validation failures, block paths, partial writes, and verification failures. Use the shared audit sink. Network has no `user` confirmation: actual read-write execution records `none`, and full execution keeps the shared runner's `policy` record without elicitation. Dry runs and pre-execution denials record `none`. Informational and block guard satisfaction is null. Record ordered items, checked preconditions, binding identity/revision, outcomes, guards, durations, and the exact returned canonical document hash. MCP argument-binding rejection before tool entry follows the SDK's normal rejection path rather than inventing a second audit stream.

Use the current 60,000-character item value and 180,000-character complete-document limits. Measure the complete new envelope, including warnings, guards, effects, and verification. Budgeting only the old batch document is insufficient.

Reuse whole-value omission metadata. Effects and verification details that exceed the budget are omitted as whole values with their operation identity and an omission record; preserve compact outcome summaries, guards, and inspection guidance. Never cut JSON or turn a failed partial outcome into success. Guard evaluation and mutation use the complete internal plan, independently of presentation budgeting. Output omission must not cause write replay; recovery uses filtered or paged `network_read` calls.

## 10. Cleanup and maintained documentation

Retire Network-only `CanonicalWriteSafety`, token preview DTOs, token hash/expiry instructions, and Network callers after a caller inventory proves they are unused. Preserve `WriteSafetyService`, legacy `WriteSafetyTooling`, and existing batch `SafetyRead` infrastructure while generic batches still use them. Preserve shared canonical JSON, structured batch, payload-budget, and typed decode helpers.

Update Network examples and live harnesses to `dryRun` and execution without server elicitation. Add migration guidance, partial-result examples, and inspection-before-retry advice. During implementation update `AGENTS.md`, `README.md`, `docs/ARCHITECTURE.md`, the Network reference, JSON/Network roadmaps, and relevant client/local-testing guides to describe the implemented state. Keep historical reports intact; link the new spec and later plan/evidence from both documentation indexes.

## 11. Verification and delivery gates

Implementation uses TDD for behavior changes and focused checks after each step/fix. Record a local commit for each Network implementation/fix step once Network execution is separately authorized. Builds use `-m:1`; test invocations disable collection and assembly parallelism with one xUnit thread and one VSTest CPU. Run the final full offline suite, stub build, installed-reference build, and package checks against one frozen candidate; measure meaningful coverage of changed production logic against the repository threshold.

The user additionally requires Network and Multiuser test runs to be serialized across their worktrees. A coordinator-owned slot and an exclusive execution lock prevent overlap; the slot remains held until the test process fully exits, including yielded runs.

Required automated cases include:

- Registered input/output schemas, legacy-argument rejection, text/structured equality, explicit-null/conditional-member rules, strict typed decode, and no rejected-payload echo.
- Zero Network elicitation in read-write/full and dry runs, including connected deletion and clients without elicitation capability; reject Network acknowledge guard registration; preserve lifecycle confirmation behavior; reject changed binding identity/revision before dispatch.
- Connected and unconnected subnet deletion, reliable versus degraded connection evidence, non-overridable blocks, and grouped/ungrouped device evidence.
- Fully applied, partially applied, and all-skipped configuration, with retained sparse typed results; applied-subset verification and stop/skip/no-rollback outcomes.
- Repeated targets, ordered dependencies, late guards, intentionally superseded settings, and unknown mutation/post-read outcomes.
- Exactly one audit v2 record for each tool-entry path, correct `none`/`policy` confirmation and null informational/block satisfaction, response hashing, and complete-envelope budget/omission behavior.
- Regression coverage for lifecycle confirmation/binding and legacy batch token flows; Network-only cleanup must leave those callers working.

Offline/stub/FakeWorker evidence and installed-reference compilation do not establish live V21 behavior. Prepare the public-tool live harness during implementation, then request fresh authorization for the frozen head, exact disposable project, access modes, operation arrays, and restoration procedure. The live gate covers multi-homed node configuration and subnet/IO-system identity, connected Ethernet/PROFIBUS deletion, dry runs, execution without server elicitation, typed partial outcomes where reproducible, and fresh restoration inspection. No live TIA or PLC action is authorized by approval of this design.

The implementation is complete only after required automated gates, independent branch review, maintained documentation, and clearly recorded live acceptance status. This written spec must be reviewed before writing the implementation plan; plan approval and execution-method selection precede Network implementation.

## 12. Separate undo investigation

Agent-accessible undo needs its own feasibility work, design, plan, and PR. [Siemens V21 transaction documentation](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/transaction-handling) confirms rollback of an open uncommitted transaction and grouping committed changes into a named UI undo unit. Brief research of the installed V21 XML documentation did not identify a direct Undo/Redo entry point; that is not proof that no supported route exists.

Unsaved status alone does not guarantee undo: saving, project management, and establishing an online connection can clear the history. Undoing an older entry also undoes intervening entries. See [Siemens V21 undo basics](https://docs.tia.siemens.cloud/r/en-us/v21/introduction-to-the-tia-portal/undoing-and-redoing-actions/basics-of-undoing-and-redoing-actions). A future design must identify the exact agent-owned action and handle intervening user edits. This PR adds no undo tool or automatic batch rollback.
