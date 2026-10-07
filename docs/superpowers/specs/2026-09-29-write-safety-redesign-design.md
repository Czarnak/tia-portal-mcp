# Write-Safety Redesign: Retiring Preview→Apply Tokens

**Date:** 2026-09-29
**Status:** Design accepted 2026-09-29. Every open question in §9 was settled with the maintainer
the same day; points marked **Decided 2026-09-29** record the answer next to the design they
change. Phases 0, 1, 1b, 2 (lifecycle, amended by the
[lifecycle tiers design](2026-10-01-lifecycle-tiers-and-project-binding-design.md)), 3 (Network) and
4 (PLC domain tools, see the delivery note before §7) are delivered. Phase 4 also carried the
Phase 5 deletions; the Phase 5 cleanup was delivered 2026-10-07 (see its delivery note). Only the
Phase 5 release work (major version, release notes, agent migration note, one tag) remains.
**Supersedes:** the token-flow parts of
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
| Stale-state protection (state hash in token) | `WriteSafetyService`, all snapshot readers | **Drop the token hash.** Replace with a *required* `expectedContentHash` precondition on content replacement (§4.5). Decided 2026-09-29. |
| Token issuance, expiry, single use, `confirm` flag | `WriteSafetyService`, every write tool signature | **Drop.** |
| Two binding presentations | `WriteSafetyService` vs `CanonicalWriteSafety` | **Drop**; one canonical path. |
| Two-tool batch pair | `preview_write_batch` / `apply_write_batch` | **Out of scope here.** A separate redesign splits the batch into domain read/write tools (`block_read`, `tag_write`, …). Those tools adopt the pipeline of §4.1 as they are created; the legacy pair is retired when they cover it (§4.9). Decided 2026-09-29. |

The one real loss is protection against a project change between the agent's last read and its
write, for example the user editing a block in the TIA Portal UI while the agent works. The current
token covers this with an opaque hash the agent never sees. The replacement (§4.5) covers the case
that matters, content replacement, with a precondition bound to what the agent actually read. The
maintainer works in the same project as the agent, so this precondition is mandatory, not opt-in.

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
- **Two passes (decided 2026-09-29).** Every item is resolved and every guard evaluated before any
  mutation, so `blocked` always means nothing ran. An item the resolver marks as depending on an
  earlier item of the same call is resolved again immediately before its own mutation. If that
  late resolution fires an `acknowledge` guard the call did not acknowledge, or any `block` guard,
  the item fails with `guard_blocked` and the call stops there.
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
| `discards_unsaved_source_changes` | `open_project` with `forceRebind` | `block` | The source project has unsaved changes and would be closed. Stays a hard rejection (decided 2026-09-29): save or close the source explicitly first. |
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
guards that fired for this call. **Decided 2026-09-29** (the table above and an earlier draft of
this paragraph disagreed): a malformed array fails with `validation_error` and `phase: error`. That
covers a blank or duplicated id, an unknown id, the id of an `info` or `block` guard, and the id of
a guard that did not fire. A fired `acknowledge` guard missing from the array, or any fired `block`
guard, refuses the write with `phase: blocked` and the new failure category `guard_blocked`, and
`guards` names each one. A dry run never blocks: it reports every fired guard with
`acknowledged: true` or `false`, so the agent learns the exact set to send. One acknowledged id
covers every firing of that guard in the call. This prevents a blanket "acknowledge everything"
habit from working, and it puts the specific consequence into the tool-call arguments, where a
client with a permission prompt shows it to the human. That is the strongest lever a server has
over client-side consent: make the dangerous intent visible in the call itself.

### 4.4 `dryRun` (decided 2026-09-29)

Every write tool accepts `dryRun: bool = false`. A dry run executes the pipeline through guard
evaluation, mutates nothing, and returns `phase: preview` with the same `effects` and `guards`
members an applied write would carry. For content replacement it includes the bounded diff
evidence from `BatchPreviewDiff`. There is no token in the response and nothing to carry into a
later call. This keeps "the server tells you the consequences" without a mandatory second call.

Accepted trade-off: with `dryRun` on a `Destructive=true` tool, a client that prompts on
destructive tools prompts for the dry run too. The domain split already gives every write tool a read-only
sibling, and a client that prompts on a dry run shows the human the plan, which is not a bad
outcome. A separate preview tool per surface was rejected as naming churn.

### 4.5 Required stale-content precondition (decided 2026-09-29)

`update_block_logic` and `update_type_content` **require** `expectedContentHash`. The read tools
(`get_block_content`, `get_type_content`) return the matching `contentHash` of the exact format
they served, so a content replacement always follows a read of what it replaces. A missing hash is
`validation_error` before any worker call. If the fresh content read at write time differs from the
hash, the item fails with `state_changed` and nothing is written. The rule exists because the
maintainer and the agent edit the same project: the agent must never overwrite an edit it has not
seen. This is the HTTP `If-Match` pattern: a
guard bound to what the agent actually read, not to an opaque server-side snapshot. Tools that set
explicit values (`update_tag`, `configure_network_device`) do not need it: their exact-target
resolution at write time already refuses a renamed or deleted target, and the new values are the
request.

**Hash format (decided 2026-09-29).** `contentHash` is format-tagged: `xml:sha256:<hex>` or
`source:sha256:<hex>`, over the exact text the read served. A write whose `format` differs from the
hash's tag fails with `validation_error` naming both formats, not with a confusing `state_changed`.
A read omits `contentHash` when its content was truncated or omitted for size, and on
`withDependencies` reads, which cannot be written back: a hash for content the agent never saw
would defeat the precondition.

### 4.6 Asking the human (decided 2026-09-29)

> **Superseded 2026-10-01** by
> [lifecycle tiers and project binding](2026-10-01-lifecycle-tiers-and-project-binding-design.md)
> §3: `--confirm-with-user` and the agent `acknowledge` list are removed; `read-write` asks the user
> on every lifecycle call and `full` never asks. Elicitation stays the mechanism.

The acknowledgement model is settled: elicitation is the main mechanism and is on by default; the
agent-side `acknowledge` array of §4.3 applies only when the switch below is turned off (revised
2026-09-29 after the Phase 1 spike; the default is provisional until the maintainer has tested it
further). The mechanisms below all depend on the client.

- **Elicitation (form mode). Decided.** With the switch `--confirm-with-user` (name
  provisional, on by default) on and `server.ClientCapabilities?.Elicitation` present, an `acknowledge`-severity
  guard is satisfied only by the user accepting an `ElicitAsync` prompt that carries the guard
  message and a single boolean. The agent's `acknowledge` list is ignored while the switch is on.
  Decline, cancel, and request timeout all fail the write with `access_denied`. With the switch
  on and the capability absent, `acknowledge`-severity writes are refused with a message naming
  the missing client capability rather than falling back to the agent: the switch means what it
  says. With the switch off, behaviour is exactly §4.3.
- **Multi-round-trip requests.** The 2026-07-28 revision lets a tool return `input_required` with
  an elicitation request and a `requestState`, and the client retries the call with the user's
  answer attached. This is the protocol-native version of preview→confirm and needs no server-side
  token store. It is the eventual home of the `acknowledge` flow once clients adopt the revision.
- **Per-tool forced approval marker: dropped (decided 2026-09-29).** Claude Code shows its
  permission prompt on every call to a tool whose `tools/list` entry carries
  `_meta["anthropic/requiresUserInteraction"] = true`. The Phase 1 spike showed that Codex
  ignores it (Appendix A). A mechanism that only one client honours cannot carry the safety
  model, and elicitation already reaches the user in every client tested. The `--user-approval`
  switch is therefore not built.

Honesty clause for the documentation: even with elicitation, the server only knows that the client
returned `accept`. Whether a human saw the prompt depends on the client. The server never claims to
guarantee consent.

The Phase 1 spike (Appendix A) showed that Claude Code, Codex, and MCP Inspector all render form
elicitation, while only Claude Code honours the marker. **Decided 2026-09-29:** elicitation is the
main mechanism for asking the human, every outcome other than an explicit accept blocks, and the
marker is dropped.

### 4.7 Response envelope

Aligned with the target envelope in the JSON contract roadmap, delivered through
`StructuredToolResult` as one `CanonicalJson` document:

| Member | Content |
| --- | --- |
| `tool`, `contractVersion`, `success`, `error`, `warnings` | As in the roadmap. |
| `phase` | `preview` (dry run), `applied`, `blocked` (a guard refused), or `error`. |
| `guards` | Every guard that fired: `{ id, severity, operationId, message, acknowledged }`. `operationId` is null for a call-level guard; `acknowledged` is `true` or `false` for an `acknowledge` guard and null for `info` and `block` guards. |
| `effects` | What the write did or would do, per item: resolved target identity, the change in current → requested terms, and content diff evidence where applicable. |
| `batch` or `result` | The typed per-item results (`StructuredOperationItem`) or single result. |
| `verification` | The typed post-write read, when the tool performs one. |

`isError` is `true` only for rejection before anything ran (validation, access mode, binding,
`blocked`). A write that ran and failed reports `success: false` with `isError: false`.

### 4.8 Audit (decided 2026-09-29: stays, and stays detailed)

The audit trail is a first-class seam, not a by-product of the token flow. The pipeline writes
through an `IWriteAuditSink` (name provisional) whose only shipped implementation is the existing
JSONL directory, so a later consumer (a viewer, a database, a per-project log) plugs in without
touching the pipeline. The maintainer has later plans for this data, so the record errs on the
side of detail.

One canonical record per call replaces the two current shapes. Call level: timestamp, tool,
`contractVersion`, `recordVersion`, `phase`, the `ProjectBindingSnapshot`, access mode, the
ordered requested operations as typed JSON, the fired guards with severity and how each
`acknowledge` guard was satisfied (`agent`, `user` via elicitation, or not at all for a blocked
call), the exact response document and its hash, and wall-clock duration. Item level, one entry
per operation in request order: `operationId`, operation, the resolved target identity as the
resolver saw it, the checked preconditions (`expectedContentHash` and the fresh hash), the item
status (`succeeded`, `failed`, `skipped`), its failure category and message, its warnings, and its
own duration. Blocked and dry-run calls are audited too, marked by `phase`, so the log shows what
was refused and what was only previewed, not just what was written.

**File stream (decided 2026-09-29).** The new records go to their own files in the audit directory,
`writes-yyyy-MM-dd.jsonl` (UTC date, UTF-8 without a byte-order mark), each record carrying
`recordKind: "write"` and `recordVersion: 1`. A consumer reads one record shape; the legacy
`yyyy-MM-dd.jsonl` files stop growing when the last token-flow tool is retired in Phase 5.

### 4.9 Generic batch tools (decided 2026-09-29: separate redesign)

The generic batch pair is not redesigned here. A separate effort splits it into domain read/write
tools in the `network_read`/`network_write` shape (`block_read`, `block_write`, `tag_read`,
`tag_write`, and so on). This design only fixes what those write tools must do so that the token
machinery can go:

- run every mutation through the pipeline of §4.1 with `dryRun` and `acknowledge`;
- resolve targets sequentially at write time, so an item may depend on an earlier item in the
  same call (closes #79); in a dry run such an item reports `effects.dependsOn: <operationId>`
  instead of failing;
- require `expectedContentHash` on content replacement (§4.5);
- describe each item's concrete target and change in `effects` (closes #78);
- keep stop-on-first-failure, no rollback, and the partial-write warning on the failed item.

`preview_write_batch` and `apply_write_batch` stay registered, on their current token flow and in
the conformance guard's legacy register, until the domain tools cover every operation they offer.
Then they are retired together with the snapshot readers behind them (§5, Phase 4).

### 4.10 Tool surface after the change

| Today | After |
| --- | --- |
| `preview_write_batch` + `apply_write_batch` | retired once the domain write tools (separate redesign) cover every operation |
| six lifecycle tools with `confirm` + `safetyToken` | same six tools with `dryRun` + `acknowledge`, structured envelope |
| `network_write` with `confirm` + `safetyToken` | `network_write` with `dryRun` + `acknowledge` |

The read-write tool count depends on the domain split. Either way this is a breaking change to
every write tool's input schema and output shape and belongs to the next major version, released
once (decided 2026-09-29: phases merge to `main` as they complete; nothing is tagged until Phase 5).

### 4.11 Access-mode tiers (decided 2026-09-29)

> **Superseded 2026-10-01** by
> [lifecycle tiers and project binding](2026-10-01-lifecycle-tiers-and-project-binding-design.md)
> §3–§4: `ProjectLifecycle` moves into `read-write` (prompted), `full` adds only `OnlineControl`,
> requests never open a project implicitly, and `bind_project` binds in every mode. The binding
> routes described below were never implemented.

Today there are two modes, `read-only` and `read-write`, and `read-write` allows everything. The
maintainer asked whether to add per-tool server permissions or one more mode for the operations
that cannot be reverted. The decision is the second, and the unit is the capability class, not
the tool name.

The boundary that matters in TIA Portal is persistence. An in-memory edit to a block, tag, or
network object is undone by closing the project without saving; TIA's own undo also covers much of
it. Saving, saving as, archiving, creating, opening, and closing change what is on disk or which
project the session is bound to, and the server cannot undo them. Starting or stopping a PLC is
not project state at all. `OperationCapability` already draws exactly these lines:
`ProjectMutation` and `Compile` on one side, `ProjectLifecycle` and `OnlineControl` on the other.
A third mode is therefore a preset over classes that exist:

| Mode | Capability classes | Registered tools | Undo path |
| --- | --- | --- | --- |
| `read-only` | `Observe`, `TemporaryExport` | the four read tools | none needed |
| `read-write` | + `Compile`, `ProjectMutation` | + `compile_check`, `network_write`, the domain write tools | close without saving in the TIA UI, or TIA undo |
| `full` | + `ProjectLifecycle`, `OnlineControl` | + the six lifecycle tools, PLC start/stop | none |

In `read-write` the session still binds without `open_project`: through the `--project` startup
path, through a `projectPath` on a read when nothing is attached (the existing `OpenRequested`
behaviour, which opens but never switches or closes), or through the project already open in the
TIA Portal UI. The human saves or discards in the UI. That is the workflow the maintainer
described: agent and human in the same open project, with the human owning persistence.

`SafetyRead` disappears with the snapshot readers. `McpAccessMode` gains one value,
`OperationPolicyCatalog.IsAllowed` becomes a preset lookup instead of a two-way branch, tool
registration in `Program.cs` gains one tier, and the read-only test suite gains a `read-write`
ceiling suite. Moving lifecycle out of `read-write` is a breaking change for existing
configurations and rides the same major release; the migration note says "if you used
`read-write` and need save or close, use `full`".

Per-tool server-side allow and deny lists are not recommended. The client already offers exactly
that, for example Claude Code's `settings.local.json` allow rules, and a server-side copy would
have to track every tool rename of the domain split. What the server must own is the ceiling: the
client's allow list is the client's decision, while a mode is enforced at worker dispatch
regardless of which client connected, and hides the tools it excludes from discovery so the agent
is never tempted. Modes set the ceiling, elicitation on guarded calls (§4.6) and the client prompt
decide per call, and the client allow list fine-tunes prompting under that ceiling. If a finer
server-side cut is ever needed, the capability class is the unit to expose
(`--deny-capability online-control`), not the tool name.

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
| 1 — Foundation | `WriteExecution` pipeline, guard catalog and `acknowledge` validation, `dryRun` support, structured envelope records, `IWriteAuditSink` with the JSONL implementation and the detailed record, `contentHash` on the two content read tools (additive). Built beside the old machinery; no registered tool changes. Spike: wire `ElicitAsync` and the `anthropic/requiresUserInteraction` marker behind switches, verify against Claude Code, Codex Desktop, and MCP Inspector; record results in Appendix A. | Pipeline and guards unit-tested through FakeWorker; old tools unchanged; spike results written down. | M |
| 1b — Access-mode tiers and confirmation switch | Add the `full` mode as a preset over capability classes, move `ProjectLifecycle` and `OnlineControl` behind it, wire the `--confirm-with-user` switch (on by default), extend the read-only enforcement tests with a `read-write` ceiling suite. The approval marker and `--user-approval` were dropped after the spike (§4.6). Independent of the pipeline; can ship before or after 1. | Three-layer enforcement holds for the new tier; the switch default is covered by tests. | S |
| 2 — Lifecycle | Six lifecycle tools onto the pipeline and structured contract: `dryRun`, `acknowledge`, the six lifecycle guards, typed verification. Remove the lifecycle token paths and `ProjectLifecycleTools.cs`. Remove the six tools from the conformance guard's legacy register and add probes. | Live V21 acceptance of open/close/save-as/archive with each guard firing once. | M |
| 3 — Network | `network_write` loses `confirm` and `safetyToken`; gains `dryRun`, `acknowledge`, `deletes_subnet_with_connected_nodes`, and current → requested `effects` (closes the network half of #78). Target resolution stays exactly where it is (fresh read under the lease). Delete `CanonicalWriteSafety.cs`. | Live V21 acceptance of the multi-homed configure and subnet delete paths. | S–M |
| 4 — Domain write tools | Owned by the separate batch-split redesign, which gets its own spec. Each domain write tool (`block_write`, `tag_write`, …) is built on the pipeline with the requirements of §4.9. As a family lands, its snapshot reader's fail-closed rules move into the worker write methods with tests, and the reader is deleted. When the domain tools cover every legacy operation, delete the batch token paths, `BatchTools.cs`, the snapshot contracts, and the two legacy tools. | Live V21 acceptance per operation family; #78 and #79 closed. | L (in that redesign) |
| 5 — Retire and release | Delete `WriteSafetyService.cs` token core, `WriteSafetyTooling.cs`, `SafetyRead`, remaining token tests. Rewrite `ARCHITECTURE.md` §8 and `README.md` write safety, update every operation summary and guide, bump the major version, write release notes and a migration note for agents and skills that hard-code the preview→apply flow. Tag the single release. | Zero functional references to `safetyToken` outside `superpowers/` (rejection tests and migration notes may name it; see the Phase 5 delivery note); one tagged major release. | M |

Phases 2, 3, and 4 are independent of each other once Phase 1 has merged; the order is
smallest-first (decided 2026-09-29). Phases merge to `main` as they complete, but the package is
released once, after Phase 5, so `main` carries a mix of old and new write flows in between and no
tag is cut during that window. The spike was positive (Appendix A), so elicitation-backed
acknowledgement (§4.6) lands with Phase 2.

**Phase 1b delivery, 2026-09-30:** capability presets now enforce read-only/read-write/full at
discovery, host dispatch, and worker dispatch. The current surfaces contain 4/8/14 tools;
lifecycle and PLC runtime control require full, including legacy batch control and internal
lifecycle probes. Read-write initial attachment remains supported without read-driven switching
or closing. The immutable default-on confirmation setting accepts bare `--confirm-with-user`,
`=true`, and `=false`; contradictory and malformed arguments fail startup. Phase 1b registers
the setting only. Phase 2 must connect it to guarded writes: on ignores agent acknowledgement
and requires supported elicitation with `accept` plus `confirm: true`; absent capability,
decline, cancel, or timeout fails with `access_denied`. Off uses exact-set agent acknowledgement.
Legacy token tools and `UserConfirmation.For` retain their behavior until that migration.
The serial Release stub build and all 4,414 offline tests passed, with 93.76% scoped line
coverage against the 80% CI gate. This is not live Siemens or client acceptance; see the
[Phase 1b implementation record](../plans/2026-09-30-write-safety-phase1b-access-modes.md).

**Phase 4 delivery, 2026-10-07 (`plc_write`):** the domain write tools landed as one tool,
`plc_write`, built on the pipeline per the
[PLC read/write design](2026-10-05-plc-read-write-and-cross-references-design.md) and its
[PR B plan](../plans/2026-10-06-plc-write.md), live-accepted in read-write and full
([validation report](../acceptance/reports/2026-10-07-plc-write-validation.md)). It amends this
design as follows:

- **Names.** The domain tools are `plc_read` and `plc_write`, not `block_write`/`tag_write`; the
  guards use a `plc_` prefix (`plc_deletes_block`, `plc_deletes_group_contents`,
  `plc_deletes_table_contents`, plus `plc_name_collision`, `plc_block_exists`,
  `plc_default_tag_table`, `plc_state_unverifiable`, `plc_attribute_unreadable`,
  `plc_address_overlap`).
- **Delete guards are `info`.** The three delete guards that §4.3 makes `acknowledge` are `info`;
  `plc_write` registers only `block` and `info` guards, so it never elicits in any mode.
- **No `changes_plc_operating_mode`.** It was dropped together with PLC run/stop: `start_plc`,
  `stop_plc` and the `OnlineControl` permission in full were removed.
- **Stale content.** A §4.5 hash mismatch is a call-level `state_changed` at plan time; the worker
  re-checks immediately before import and fails that item with `state_changed`.
- **Phase 5 deletions moved here.** The token core (`WriteSafetyService`), `WriteSafetyTooling`,
  `SafetyRead` and the legacy audit stream were deleted in this PR rather than in Phase 5.
- **#78 and #79.** This delivery meets the Phase 4 exit criterion; the PR closes #78 and #79.

**Phase 5 delivery, 2026-10-07 (cleanup, release excluded):** with the token core already gone,
this step removed the last token-era leftovers: the FakeWorker state-drift scenario that existed
only to invalidate a token through the whole-project state hash, token wording in FakeWorker
comments, two read-only tests that asserted `confirm`/`safetyToken` could not bypass the policy,
a retired `read_*_safety_snapshot` method row in the transport-guidance test, and the
`NetworkIntrospectionSafetySnapshotTests` name (now `NetworkHardwareConfigDeterminismTests`),
together with token wording in test names, test comments and one `WorkerRequest` doc comment.
`ARCHITECTURE.md` §8, `README.md` write safety, the operation summaries and the guides already
described the guarded pipeline after Phase 4; two stale `ARCHITECTURE.md` passages (the batch
snapshot decode call sites in §7a and the read-only test list in §10) were corrected here.
No production code references a safety token. **Exit criterion as applied:** "zero references to
`safetyToken`" is read as zero functional references. The remaining mentions outside
`superpowers/` are deliberate: tests that assert the argument is rejected or absent, and migration
notes that tell callers to drop it. The release work (major version, release notes, agent
migration note, tag) is left to the maintainer.

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
- **Batch-split redesign.** It owns tool names, per-domain schemas, and delivery order for the
  domain read/write tools. It inherits §4.9 as requirements and §4.1 as the seam. Its first write
  tool cannot land before Phase 1 of this design.

## 8. Risks

| Risk | Mitigation |
| --- | --- |
| A concurrent TIA UI edit is overwritten by an agent write. | `expectedContentHash` on content replacement; write-time exact-target resolution everywhere; the lease still serializes MCP-originated writes. Documented as a residual risk for value-setting operations. |
| Agents acknowledge guards reflexively. | The exact-set rule (§4.3) stops blanket acknowledgement; the acknowledgement is visible in the client's tool-call display; elicitation replaces it where the client supports it. |
| Breaking change for existing agent prompts, skills, and client configurations. | Major version; migration note; tool descriptions state the new flow in the first sentence. No compatibility shim: keeping both flows would preserve the machinery this design removes. |
| Snapshot-reader rules are lost when readers are deleted. | Phase 4 ports rule by rule with a test per rule before deletion; the live acceptance reports from PR 5 and PR 6 (2026-09-01 design) are the checklist. |
| Coverage gate moves as ~430 tests are removed. | Deleted tests covered deleted code; new pipeline and guard tests cover new code. Check the scoped rate after each phase. |
| Elicitation is unsupported by the clients that matter. | Retired by the Phase 1 spike: Claude Code, Codex, and MCP Inspector all support it (Appendix A). A client without the capability gets a refusal, not a silent approval. |

## 9. Open questions for discussion

All settled 2026-09-29:

1. **Acknowledgement model.** `--confirm-with-user` is on by default, so elicitation is the only
   way to satisfy an `acknowledge` guard; turning it off falls back to the agent-side
   `acknowledge` list (§4.6). Revised after the Phase 1 spike; the default is provisional.
   *Superseded 2026-10-01: confirmation follows the access mode (see the §4.6 note).*
3. **Preview shape.** `dryRun` flag on every write tool (§4.4).
1b. **Forced-approval marker.** Dropped after the Phase 1 spike; only Claude Code honours it
   (§4.6, Appendix A). `--user-approval` is not built.
9. **Access-mode tiers.** A third mode, `full`, as a preset over the existing capability classes;
   `read-write` narrows to in-project edits and compile. No per-tool server lists (§4.11).
   *Superseded 2026-10-01: lifecycle returns to `read-write` behind a prompt (see the §4.11 note).*

2. **Stale-state protection.** Required `expectedContentHash` on the content-replacement tools;
   the maintainer and the agent share the project and the agent must not undo the maintainer's
   work. No hash on value-setting operations.
4. **Batch tools.** Reworked separately into domain read/write tools; this design supplies the
   pipeline and the requirements in §4.9.
5. **Guard catalog.** `discards_unsaved_source_changes` stays a hard rejection (`block`). The
   remaining severities in §4.3 stand as proposed unless implementation shows otherwise.
6. **Phase ordering.** Smallest first: lifecycle → network → domain write tools.
7. **Compatibility window.** Delivered in phases, released once, as one major version.
8. **Audit.** Stays, detailed, behind a sink interface (§4.8). The maintainer has later plans for
   the data.

## 10. Non-goals

- Changing the read-only access mode, its three enforcement layers, or the read tool surface.
- Transactions or rollback; Openness `Transaction`/`ExclusiveAccess` remain deferred.
- Predicting post-write Siemens state in the host.
- Any claim that the server guarantees a human approved a write.

## Appendix A. How elicitation works, and what was verified

**Mechanism.** Elicitation is a request the *server* sends to the *client* while a tool call is in
progress (`elicitation/create`, protocol revision 2025-06-18 and later). The client shows the
request to the user, collects an answer, and returns it; the tool call is blocked until then. Two
modes exist: **form** (the server supplies a message and a flat JSON Schema of primitive fields,
strings, numbers, booleans, enums; the client renders a dialog) and **url** (the server supplies a
link the user opens for an out-of-band flow such as sign-in). The client answers with one of three
actions: `accept` with the filled content, `decline` (the user said no), or `cancel` (the user
dismissed the dialog). A client advertises support in its `initialize` capabilities; a server must
check that capability before calling, because a client without it cannot answer. The specification
forbids using form mode for secrets such as passwords or API keys.

**In this server.** The C# SDK exposes it as `McpServer.ElicitAsync(ElicitRequestParams, ct)`;
a tool method receives the `McpServer` through dependency injection and checks
`server.ClientCapabilities?.Elicitation` first. A `close_project` with unsaved changes would send
the message "Project 'X' has unsaved changes made 3 minutes ago. Discard them and close?" with a
single boolean field; `accept` with `true` satisfies the guard, anything else refuses the write
with `access_denied`. The value over the client's own permission prompt is that the server asks
with knowledge the client does not have: which project, what is unsaved, what will be destroyed.

**Multi-round-trip requests.** Revision 2026-07-28 adds a stateless variant: the tool returns
`resultType: input_required` carrying the same elicitation request plus an opaque `requestState`,
and the client re-issues the tool call with the user's answer attached. The SDK throws
`InputRequiredException` for this and reports client support through `server.IsMrtrSupported`.
It removes the blocked-call problem but needs a client on the newest revision.

**Verified 2026-09-29.**

| Client | Result | Source |
| --- | --- | --- |
| Claude Code | Renders form and URL elicitation dialogs with no configuration. On protocol revision 2026-07-28 declares `elicitation: {form: {}, url: {}}`. A tool call waiting on a dialog is not backgrounded. Users can auto-answer via an `Elicitation` hook, and an `ElicitationResult` hook can alter the answer. Honours `_meta["anthropic/requiresUserInteraction"]` from v2.1.199. | Claude Code documentation, MCP page, sections "Respond to MCP elicitation requests" and "Require approval for a specific tool" |
| C# SDK 2.2.0 (this server) | `ElicitAsync`, form and URL modes, `InputRequiredException`, `IsMrtrSupported`; tool `_meta` is a settable `JsonObject`. | SDK documentation |
| Codex Desktop, MCP Inspector | Not verified in the documentation; see the spike below. | — |
| Claude Desktop, VS Code, others | Not verified and not planned. | — |

**Phase 1 spike, 2026-09-29.** Live runs of `spike_marked_probe` (carries
`anthropic/requiresUserInteraction`) and `spike_elicitation_probe` (one form elicitation) against
a `--read-only` server with `TIA_MCP_APPROVAL_SPIKE=1`. Only the accept path was run in the normal
modes; answer paths such as decline, dismiss, and timeout depend on client configuration and were
not tested client by client.

| Client | Marker probe | Elicitation probe |
| --- | --- | --- |
| Claude Code 2.1.285 | Permission prompt shown, also in auto mode. | Capability declared; dialog shown; `accept` returned after the user confirmed. |
| Codex (CLI 0.159.0 on the maintainer's machine) | No prompt; the call ran. The marker is ignored. | Default approval: capability declared, dialog shown, `accept` after confirmation. Unrestricted mode: no dialog, and the client returned `decline`. |
| MCP Inspector | Ran without a prompt (the Inspector has no approval layer). | Dialog shown. |

**Conclusion (decided 2026-09-29).** Elicitation is the main mechanism for asking the human: it
was the only one that reached the user in all three clients. The marker works only in Claude
Code, so it is dropped. Client-side behaviour in detail (answer paths, timeouts,
permission modes) is outside this project's scope. When an agent runs unrestricted, the
server cannot force a human into the loop. It can still fail closed, and every outcome except an
explicit `accept` with `true` (`decline`, `cancel`, timeout, missing capability) blocks the
operation. Codex's unrestricted mode answers `decline`, so such operations are refused instead of
being silently approved.

**Limits to state in the documentation.**

- The server learns only that the client returned `accept`. A hook, a script, or an auto-approving
  client can produce that answer without a human. Elicitation raises the bar; it does not prove
  consent.
- The tool call blocks while the dialog is open. In a headless or remote session with nobody
  present, the call waits for the SDK request timeout and then fails. The pipeline must treat a
  timeout exactly like `decline`.
- Elicitation is per call and per guard. It is not a substitute for accurate `destructiveHint`
  annotations, which are what let a client decide to prompt before the call even starts.
