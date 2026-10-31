# Project overview

MCP server for Siemens TIA Portal V21. Exposes 8 tools in read-only, 19 in read-write (startup default), and the same 19 in full mode. Read-write permits in-project edits, compile, and lifecycle with one confirmation prompt per actual lifecycle call. Full runs lifecycle without server elicitation and adds no tools or operations; PLC run/stop was removed. The installer defaults to read-only. Windows-only, requires TIA Portal V21 with Openness enabled. Content tools (content reads and writes, `compile_check`, and every lifecycle tool except `open_project`/`create_project`) act only on the bound project and fail closed with `binding_conflict` when unbound; only `bind_project` and `open_project` accept a public `projectPath`, and `get_project_status` takes no arguments.

## Two-process architecture (critical to understand)

The host (`TiaMcpServer`, net10.0) and the worker (`TiaMcpServer.OpennessWorker`, net48) are separate processes. Siemens Openness DLLs use .NET Framework remoting and **cannot run in a .NET 10 process** — this is why the split exists.

- Host communicates with worker via newline-delimited JSON over stdin/stdout
- The host builds the worker and copies it to `openness-worker/` subdirectory automatically
- The worker restarts automatically after crash or timeout
- Worker request timeout is parsed once at startup from `--worker-timeout-minutes` or `TIA_MCP_WORKER_TIMEOUT_MINUTES` (CLI > environment > default), accepts whole minutes 5–60 with default/floor 5, applies to every request including `hello`, and is reported with its source by `doctor`.
- `ref/` contains compile-time Siemens stubs (`Siemens.Engineering.Base`, `.Step7` and `.WinCCUnified`) so CI can build without TIA Portal installed

**Do not try to run Openness code directly from the host process** — always go through the worker.

## Multiuser (inspection and local sessions)

`bind_project` retains default/null/explicit `bind` behavior and adds six explicit inspections:
`list_portals`, `list_server_connections`, `list_server_groups`, `list_server_projects`,
`list_local_sessions`, `get_lock_state`. Discovery never adopts; inventory
uses persistent Portal-only attachment, never switches/adopts/opens a project. Selectors are exact
raw-key validated; root group requires `{isRoot:true,name:null}`. Verified foreign-PID refusal is
local/NotSent and preserves binding/cursors; genuine context/transport loss still invalidates.
Sessions cover `currentMachineCurrentUser`; locks are non-atomic observations. Typed inspection
is conditional inside the budgeted binding value (60,000/180,000 characters by default), not a new envelope.
Inspection adds no lifecycle elicitation/write audit or session mutation.
See [Multiuser reference](docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).

Binding selects typed already-open local owners by observed `.amc21` identity, supports basic local
status/startup assertions and explicit file-only `.als21` opening through the guarded lifecycle
pipeline. Cold adoption has null ALS provenance and no worker ownership; known same-ALS reuse
requires continuous owner identity. Conditional local `context` includes descriptive capabilities;
mode remains unknown, remote identity null and active connection unknown.
Worker-owned local sources block a different open even when clean; borrowed sources require
proved preservation. No generic session close or automatic terminal cleanup is added.

Content reads (`browse_project_tree`, `plc_read`, `network_read`,
`read_cross_references`, `hmi_read`) work on local sessions. Read handlers use the worker's
`WithEngineeringRoot` (`ProjectBase`).
`ProjectCapabilityCatalog.OperationFor` is the only worker-method→capability map for both host
and worker gates; a new worker method must map through it.

Local sessions also support `plc_write`, `network_write`, `compile_check` and `save_project`.
Content handlers, write and compile included, use `WithEngineeringRoot`, or `WithActiveContext`
when native access or transactions also need the Portal handle. Content services take `ProjectBase`;
capability gates determine eligible container kinds. A local
`save_project` calls `LocalSession.Save()` with the identity re-check adjacent to the save; it
never updates from or checks in to the Project Server, and read-write confirmation says so.
`StandaloneOnlyProbes` keeps the IO-system and subnet qualification probes standalone-only.
Create, save-as, archive and close stay standalone-only.

## Solution structure

| Project | TFM | Role |
| --------- | ----- | ------ |
| `TiaMcpServer` | net10.0 | MCP stdio server, tool registration, guarded write pipeline, structured batch engine, CLI (doctor) |
| `TiaMcpServer.Contracts` | netstandard2.0 | Shared DTOs (`WorkerRequest`, `WorkerResponse`, all info/result types) |
| `TiaMcpServer.OpennessWorker` | net48 | Worker that loads `Siemens.Engineering.*`, handles all TIA Portal operations |
| `TiaMcpServer.Tests` | net10.0 | xunit tests; links host source files directly via `<Compile Include>` (not a project reference) |
| `TiaMcpServer.FakeWorker` | net10.0 | Scripted worker stand-in for IPC integration tests |

## Build and test

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1          # -m:1 serializes builds — required to avoid parallel worker build conflicts
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
```

Packaging tests (`Category=Packaging`) are excluded from the default run and run in their own CI
workflow only when packaging files change. When you touch a product `.csproj`,
`Directory.Build.props`, `global.json`, `scripts/verify-doctor-package.ps1`,
`scripts/verify-reference-stubs.ps1` or `reference-stubs/`, also run
`dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`.

CI/stub build (no TIA Portal needed):

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

Local dev (uses real TIA assemblies):

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

## Write safety model

Access tiers are capability presets in the shared `OperationPolicyCatalog`, enforced at discovery,
host dispatch before worker activity, and worker dispatch before Siemens calls. Unknown operations
are denied in every mode. `SessionSelection` is permitted in every mode: `bind_project` adopts or
switches to an already-open project without opening, creating, saving or closing. Ordinary reads
never bind or switch. Content tools take no `projectPath`: with no verified binding (a configured `--project` is grounded by status first) they return `binding_conflict` and the caller must `bind_project` first; a retired `projectPath` is a canonical `validation_error`. Only `open_project` and `create_project` open a project; there are no implicit
opens. Worker ownership does not survive detach. Read-only never opens, creates, saves or closes.
Lifecycle confirmation follows access mode. `plc_write`, `network_write`, `hmi_write` and `vci_write` use guarded writes with no server elicitation; no tool uses safety tokens.

Lifecycle, Network, PLC, HMI and VCI writes use the guarded single-call pipeline. Lifecycle confirms every
actual read-write call; Network, PLC, HMI and VCI have `ConfirmsEveryCall=false`, no acknowledge guards, and
zero server elicitation in read-write, full, or dry runs. The generic batch tools
(`preview_write_batch`/`apply_write_batch`), safety tokens, the token core and the legacy write audit
stream were removed.

- **PLC writes**: `plc_write(operations, dryRun=false)` executes by default; set `dryRun:true` for preview. Same binding, ordering, 50-item, stop-at-first-failure and no-rollback rules as Network; the 14 operations are listed in the tool description. Content writes pass `content` plus `expectedContentHash` from a `plc_read` in the same format; stale content is `state_changed`, and content over the value budget (60,000 characters by default, raised with `--max-item-chars`) has no hash and cannot be written. Write contract `2.0` (`plc_read` stays `1.0`): creates are not pre-checked for collisions, and collision directions beyond the cases recorded in the PLC acceptance report are unverified. A native `EngineeringTargetInvocationException` in a mutation worker method fails `engineering_rejected` with `engineeringError` (see Failure categories below). Guards are block (`plc_ob_singleton_exists`; `plc_state_unverifiable`, narrowed to incomplete inventory, unreadable update/delete targets, delete consequences or constant values, and unexportable content) or info (`plc_deletes_block`, `plc_deletes_group_contents`, `plc_deletes_table_contents`, `plc_address_overlap`). Update and delete targets still pre-flight `target_not_found`; a create into a missing parent group or folder fails at mutation. Later items may depend on objects created earlier in the call (`dependsOn`, replanned before their mutation). Verification reads back content only: `trusted` for completed non-content items, `contentHash` (a re-export, equal to the next `plc_read` hash and usable as the next `expectedContentHash`, never computable from the content) for content updates, `superseded` for overwritten items (a short-path `update_block_logic` is conservatively superseded by a later same-name block item or any group delete on that PLC); a failed non-content item has no verification entry, so inspect it with `plc_read`. `update_tag` applies a rename first and reads `Name` back, because Portal silently ignores a colliding rename: the item fails `postcondition_failed` before any other property changes. Audit confirmation is `none` in read-write and `policy` in full.
- **Network writes**: `network_write(operations, dryRun=false)` executes by default; explicitly set `dryRun:true` for preview. Write contract `2.0` (`network_read` stays `1.0`). Require exact already-open verified binding and one project; process up to 50 unique-ID items in caller order, stop at first failure, never roll back or replay. One hardware snapshot plans the call; no per-item re-planning or verification read-back. Connected deletion is informational; incomplete consequence inventory blocks every mode. Delete effects list initial connections and omit nodes connected earlier in the same call. Configure stops at the first native rejection in Address, SubnetMask, auto-generation, PN name, Subnet, IoSystem order; a carried typed failure result retains `deviceName`, `appliedSettings`, `messages`, and earlier settings may remain applied. Native exception type determines `engineering_rejected` versus `worker_operation_failed` (below). Completed items receive `trusted` or `superseded`; failed/skipped items have no verification entry. A requested subnet rename is compared with the already-read name before commit: a silently ignored rename fails `postcondition_failed`. See the [Network reference](docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md). Reject legacy `confirm`, `acknowledge`, unknown roots and nonboolean `dryRun`, like every argument-shape error in all 19 tools, with the tool's canonical document (`validation_error`, `isError:true`) and no audit record. Entered validation/binding/guard denials use a canonical root error and one audit v2 record. Read-write actual confirmation is `none`, full actual is `policy`; previews/denials use `none`, and info/block satisfaction is null.
- **Project lifecycle writes** (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`): available in read-write and full. `dryRun=true` resolves targets, effects, and guards without mutation or elicitation. Every actual read-write call requires one form elicitation `accept` plus boolean `confirm:true`, even with only info guards or none. Missing capability, decline, cancel, timeout, or transport failure denies with `access_denied`. Full runs without server elicitation and satisfies acknowledge guards by policy. Block guards stop the call in every mode. Public confirmation arguments and safety tokens are absent.
- **HMI writes**: `hmi_write(operations, dryRun=false)` provides nine create/update/delete operations for Unified tag tables, tags and discrete/analog alarms, on standalone and bound local-session roots. Read contract stays 1.1; write contract is 2.0. The 50-item ordered pipeline stops at the first failure without batch rollback. One initial public-read evidence set per HMI plans the call; its reads are sequential and non-atomic. There is no per-item re-plan, worker read-back or automatic Validate. Three guards remain: incomplete evidence blocks; deleted contents/references inform. Initial delete planning caps cross-reference queries at 200. Collision, member/reference existence, existing-connection and bound-tag `dataType` semantic refusals are removed; request shape still requires `plcTag` with `connection` and `plcTag` or `dataType` for a create. Failed creates attempt identity-checked cleanup; failed updates retain changes. Typed Siemens failures carry `engineeringError` except for `portal_lost`, which takes precedence; a partial outcome is conditional on writer allocation. `appliedProperties` lists completed stages, not atomic properties: a failed stage can already have changed a culture. Shared verification marks completed items `trusted` or `superseded`; an outcome-bearing failed attempt can supersede earlier success even with `target_not_found`. Inspect public reads, Validate and HMI UI compile separately; mutations invalidate read snapshots. The rebind/read/second-call member restriction is removed. Direct tag case-only rename can fail natively; native connection-only assignment remains unprobed. See [HMI reference](docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md). Retained locator entries still require fresh uniqueness traversal and clear on next locator use after owner change.
- **VCI**: `vci_read` (three operations, all modes) and `vci_write` (six operations, writable modes) require a verified standalone project. Writes execute by default, preview with `dryRun:true`, use 1–50 unique-ID items and stop after the first failed item without batch rollback or server elicitation. Scope expansion calls native VCI only on leaves. Planning never reads status; sync uses `GetStatus()`, fresh owner checks and a plain adjacent recheck for workspace sync. Import prefilters before one ExclusiveAccess transaction and needs `compile_check` after success; saving the project persists workspaces and mappings. Export safety scans grow with leaves × mappings; `SimaticSD` export can crash Portal. A matching timeout breadcrumb and `portal_lost` retain attempted-leaf context, invalidate binding and require explicit recovery. See the [VCI reference](docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md).
- Planning, mutation, verification, and audit run under one pinned project-binding lease. Lifecycle uses explicit preparation and the same lease, re-resolving state after elicitation so an accepted consequence cannot silently change.
- Lifecycle returns one canonical document with typed `result` and `verification`; rejection has top-level `error` and `isError:true`, while attempted mutation/verification failure has `success:false`, `error:null`, and `isError:false`. Inspect possible mutation before retrying.
- Lifecycle calls append one audit v2 record, including dry runs and blocked calls, under `%LOCALAPPDATA%\TiaMcpServer\audit`; confirmation records `user`, `policy`, or `none`, and guard satisfaction is `user`, `policy`, or null. Client-returned acceptance does not prove a human saw a prompt.
- The removed startup switch is rejected with: `--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.`
- Doctor reports Warning for an unbound writable session with remediation naming `bind_project`. Project-tree cursors reject binding ID/revision changes as `cursor_binding_mismatch`.
- **Portal loss**: `Program.Execute` delegates to `WorkerExecution`: `NonRecoverableException` maps to `portal_lost` across worker domains; an escaping `EngineeringException` does too only when the session observed unexpected disposal of a current Portal attachment during that execution. Deliberate detach, stale callbacks and earlier disposal do not qualify; classification uses cached event evidence, never message matching or native liveness probes. The host invalidates verified binding/cursors without replay. Broad Network recovery, XML-bundle export, cached HMI liveness and Multiuser inventory exclude fatal loss through `OpennessExceptions.IsRecoverable`; original fatal inventory exceptions survive final identity checks. Strict HMI discovery preserves original engineering exceptions; lenient inventory diagnostics remain unchanged. The locator evicts entries on a different managed root handle without native equality; this is cache ownership, never a binding identity check. Existing narrower property policies, typed post-mutation failures, cleanup warnings and completed-operation stamping retain their contracts. VCI's writer keeps its outcome-bearing mapping. Broader native fault locations and write interruptions are untested.
- **No token-bound write surfaces.** Tokens are retired in favor of one guarded single-call
  pipeline (validate, verified binding, resolve targets, guards, `dryRun`, mutate, verify, audit).
  A new write domain builds on that pipeline (`Safety/Pipeline`), as `plc_write` and
  `network_write`, `hmi_write` and `vci_write` do; do not reintroduce a snapshot reader, `SafetyRead` catalog, or token.

Network read roots declare `contractVersion:"1.0"`; write roots declare `contractVersion:"2.0"`.
Both retain warnings arrays and explicit nulls.
Write phases are `preview`, `applied`, `blocked`, `error`; attempted failures have `error:null`
and MCP `isError:false`. Sparse `appliedSettings` keys are `Address`, `SubnetMask`,
`PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`; effect settings split
`IoSystem` into scalar `IoSystemSubnet`/`IoSystemNumber`. Effect `currentSettings` are snapshot
strings/nulls without inspection metadata. Shared verification is `{success,operations,omission}`;
entries are `{operationId,success,check,expected,actual,message}` with `trusted` or `superseded`,
without native read-back. Configure keys include device, interface names/positions and raw node ID;
optional type evidence does not change identity. A later attempted item can supersede an earlier
success even when it fails after possible mutation; precondition refusals do not supersede.
Failed/skipped Network items have no entry, so `verification.success:true` with root `success:false`
is by design. Immediate verification, `skippedSettings`, `finalChecks` and device-count fields are
retired. Host preflight selector misses are `target_not_found`/`target_ambiguous`;
`postcondition_failed` covers late worker drift, unreadable created subnet identity and ignored
subnet rename (plus PLC's `update_tag` rename read-back).

Failure categories from Siemens: `engineering_rejected` is an `EngineeringTargetInvocationException` thrown during a mutation worker method (`OperationPolicyCatalog` `ProjectMutation`); the mutation flag spans the entire method, including connection, identity and target checks before any native write, so this category does not prove a setter/create was reached. Every other Siemens `EngineeringException` stays `worker_operation_failed`; reads never produce `engineering_rejected`, and `portal_lost` wins over both. The type decides, never message text (messages follow the Portal UI language). Both carry the conditional `engineeringError {type, message, inner[]}` (message cut at 4,000 characters, chain at 8, silently). The shared item-failure projection copies it for every write tool. `WorkerExecution.Classify`/`Describe` serve the shared catch and Network configure wrap sites; the latter produce `WorkerOperationException.EngineeringError` and `ConfigureNetworkDeviceOutcome`. A wrapped engineering failure is also checked for current-execution Portal disposal; `portal_lost` retains its partial configure outcome and invalidates binding. Other Network mutation paths and PLC tag, table, group, block-create and delete paths can reach the shared Siemens catch. HMI write wraps preserve the classification and `engineeringError` alongside `HmiWriteOutcomeInfo`; VCI wraps and PLC content importers (`update_block_logic`, `update_type_content`) still flatten Siemens failures without producing this evidence. Open follow-ups are in `docs/IMPROVEMENT_LOG.md`.
`PersistentWorkerTransport` validates raw `configureNetworkDeviceOutcome` with `NetworkPayloadContract.ValidateEnvelopeOutcome` before the permissive `WorkerJson.Envelope` reader. Wrongly cased/duplicate outcome roots or missing/unknown/wrongly cased/duplicate members in a non-null outcome fail `protocol_error` without publishing the rejected evidence. An absent or explicit-null outcome remains allowed and leaves applied state unknown; do not normalize malformed recovery evidence into an apparently empty applied subset.
Worker identity comparisons of live Openness objects (owner resolve-back, ancestor containment, read-selector interface-owner diagnostics) use `object.Equals`, never `ReferenceEquals`; the cycle guards over live ancestor/owner hierarchies keep `ReferenceEquals`.
Budget complete canonical responses at 180,000 characters and individual values at 60,000 by default.
Those are `StructuredOperationBatchPayloadBudget.Default*Chars`; `MaxItemChars`/`MaxDocumentChars` are the
effective limits, raised once at startup by `--max-item-chars`/`--max-document-chars` (or
`TIA_MCP_MAX_ITEM_CHARS`/`TIA_MCP_MAX_DOCUMENT_CHARS`, raise-only, at most 1,000,000). New budget code
reads the effective properties, never a literal; project-tree pages, hardware pages and the worker
compile report keep their own fixed 60,000 limits.
Write admission reserves aggregate encoded IDs/protected summaries before binding; the 256-character
per-ID limit remains unchanged, with no smaller per-ID limit. Root `omission` is null when complete;
whole diagnostic/value omissions can make delivery success false while trusted execution/item/
verification summaries remain true. Audit stores exact delivered document/hash and actual item statuses.
Inspect with original exact selectors before retry; omission metadata is not a selector.

## Structured JSON contract rules

The canonical JSON contract lives in `TiaMcpServer/Json/CanonicalJson.cs`,
`TiaMcpServer/Tools/StructuredToolResult.cs` and
`TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs`. These rules bind every tool on it
and any future tool that migrates onto it:

- **Reuse the shared gate.** A new structured tool builds on `StructuredToolResult` /
  `StructuredOperationBatch`; do not hand-roll a parallel canonical-JSON mechanism for a new
  domain. `CanonicalWriteSafety` and the generic-batch token infrastructure are retired.
- **Text and structured documents are the same document.** A migrated tool's `content` text block
  and its `structuredContent` come from exactly one `CanonicalJson.Serialize` call. They must
  never be built from two independent renderings that could drift apart.
- **Worker success payloads are typed.** A migrated tool declares exactly one CLR result type per
  operation (see `TiaMcpServer/Network/NetworkPayloadContract.cs` for the pattern) and rejects a
  payload that does not decode as that type — category `protocol_error` — rather than forwarding
  worker-shaped data under a schema that does not describe it. The rejected payload is never
  echoed back.
- **Required members are enforced by the reader.** A host decode of a worker payload whose root
  writes explicit nulls goes through the worker-payload reader
  (`CanonicalJson.DeserializeWorkerPayload` / `NormalizeWorkerPayload`). Every member is required
  unless it is declared conditional with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`.
  A new conditional member is added to `ConditionalMemberRegisterTests`, with a description stating
  when it appears. Do not hand-write required-member lists.
- **No nested JSON strings.** A migrated tool's operation results are real JSON objects/arrays
  under the response document, never an escaped JSON string a caller has to parse a second time.
- **Every tool is structured or registered as legacy.**
  `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs` fails when a tool is neither
  on this contract nor listed in its legacy register with a reason. When a tool migrates, remove it
  from that register and add a success probe and a rejection probe. The target envelope and
  migration order are in `docs/roadmap/json-contract.md`.

See `docs/ARCHITECTURE.md` §7a for the full seam description and the exact host-to-worker
selector boundary, and `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` for the concrete
Network contract these rules describe in the abstract.

## Key conventions

- **`global.json`** pins stable .NET SDK 10.0.400 with `rollForward: latestFeature` and disallows prerelease SDKs — use `dotnet` commands, not version-specific aliases
- **Tests link host source files** via `<Compile Include>` instead of a host project reference. Network/Tools files are explicitly enumerated; add new files to `TiaMcpServer.Tests.csproj` when needed. `Safety/Pipeline` uses a glob. Check actual project wiring before assuming a new source file is tested.
- **HMI worker methods** are named `hmi_<operation>` (e.g. `hmi_list_tag_tables`) because `OperationPolicyCatalog` is one flat namespace and PLC already owns `list_tag_tables`/`create_tag`/...; `HmiOperationCatalog.WorkerMethod` is the single mapping. The worker method names live in `TiaMcpServer.Contracts/Hmi` (`HmiReadMethods.All`/`HmiWriteMethods.All`) and feed `OperationPolicyCatalog`, `ProjectCapabilityCatalog` and the worker dispatch switch; `HmiOperationCatalog.WorkerMethod` stays the public-to-worker map, and tests pin the two against each other. Read parameters travel in `WorkerRequest.HmiQuery`; the nine writes use `WorkerRequest.HmiWrite` and map to the `hmi_write` capability through `ProjectCapabilityCatalog.OperationFor`. Every HMI content handler uses `WithEngineeringRoot`. Classic HMIs (type name `Siemens.Engineering.Hmi.HmiTarget`) fail `target_kind_unsupported`; the worker does not reference `Siemens.Engineering.WinCC.dll`. `hmi_read` is read-only, in every mode, 1-50 items, paging `limit` 1-2000 default 100. Paged operations also return an opt-in snapshot `nextCursor` (host-sealed by `HmiPageCursorCodec`; the worker `HmiSnapshotStore` holds at most 4 snapshots, 10-minute idle lifetime, offset-only scans included). `validate` adds name-scoped `memberTags`, `loggingTags` and `screenItems` (`name` required, not in `all`); see [HMI reference](docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md).
- **Worker methods** are dispatched by `method` string in `WorkerRequest` — add new operations in `TiaMcpServer.OpennessWorker/Program.cs` switch expression, then register them in their owning domain catalog and invoker. A worker method is not automatically a public operation; Network, PLC and HMI each use their own request, catalog, and invoker.
- **Contract types** live in `TiaMcpServer.Contracts` (netstandard2.0) so both host and worker can share them — no Siemens dependencies here
- **Worker payload JSON** goes through `WorkerJson.SerializePayload`. A new payload contract writes null members and is decoded through the worker-payload reader; `[LegacyNullOmission]` is only for the reasons in `LegacyNullOmissionReason` (a payload that omits nulls cannot go through the reader), and adding or removing one updates `WorkerPayloadNullPolicyRegisterTests`
- Siemens DLLs are **never committed** to the repo or the NuGet package

## Plans, specs and evidence (lifecycle)

These rules override the default output locations of the superpowers skills (brainstorming: "Write the validated design (spec) to"; writing-plans: "Save plans to") and of any similar tool.

- Specs go to `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`, plans to `docs/superpowers/plans/YYYY-MM-DD-<topic>.md`, run evidence to `docs/superpowers/evidence/YYYY-MM-DD-<topic>/`. These three directories are a transient per-PR working area: collapsed in diffs and empty on `main`.
- Never link a transient file from outside `docs/superpowers/`; cite it as "PR #N, commit `<sha>`".
- Before pushing documentation changes, run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` (`python3` on Linux/macOS).
- Before the PR merges, delete everything you added there in a final commit (`docs: prune working files`). First move what must survive: a lasting decision to `docs/adr/` (one ADR, see its README), behavior to `docs/ARCHITECTURE.md` or `docs/SupportedOperations/`, follow-ups to `docs/IMPROVEMENT_LOG.md`, the live-verification report to `docs/superpowers/acceptance/reports/`, and verified raw Openness call/response files to `docs/superpowers/acceptance/reports/inputs/` (listed in its `README.md`). MCP-level traces, preflights, restoration dumps and timing outputs are not kept; use `scripts/timing/` for measurements.

## Documentation layout

`docs/` is grouped by audience, not by document type:

| Path | Audience |
| --- | --- |
| `docs/guides/` | people **using** the server — installation, client configuration, troubleshooting |
| `docs/development/` | people **building** the server — building, MCP sandbox testing, packaging |
| `docs/roadmap/` | direction — per-area tactical roadmaps (root `ROADMAP.md` is strategic) |
| `docs/SupportedOperations/` | per-area operation reference |
| `docs/ARCHITECTURE.md` | the design; keep current per its own §11 |
| `docs/IMPROVEMENT_LOG.md` | engineering log — open follow-ups first, completed work at the end |
| `docs/adr/` | architecture decision records — why the design is as it is; project history |
| `docs/superpowers/` | live-verification reports under `acceptance/reports/`; transient working area for plans, specs, evidence (empty on `main`) |

Two rules that keep this from re-rotting:

- **A new document under `docs/` is not complete until it is reachable from `docs/README.md`**,
  directly or through the index of its directory. An unreachable document is unfindable in practice.
- **`README.md` is also the NuGet package readme**, declared via `<PackageReadmeFile>` in
  `TiaMcpServer/TiaMcpServer.csproj`. Relative links do not resolve on nuget.org, so every
  cross-document link in `README.md` must be an absolute
  `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL. Every other document uses
  relative links.

`README.md` is a landing page: pitch, tool list, write-safety model, architecture summary, quick
start, and the documentation map. Procedure belongs in `docs/`, not in the README.

## Project history

This file states current rules only; it records no history. To find why something is the way it is:
- Decisions and project evolution: `docs/adr/README.md` (each ADR cites the original plans/specs).
- What was verified live, with scope and limits: the reports in `docs/superpowers/acceptance/reports/`; kept raw Openness evidence in its `inputs/README.md`.
- Open follow-ups: `docs/IMPROVEMENT_LOG.md`. Current design: `docs/ARCHITECTURE.md`. Per-area contracts: `docs/SupportedOperations/`.
- Removed plans, specs and evidence: `git show docs-archive-2026-10-30:<path>`; find a path with `git ls-tree -r --name-only docs-archive-2026-10-30 -- docs/superpowers | grep <term>`, or search history with `git log -S"<term>" -- docs/`.

Do not copy acceptance results or dated "live verified" statements into this file.
