# Write-Safety Redesign: Retiring Preview→Apply Tokens

**Date:** 2026-09-29
**Status:** Draft for discussion. Nothing in this document is decided or implemented.
**Supersedes when accepted:** the token-flow parts of
[write-safety hardening (2026-09-01)](2026-09-01-write-safety-hardening-design.md); Phase 3 and
part of Phase 4 of [the JSON contract roadmap](../../roadmap/json-contract.md).
**Unblocks:** the safety integration boundary in
[Multiuser Engineering (2026-09-28)](2026-09-28-multiuser-engineering-design.md), which waits for
"the separate write-safety redesign".

## 1. Why this document exists

The preview→apply safety-token flow was designed to make an agent stop and obtain the user's
permission before writing to a TIA Portal project. It does not do that, and no MCP server can.
An agent calls `preview_write_batch`, reads the token out of the response, and calls
`apply_write_batch` with `confirm=true` in the same turn. The round trip is a self-confirmation.
Nothing in the protocol lets a server require that a human saw the preview.

MCP places consent on the other side of the wire. The specification's user-interaction model says
the *host application* should keep a human in the loop and require explicit user confirmation
before executing operations; tool annotations (`readOnlyHint`, `destructiveHint`) are advisory
input to that decision and must be treated as untrusted by clients. Two protocol features do let a
server reach the user directly, both client-capability-gated: **elicitation** (form mode, since
protocol revision 2025-06-18; `McpServer.ElicitAsync` in the C# SDK this project uses) and
**multi-round-trip requests** (`input_required` results, revision 2026-07-28; `InputRequiredException`
in the SDK). Neither is used today.

The token flow nevertheless carries several properties that are worth keeping: a description of
consequences before a write, refusal of ambiguous or missing targets, refusal of a few
known-dangerous configurations, serialization of writes under one project binding, and an audit
trail. This document separates those properties from the ceremony around them and proposes a
design and a phased plan to get from here to there.

## 2. What exists today

### 2.1 Three write surfaces, one token service

| Surface | Tools | Flow | Binding serializer | Where |
| --- | --- | --- | --- | --- |
| Generic batch | `preview_write_batch`, `apply_write_batch` | Two tools; one token per batch | `TiaJson.Presentation` | `TiaMcpServer/Batch/WriteBatchTools.cs` |
| Project lifecycle | `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project` | Self-previewing: no token → preview; token + `confirm=true` → apply | `TiaJson.Presentation` | `TiaMcpServer/Tools/ProjectWriteTools.cs` |
| Network | `network_write` | Self-previewing, structured `phase: preview \| apply \| error` envelope | `CanonicalJson` | `TiaMcpServer/Network/NetworkWriteTools.cs` |

All three route through `WriteSafetyService` (`TiaMcpServer/Safety/WriteSafetyService.cs`,
`CanonicalWriteSafety.cs`). A token is 32 random bytes held in an in-memory dictionary and bound to:
tool name, normalized project path, the complete `ProjectBindingSnapshot`, the rendered target,
a hash of the requested input, and a hash of a freshly read "current state". Tokens expire after
ten minutes, are consumed by the *attempt* (removed before validation), and are swept on every
preview. `WriteSafetyTooling.cs` orchestrates the lifecycle apply path (cheap envelope check, then
fresh-state read, token consumption, mutation, verification, and audit under the pinned binding
lease). Successful writes append JSONL under `%LOCALAPPDATA%\TiaMcpServer\audit` in two record
shapes (presentation and canonical).

### 2.2 Machinery that exists only because tokens bind "current state"

Binding a token to a state hash requires a side-effect-free reader that produces a deterministic
snapshot for every write operation. That requirement, not the write itself, is what grew the
following code:

| Area | Files | Approx. lines |
| --- | --- | --- |
| Host token core | `Safety/WriteSafetyService.cs`, `Safety/CanonicalWriteSafety.cs`, `Safety/WriteSafetyTooling.cs` | 960 |
| Host batch snapshots | `Batch/BatchSafetySnapshot.cs`, `Batch/TagOperationSafetySelector.cs`, `Batch/TagUpdateSafetyCurrentState.cs`, `Batch/TagOperationSafetySnapshotContract.cs`, `Batch/ProjectTreeSafetyPayloadContract.cs`, `OperationBatches/OperationBatchStateComposer.cs`, the `ReadCurrentStateAsync` family in `Batch/BatchWorkerInvoker.cs` | 750 |
| Host network snapshot | `Network/NetworkSafetySnapshot.cs` (state read half; target resolution half stays) | 60 |
| Worker snapshot readers | `Openness/TagOperationSafetySnapshotReader.cs`, `TagOperationSafetySnapshotBuilder.cs`, `TagUpdateSafetySnapshotReader.cs`, `ProjectTreeSafetySnapshotReader.cs`, eleven `read_*_safety_snapshot` dispatch entries in `Program.cs` | 650 |
| Contracts | `TagOperationSafetySnapshotInfo.cs`, `TagUpdateSafetySnapshot.cs`, `ProjectTreeSafetySnapshotInfo.cs`, the `SafetyRead` capability and its eleven `OperationPolicyCatalog` entries | 200 |
| Tests | 36 files reference the token service or `safetyToken`; roughly 430 of the suite's ~2,300 facts and theories pin token, snapshot, or preview semantics | — |
| Documentation | `README.md`, `AGENTS.md`, `ARCHITECTURE.md` §7a and §8, six operation summaries, `guides/troubleshooting.md`, `development/local-mcp-testing.md`, two roadmaps | — |

`Batch/BatchPreviewDiff.cs` (bounded current-versus-requested excerpts for block and type
replacement) is display evidence outside token hashing and is worth keeping regardless.

### 2.3 Costs observed in practice

- Every write is two round trips. Preview responses echo targets, hashes, and the full binding
  snapshot into the agent's context; the apply call repeats the entire operation list.
- **Dependent items cannot be batched** ([#79](https://github.com/Czarnak/tia-portal-mcp/issues/79)):
  the preview reads state as it is now, so `create_tag_table` followed by `create_tag` in that
  table fails at preview. The single-token-per-batch design cannot express "created by an earlier
  item".
- **Preview quality is uneven** ([#78](https://github.com/Czarnak/tia-portal-mcp/issues/78)):
  `delete_block` previews as `delete_block.`; `update_tag` names the tag but not the change;
  `network_write` previews carry `diff: null`. The preview text is a by-product of the state read,
  not a purposeful description of the change.
- **Every new write operation pays a safety tax**: a worker snapshot reader, a typed snapshot
  contract, a dedup selector key, and a live acceptance report for the token binding. The
  Multiuser design explicitly stopped at this boundary.
- Two token presentations (presentation-serializer and canonical) and two audit record shapes
  coexist; the JSON contract roadmap already lists both as debt.
- The state hash is fragile by construction: the `<DocumentInfo><Created>` timestamp had to be
  stripped from exported XML before every preview→apply pair stopped failing with
  `state_changed`.
- The documentation claims more than the mechanism delivers. `AGENTS.md` says preview-then-apply
  is "non-negotiable"; `README.md` describes it as the write-safety model. A reader can
  reasonably conclude a human approves each write.

## 3. What to keep, transform, and drop

| Property | Where it lives today | Verdict |
| --- | --- | --- |
| Description of consequences before a write | Preview `summary`, `diff`, lifecycle `Describe*` helpers | **Keep, transform.** Becomes the `effects` report of a `dryRun` and of every applied write. Written on purpose per operation (closes #78). |
| Refusal of ambiguous or missing targets | `NetworkIdentityResolver`, collision probes in the tag and project-tree snapshot readers | **Keep, relocate.** Resolution happens at write time, inside the lease, immediately before the mutation. Rules currently in snapshot readers become preconditions of the worker write methods. |
| Known-dangerous configurations refused or flagged | `ArchiveDirectoryGuard`, `WouldCloseModifiedSource`, `rebind:false` rejection, partial-write warning, `delete_subnet` node-count report | **Keep, formalize** as a guard catalog (§4.3). |
| Pinned binding lease and verified binding gate | `OpennessWorkerClient.ExecuteWithPinnedBindingAsync`, `RequireVerifiedWriteBindingAsync` | **Keep unchanged.** Independent of tokens: a write pins the current snapshot instead of a token's retained one. |
| Audit trail | `AppendAudit`, `AppendCanonicalAudit` | **Keep, simplify** to one canonical record shape. |
| Read-only access mode | `OperationAccessPolicy`, tool registration | **Untouched.** |
| Bounded content diff evidence | `BatchPreviewDiff` | **Keep** for `dryRun` and applied-write reports of content replacement. |
| Stale-state protection (state hash in token) | `WriteSafetyService`, all snapshot readers | **Drop as a mandatory mechanism.** Replace with an opt-in precondition on content replacement (§4.5). |
| Token issuance, expiry, single use, `confirm` flag | `WriteSafetyService`, every write tool signature | **Drop.** |
| Two binding presentations | `WriteSafetyService` vs `CanonicalWriteSafety` | **Drop**; one canonical path. |
| Two-tool batch pair | `preview_write_batch` / `apply_write_batch` | **Collapse** to one write tool with `dryRun` (§4.9, open question 4). |

The one real loss is protection against a project change between the agent's last read and its
write, for example the user editing a block in the TIA Portal UI while the agent works. The current
token covers this with an opaque hash the agent never sees. The replacement (§4.5) covers the case
that matters, content replacement, with a precondition bound to what the agent actually read.

## 4. Target design

Principle: **one call performs one write; safety is validation and transparency, not ceremony.**

### 4.1 The write pipeline

Every write tool runs the same pipeline inside `ExecuteWithPinnedBindingAsync(workerClient.BindingSnapshot, …)`:

```
validate input → verified-binding gate → resolve exact targets (fresh read)
   → evaluate guards → [dryRun? stop and report] → mutate → verify → audit → report
```

- **Resolve** fails closed exactly as `network_write` does today: zero, several, or unreadable
  candidates fail with `target_not_found`, `target_ambiguous`, or `postcondition_failed`.
- **Guards** (§4.3) run against the resolved targets and fresh state.
- **Mutate** is sequential per item; stop on first failure; no rollback; the failed item carries
  the existing partial-write warning.
- **Verify** is the existing post-write read (`get_basic_project_status`, `network_read` guidance).
- **Audit** appends one canonical record (§4.8).
- **Report** is the structured envelope (§4.7).

A shared `WriteExecution` (name provisional) in `TiaMcpServer/Safety/` owns this sequence so a new
domain (Multiuser, HMI, libraries) supplies a resolver, a guard set, and a mutation, and inherits
the rest. This replaces `WriteSafetyTooling.ValidateAndExecuteForApplyAsync`.

### 4.2 No token, no `confirm`

Write tools lose `confirm` and `safetyToken`. The MCP annotations stay explicit
(`ReadOnly=false, Destructive=true` on every write tool). Consent remains the client's job, and the
documentation says so plainly.

### 4.3 Guards

A guard is a named, testable rule evaluated before mutation. Each has an id, a severity, and a
message that names the concrete target.

| Severity | Behaviour |
| --- | --- |
| `info` | Write proceeds; the guard appears in `warnings` and `effects`. |
| `acknowledge` | Write is refused with `phase: blocked` unless the call's `acknowledge` array names this guard's id. |
| `block` | Write is always refused. |

Proposed initial catalog (ids provisional):

| Guard id | Operation | Severity | Fires when |
| --- | --- | --- | --- |
| `closes_source_project` | `open_project` with `forceRebind` | `info` | A worker-owned source project will be closed (saved). |
| `discards_unsaved_source_changes` | `open_project` with `forceRebind` | `acknowledge` | The source project has unsaved changes and would be closed. Today a hard rejection. |
| `discards_unsaved_changes` | `close_project` with `saveBeforeClose=false` | `acknowledge` | The project is modified. |
| `archive_without_save` | `archive_project` with `saveBeforeArchive=false` | `info` | The project is modified. |
| `archive_discards_restorable_data` | `archive_project` | `info` | Mode is `DiscardRestorableData*`. |
| `archive_inside_project_folder` | `archive_project` | `block` | Existing `ArchiveDirectoryGuard` rule. |
| `target_exists` | `create_project`, `save_project_as` | `block` | The destination directory already exists. |
| `deletes_block` | `delete_block` | `acknowledge` | Always; message names path, type, language. |
| `deletes_group_with_children` | `delete_block_group` | `acknowledge` | The group is not empty; message lists children. |
| `deletes_table_with_tags` | `delete_tag_table` | `acknowledge` | The table is not empty; message counts tags and constants. |
| `changes_plc_operating_mode` | `start_plc`, `stop_plc` | `acknowledge` | Always; physical effect. Blocked entirely until [#74](https://github.com/Czarnak/tia-portal-mcp/issues/74) is fixed. |
| `deletes_subnet_with_connected_nodes` | `delete_subnet` | `acknowledge` | Connected node count > 0. |
| `partial_write_no_rollback` | any multi-item write | `info` | Always, on the failed item (existing warning). |

Acknowledgement rule: the `acknowledge` array must equal exactly the set of `acknowledge`-severity
guards that fired for this call. Unknown ids, ids for guards that did not fire, and missing ids
all fail with `validation_error`. This prevents a blanket "acknowledge everything" habit from
working, and it puts the specific consequence into the tool-call arguments, where a client with a
permission prompt shows it to the human. That is the strongest lever a server has over client-side
consent: make the dangerous intent visible in the call itself.

### 4.4 `dryRun`

Every write tool accepts `dryRun: bool = false`. A dry run executes the pipeline through guard
evaluation, mutates nothing, and returns `phase: preview` with the same `effects` and `guards`
members an applied write would carry. For content replacement it includes the bounded diff
evidence from `BatchPreviewDiff`. There is no token in the response and nothing to carry into a
later call. This keeps "the server tells you the consequences" without a mandatory second call.

Trade-off to decide (open question 3): with `dryRun` on a `Destructive=true` tool, a client that
prompts on destructive tools will prompt for the dry run too. A separate read-only preview tool
avoids that at the cost of a second tool name per surface.

### 4.5 Opt-in stale-content precondition

`update_block_logic` and `update_type_content` accept an optional `expectedContentHash`. The read
tools (`get_block_content`, `get_type_content`) return the matching `contentHash` of the exact
format they served. If the hash is supplied and the fresh content read at write time differs, the
item fails with `state_changed` and nothing is written. This is the HTTP `If-Match` pattern: a
guard bound to what the agent actually read, not to an opaque server-side snapshot. Tools that set
explicit values (`update_tag`, `configure_network_device`) do not need it: their exact-target
resolution at write time already refuses a renamed or deleted target, and the new values are the
request.

### 4.6 Asking the human

Two protocol mechanisms exist; both depend on the client.

- **Elicitation (form mode).** When `server.ClientCapabilities?.Elicitation` is present and the
  server is started with an opt-in switch (name provisional: `--confirm-with-user`), an
  `acknowledge`-severity guard can be satisfied by the user accepting an `ElicitAsync` prompt that
  carries the guard message and a single boolean, instead of by the agent's `acknowledge` list. A
  decline or cancel fails the write with `access_denied`. Without the capability or the switch,
  behaviour is exactly §4.3.
- **Multi-round-trip requests.** The 2026-07-28 revision lets a tool return `input_required` with
  an elicitation request and a `requestState`, and the client retries the call with the user's
  answer attached. This is the protocol-native version of preview→confirm and needs no server-side
  token store. It is the eventual home of the `acknowledge` flow once clients adopt the revision.

Honesty clause for the documentation: even with elicitation, the server only knows that the client
returned `accept`. Whether a human saw the prompt depends on the client. The server never claims to
guarantee consent.

Whether the maintainer's clients (Claude Code first) render form elicitation is unverified and is
the first spike in Phase 1.

### 4.7 Response envelope

Aligned with the target envelope in the JSON contract roadmap, delivered through
`StructuredToolResult` as one `CanonicalJson` document:

| Member | Content |
| --- | --- |
| `tool`, `contractVersion`, `success`, `error`, `warnings` | As in the roadmap. |
| `phase` | `preview` (dry run), `applied`, `blocked` (a guard refused), or `error`. |
| `guards` | Every guard that fired: `{ id, severity, operationId?, message, acknowledged }`. |
| `effects` | What the write did or would do, per item: resolved target identity, the change in current → requested terms, and content diff evidence where applicable. |
| `batch` or `result` | The typed per-item results (`StructuredOperationItem`) or single result. |
| `verification` | The typed post-write read, when the tool performs one. |

`isError` is `true` only for rejection before anything ran (validation, access mode, binding,
`blocked`). A write that ran and failed reports `success: false` with `isError: false`.

### 4.8 Audit record

One canonical shape replaces the two current ones: timestamp, tool, `contractVersion`, the
`ProjectBindingSnapshot`, the ordered typed operations, the resolved targets, the fired guards and
how each `acknowledge` guard was satisfied (`agent` or `user`), the `expectedContentHash`
preconditions checked, and the exact response document. Same directory and JSONL layout; a
`recordVersion` member discriminates old records from new.

### 4.9 Batches

The generic batch becomes one tool (name to decide, §9 question 4) with `operations`, `dryRun`,
and `acknowledge`. Items resolve sequentially at write time, so an item may depend on an earlier
item in the same call (closes #79). In a dry run, an item whose target does not exist yet but is
created by an earlier item reports `effects.dependsOn: <operationId>` instead of failing. Stop on
first failure, no rollback, and the failed item's partial-write warning are unchanged.

### 4.10 Tool surface after the change

| Today | After |
| --- | --- |
| `preview_write_batch` + `apply_write_batch` | one batch write tool with `dryRun` |
| six lifecycle tools with `confirm` + `safetyToken` | same six tools with `dryRun` + `acknowledge`, structured envelope |
| `network_write` with `confirm` + `safetyToken` | `network_write` with `dryRun` + `acknowledge` |

Read-write mode exposes thirteen tools instead of fourteen. This is a breaking change to every
write tool's input schema and output shape and belongs to the next major version.

## 5. What is deleted

Only after its replacement is in place and tested (see phases). Test files are listed for scope;
tests themselves are written separately.

**Host.** `Safety/WriteSafetyService.cs` token core (the audit half moves to a new
`Safety/WriteAuditLog.cs`), `Safety/CanonicalWriteSafety.cs`, `Safety/WriteSafetyTooling.cs`
(`DescribePathState`/`DescribeProjectCreationState` move into lifecycle guards),
`Batch/BatchSafetySnapshot.cs`, `Batch/TagOperationSafetySelector.cs`,
`Batch/TagUpdateSafetyCurrentState.cs`, `Batch/TagOperationSafetySnapshotContract.cs`,
`Batch/ProjectTreeSafetyPayloadContract.cs`, `OperationBatches/OperationBatchStateComposer.cs`,
the `ReadCurrentStateAsync` family in `Batch/BatchWorkerInvoker.cs`, the state-read half of
`Network/NetworkSafetySnapshot.cs`, and the test-compatibility wrappers `Batch/BatchTools.cs` and
`Tools/ProjectLifecycleTools.cs`.

**Worker.** `TagOperationSafetySnapshotReader.cs`, `TagOperationSafetySnapshotBuilder.cs`,
`TagUpdateSafetySnapshotReader.cs`, `ProjectTreeSafetySnapshotReader.cs`, and the eleven
`read_*_safety_snapshot` dispatch entries. Before each reader goes, its fail-closed rules (name
collisions across tags, constants, and blocks; logical-address collisions; folder identity;
unreadable-candidate refusal) become preconditions inside the corresponding write method, with
tests. `probe_project_status_for_lifecycle`, `probe_open_project_rebind`, and
`get_basic_project_status` stay: they feed guards and verification.

**Contracts.** `TagOperationSafetySnapshotInfo.cs`, `TagUpdateSafetySnapshot.cs`,
`ProjectTreeSafetySnapshotInfo.cs`, `OperationCapability.SafetyRead` and its catalog entries, and
any `WorkerRequest` members used only by snapshot reads. `WorkerFailureCategories.StateChanged`
stays for the `expectedContentHash` precondition.

**Tests (scope only).** `Safety/WriteSafetyServiceTests.cs`, `Safety/WriteToolSafetyTokenTests.cs`,
`Batch/BatchSafetyTokenTests.cs`, `Project/ProjectLifecyclePreviewSafetyTests.cs`, the snapshot
contract and reader tests under `Batch/`, and the token-flow halves of the behaviour, protocol, and
FakeWorker tests. `Safety/WriteSafetyLeaseConcurrencyTests.cs` is rewritten for the lease alone.
`Safety/AuditIsolationTests.cs` and `Safety/ReadOnlyModeTests.cs` stay.

**Documentation.** `README.md` "Write safety", `AGENTS.md` write-safety section and the "reuse the
shared gate" rule, `ARCHITECTURE.md` §7a canonical safety flow and §8, the six operation summaries
that describe the flow, `guides/troubleshooting.md`, `development/local-mcp-testing.md`, and the
two roadmaps. The historical `superpowers/` material is left as is.

## 6. Delivery phases

Serial pull requests from `main`, each independently shippable, each with its own plan and, where
it touches the worker, live TIA Portal V21 acceptance. Sizes are relative.

| Phase | Scope | Exit criteria | Size |
| --- | --- | --- | --- |
| 0 — Decide and tell the truth | Accept this design (amended). Edit `README.md`, `AGENTS.md`, and `ARCHITECTURE.md` §8 so they describe the token flow as a server-side consistency check, not as user consent, and point at this spec. Amend `roadmap/json-contract.md`: Phase 3 becomes "lifecycle onto the guarded write pipeline"; the batch exclusion is resolved by Phase 4 below. | Docs no longer imply a human approves writes. | S |
| 1 — Foundation | `WriteExecution` pipeline, guard catalog and `acknowledge` validation, `dryRun` support, structured envelope records, `WriteAuditLog` with the v2 record, `expectedContentHash` on the two content read tools (additive). Built beside the old machinery; no registered tool changes. Elicitation spike: confirm which clients render form elicitation; record the result in the spec. | Pipeline and guards unit-tested through FakeWorker; old tools unchanged; spike result written down. | M |
| 2 — Lifecycle | Six lifecycle tools onto the pipeline and structured contract: `dryRun`, `acknowledge`, the six lifecycle guards, typed verification. Remove the lifecycle token paths and `ProjectLifecycleTools.cs`. Remove the six tools from the conformance guard's legacy register and add probes. | Live V21 acceptance of open/close/save-as/archive with each guard firing once. | M |
| 3 — Network | `network_write` loses `confirm` and `safetyToken`; gains `dryRun`, `acknowledge`, `deletes_subnet_with_connected_nodes`, and current → requested `effects` (closes the network half of #78). Target resolution stays exactly where it is (fresh read under the lease). Delete `CanonicalWriteSafety.cs`. | Live V21 acceptance of the multi-homed configure and subnet delete paths. | S–M |
| 4 — Batch | The batch redesign: one write tool, sequential resolve-then-mutate, dependent items, `expectedContentHash` enforcement, delete and PLC-mode guards, purposeful `effects` per operation (closes the batch half of #78 and all of #79). Port each snapshot reader's rules into its write method, then delete the reader. Delete the batch token paths, `BatchTools.cs`, and the snapshot contracts. | Live V21 acceptance per operation family; #78 and #79 closed. | L |
| 5 — Retire | Delete `WriteSafetyService.cs` token core, `WriteSafetyTooling.cs`, `SafetyRead`, remaining token tests. Rewrite `ARCHITECTURE.md` §8 and `README.md` write safety, update every operation summary and guide, bump the major version, write release notes and a migration note for agents and skills that hard-code the preview→apply flow. Update the vault concept note. | Zero references to `safetyToken` outside `superpowers/`. | M |

Phases 2, 3, and 4 are independent of each other once Phase 1 has merged; the order above is
smallest-first. Elicitation-backed acknowledgement (§4.6) is a Phase 2 add-on if the spike is
positive, otherwise a later slice.

## 7. Interactions

- **JSON contract roadmap.** Phase 3 as written (typed preview through `CreateCanonicalPreview`,
  canonical token binding) would build lifecycle tokens only to delete them here. Fold it into
  Phase 2 above. Phase 4's retire list grows to include the token core. The batch tools leave the
  conformance guard's legacy register in Phase 4 above.
- **Multiuser design.** Its safety boundary lists invariants (mutations use the repository-wide
  mechanism; selectors, ordered inputs, binding identity, and relevant state are validated; apply
  revalidates immediately before dispatch; unknown outcomes are inspected, never replayed). Every
  invariant survives: §4.1 is the repository-wide mechanism, and "relevant state" is what the
  resolver and guards read at write time. The design's "token or handle format" question is
  answered as "none".
- **Open issues.** #78 and #79 are closed by construction. #77 (`configure_network_device`
  reports success when a change is skipped) is a mutation-outcome bug, not a safety-flow bug, and
  is fixed independently. #74 (`start_plc`/`stop_plc` always fail) gates the
  `changes_plc_operating_mode` guard.
- **Network roadmap.** Its "preview-before-apply for every write" line is rewritten to
  "guarded single-call write with `dryRun`".

## 8. Risks

| Risk | Mitigation |
| --- | --- |
| A concurrent TIA UI edit is overwritten by an agent write. | `expectedContentHash` on content replacement; write-time exact-target resolution everywhere; the lease still serializes MCP-originated writes. Documented as a residual risk for value-setting operations. |
| Agents acknowledge guards reflexively. | The exact-set rule (§4.3) stops blanket acknowledgement; the acknowledgement is visible in the client's tool-call display; elicitation replaces it where the client supports it. |
| Breaking change for existing agent prompts, skills, and client configurations. | Major version; migration note; tool descriptions state the new flow in the first sentence. No compatibility shim: keeping both flows would preserve the machinery this design removes. |
| Snapshot-reader rules are lost when readers are deleted. | Phase 4 ports rule by rule with a test per rule before deletion; the live acceptance reports from PR 5 and PR 6 (2026-09-01 design) are the checklist. |
| Coverage gate moves as ~430 tests are removed. | Deleted tests covered deleted code; new pipeline and guard tests cover new code. Check the scoped rate after each phase. |
| Elicitation is unsupported by the clients that matter. | It is an add-on, not a dependency. §4.3 stands alone. |

## 9. Open questions for discussion

1. **Acknowledgement model.** Agent-side `acknowledge` list as the baseline, elicitation as an
   opt-in upgrade (this proposal), or elicitation-only where supported and refuse otherwise?
2. **Stale-state protection.** Opt-in `expectedContentHash` on the two content tools only (this
   proposal), on more operations, or none at all?
3. **Preview shape.** `dryRun` flag on every write tool (this proposal), or a separate read-only
   preview tool per surface so clients with destructive-tool prompts do not prompt on previews?
4. **Batch tool name.** Rename the pair to a single `write_batch`, or keep `apply_write_batch` as
   the survivor to reduce the naming churn?
5. **Guard catalog.** Which of the proposed `acknowledge` guards should be `info`, and which
   `info` guards should require acknowledgement? Should `discards_unsaved_source_changes` stay a
   hard rejection as it is today?
6. **Phase ordering.** Lifecycle → network → batch (smallest first, this proposal), or batch first
   because it carries the most user-visible pain (#78, #79)?
7. **Compatibility window.** Ship the new tools alongside the old for one release, or cut over in
   one major version (this proposal)?
8. **Audit.** Keep the audit trail at all? If yes, is one record per call sufficient, or is one
   per operation item wanted for forensics?

## 10. Non-goals

- Changing the read-only access mode, its three enforcement layers, or the read tool surface.
- Transactions or rollback; Openness `Transaction`/`ExclusiveAccess` remain deferred.
- Predicting post-write Siemens state in the host.
- Any claim that the server guarantees a human approved a write.
