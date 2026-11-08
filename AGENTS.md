# Project overview

MCP server for Siemens TIA Portal V21. Exposes 9 tools in read-only, 21 in read-write (startup default), and the same 21 in full mode. Read-write permits in-project edits, compile, and lifecycle with one confirmation prompt per actual lifecycle call. Full runs lifecycle without server elicitation and adds no tools or operations; PLC run/stop was removed. The installer defaults to read-only. Windows-only, requires TIA Portal V21 with Openness enabled. Content tools (content reads and writes, `compile_check`, and every lifecycle tool except `open_project`/`create_project`) act only through the verified binding, on the bound project and, for `library_read`, on Portal-scope objects of the bound Portal, and fail closed with `binding_conflict` when unbound; only `bind_project` and `open_project` accept a public `projectPath`, and `get_project_status` takes no arguments.

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
Create, save-as, archive and close stay standalone-only.

## Solution structure

| Project | TFM | Role |
| --------- | ----- | ------ |
| `TiaMcpServer` | net10.0 | MCP stdio server, tool registration, guarded write pipeline, structured batch engine, CLI (doctor) |
| `TiaMcpServer.Contracts` | netstandard2.0 | Shared contracts, catalogs and Siemens-free logic (`WorkerRequest`, `WorkerResponse`, all info/result types) |
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
- **Network writes**: `network_write(operations, dryRun=false)` executes by default; explicitly set `dryRun:true` for preview. Write contract `2.0` (`network_read` stays `1.0`). Require exact already-open verified binding and one project; process up to 50 unique-ID items in caller order, stop at first failure, never roll back or replay. One hardware snapshot plans the call; no per-item re-planning or verification read-back. Connected deletion is informational; incomplete consequence inventory blocks every mode. Delete effects list initial connections and omit nodes connected earlier in the same call. Configure stops at the first native rejection in Address, SubnetMask, auto-generation, PN name, Subnet, IoSystem order; a carried typed failure result retains `deviceName`, `appliedSettings`, `messages`, and earlier settings may remain applied. Native exception type determines `engineering_rejected` versus `worker_operation_failed` (below). Completed items receive `trusted` or `superseded`; failed/skipped items have no verification entry. A requested subnet rename is compared with the already-read name before commit: a silently ignored rename fails `postcondition_failed`. See the [Network reference](docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md). Reject legacy `confirm`, `acknowledge`, unknown roots and nonboolean `dryRun`, like every argument-shape error in all 21 tools, with the tool's canonical document (`validation_error`, `isError:true`) and no audit record. Entered validation/binding/guard denials use a canonical root error and one audit v2 record. Read-write actual confirmation is `none`, full actual is `policy`; previews/denials use `none`, and info/block satisfaction is null.
- **Project lifecycle writes** (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`): available in read-write and full. `dryRun=true` resolves targets, effects, and guards without mutation or elicitation. Every actual read-write call requires one form elicitation `accept` plus boolean `confirm:true`, even with only info guards or none. Missing capability, decline, cancel, timeout, or transport failure denies with `access_denied`. Full runs without server elicitation and satisfies acknowledge guards by policy. Block guards stop the call in every mode. Public confirmation arguments and safety tokens are absent.
- **HMI writes**: `hmi_write(operations, dryRun=false)` provides nine create/update/delete operations for Unified tag tables, tags and discrete/analog alarms, on standalone and bound local-session roots. Read contract stays 1.1; write contract is 2.0. The 50-item ordered pipeline stops at the first failure without batch rollback. One initial public-read evidence set per HMI plans the call; its reads are sequential and non-atomic. There is no per-item re-plan, worker read-back or automatic Validate. Three guards remain: incomplete evidence blocks; deleted contents/references inform. Initial delete planning caps cross-reference queries at 200. Collision, member/reference existence, existing-connection and bound-tag `dataType` semantic refusals are removed; request shape still requires `plcTag` with `connection` and `plcTag` or `dataType` for a create. Failed creates attempt identity-checked cleanup; failed updates retain changes. Typed Siemens failures carry `engineeringError` except for `portal_lost`, which takes precedence; a partial outcome is conditional on writer allocation. `appliedProperties` lists completed stages, not atomic properties: a failed stage can already have changed a culture. Shared verification marks completed items `trusted` or `superseded`; an outcome-bearing failed attempt can supersede earlier success even with `target_not_found`. Inspect public reads, Validate and HMI UI compile separately; mutations invalidate read snapshots. The rebind/read/second-call member restriction is removed. Direct tag case-only rename can fail natively; native connection-only assignment remains unprobed. See [HMI reference](docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md). Retained locator entries still require fresh uniqueness traversal and clear on next locator use after owner change.
- **VCI**: `vci_read` (three operations, all modes, contract `1.0`) and `vci_write` (six operations, writable modes, contract `2.0`) require a verified standalone project. Writes execute by default, preview with `dryRun:true`, use 1–50 unique-ID items and stop after the first failed item without batch rollback or server elicitation. Typed native failures carry `engineeringError`; outcomes list up to 20 leaf failures in encounter order with 500-character messages and `failuresOmitted`, without capping counts. Import prefilter failures can precede synchronization failures, so recording order is not global scope order. Export leaves existing-file and mapping-key behavior to Portal: native overwrite, companion-file deletion and disruption of another mapping are accepted possibilities. Workspace conflict protection compares exact roots only (case-insensitive, trailing separators ignored); duplicate names reach Portal and nested roots are allowed. Directory segments and containment remain guarded; leaves retain only path-boundary/ADS checks. Length counts the complete path up to 259 characters, with `.xml` taking four characters and other formats reserving eight. Only the first item per case-insensitive workspace is planned; dependents get no inventory, counts, evidence guards or re-plan, while operation info guards remain. Shared verification reports `trusted`/`superseded` without native read-back; failed items have no entry. Scope expansion calls native VCI only on leaves. Planning never reads status; sync uses `GetStatus()`, fresh owner checks and a plain adjacent recheck for workspace sync. Import prefilters before one ExclusiveAccess transaction and needs `compile_check` after success; saving the project persists workspaces and mappings. `SimaticSD` export can crash Portal. A matching timeout breadcrumb and VCI's direct `portal_lost` path retain attempted-leaf context, invalidate binding and require explicit recovery. See the [VCI reference](docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md).
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

Failure categories from Siemens: `engineering_rejected` is an `EngineeringTargetInvocationException` thrown during a mutation worker method (`OperationPolicyCatalog` `ProjectMutation`); the mutation flag spans the entire method, including connection, identity and target checks before any native write, so this category does not prove a setter/create was reached. VCI deliberately classifies its read-only `create_workspace` planning recheck as a read: even an `EngineeringTargetInvocationException` there remains `worker_operation_failed` with `engineeringError`. Every other Siemens `EngineeringException` stays `worker_operation_failed`; reads never produce `engineering_rejected`, and `portal_lost` wins over both. The type decides, never message text (messages follow the Portal UI language). Both carry the conditional `engineeringError {type, message, inner[]}` (message cut at 4,000 characters, chain at 8, silently). The shared item-failure projection copies it for every write tool. `WorkerExecution.Classify`/`Describe` serve the shared catch and Network configure wrap sites; the latter produce `WorkerOperationException.EngineeringError` and `ConfigureNetworkDeviceOutcome`. A wrapped engineering failure is also checked for current-execution Portal disposal; `portal_lost` retains its partial configure outcome and invalidates binding. Other Network mutation paths and PLC tag, table, group, block-create and delete paths can reach the shared Siemens catch. HMI and VCI write wraps preserve classification and `engineeringError` alongside their typed partial outcomes; `portal_lost` takes precedence and retains an available outcome without `engineeringError`. Only PLC content importers (`update_block_logic`, `update_type_content`) still flatten Siemens failures without producing this evidence. Open follow-ups are in `docs/IMPROVEMENT_LOG.md`.
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
- **Library reads**: `library_read(operations)` is registered in every mode (contract `1.0`, 1-50 independent items, five operations: `list_global_libraries`, `inspect_library`, `list_children`, `list_versions`, `list_dependencies`). Read-only means read-only: no Open/Retrieve/Save/Close/Create/Delete or setter in any library read path. Every call needs a verified binding (all-global batches included); global reads use the bound Portal, so the tool has no Portal argument ([ADR-0015](docs/adr/0015-portal-scope-reads-under-binding-gate.md)). All library worker methods use the bound route (`SendBoundProjectRequestAsync`, worker `WithActiveContext`) and are `Observe`; the names live in `TiaMcpServer.Contracts/Library` (`LibraryReadMethods`) and map through `ProjectCapabilityCatalog.OperationFor`: `library_project_*` to `library_project_read` (`standaloneOnly`, not a tool name), `library_list_global_libraries` and `library_global_*` to `library_read` (`projectContent`, `notYetDelivered` on server projects). `list_global_libraries` enumerates `GetGlobalLibraryInfos()` (every available library, with nullable `libraryType` and `isOpen`); the other global operations resolve only libraries open in the bound Portal (exact name or case-insensitive full path), and an unopened match is `target_not_found`. Paging uses host-sealed snapshot cursors on the generic worker core (`PageSnapshotStore<T>`, `SnapshotPager`) shared with HMI, but with its own library store (at most 4 snapshots, 10 idle minutes); the frozen row order is paged with live values. A flag that cannot be read is null with a diagnostic, never false. The worker raises `binding_conflict` only for identity or Portal-attachment loss. Type selectors accept `typeName` or `typeGuid`; omitted `folderPath` searches the whole type tree, `[]` means root, a path narrows the folder. `list_dependencies` accepts `versionNumber` or `versionGuid` and selects the default when omitted; ambiguous matches fail `target_ambiguous`. Library writes are the separate `library_write` tool (next bullet). See [Library reference](docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md).
- **Library writes**: `library_write(operations, dryRun=false)` (write contract `2.0`, read-write/full, 20 operations: six global-library lifecycle, two folder, four master-copy and eight type operations) uses `Safety/Pipeline`: 1-50 unique-ID items in caller order, stop at first failure, no batch rollback or replay, no token/public confirmation argument, every call needs a verified binding. `LibraryWriteMethods.Validate` owns closed members. Worker methods are `library_global_<operation>` or `library_project_<operation>`; plans are Observe `library_global_plan`/`library_project_plan`, mutations are `ProjectMutation`. `ProjectCapabilityCatalog.OperationFor` maps global methods to `library_write` (`projectContent`, allowed on local sessions) and project methods to `library_project_write` (`standaloneOnly`). Any project-library selector on either side, or `create_master_copy`, `instantiate_master_copy`, `create_type`, `create_type_version`, `release_type_version`, `instantiate_type_version`, takes the project route. `CapabilityOperations(items)` refuses the first unsupported capability before worker activity. `ConfirmsCall(items)` prompts once in read-write only for calls containing global-library lifecycle; content-only and full calls never prompt. `PlansBeforeApply(items)` defaults to true for other domains and is false for libraries; previews and prompting calls still plan. This hook is safe only for domains without acknowledge guards. Unplanned apply returns `effects:[]`, resolves targets and checks next to each native call, and may use earlier creations. A planned supported dependency is descriptive `createdByItem`; a library created/opened/retrieved in a prompting call must be used later. A collision or missing target on unplanned apply fails the item after earlier items have run. See [ADR-0016](docs/adr/0016-library-write-staging-and-lifecycle-prompt.md) and [ADR-0017](docs/adr/0017-library-writes-apply-without-planning.md).
- **Library lifecycle and master copies**: Global-library lifecycle is Portal-scope, exempt from the project lifecycle's standalone-only rule, with no worker-owned/borrowed distinction. The binding snapshot's Portal process id supplies identity. `close_global` requires `closeAction`; `save` calls `Save()` only when modified (clean read-only close returns `saved:false`); discard always reports `library_discards_changes`. Save/close/archive require `UserGlobalLibrary`; archives use `Compressed` and append `.zal21` case-insensitively. Master-copy create/copy checks names OrdinalIgnoreCase within master copies only (`library_master_copy_exists`); a native rename fails `postcondition_failed` with `actualName`, and instantiate uses `ThrowIfExists`. `open_global` always calls `Open`; `alreadyOpen` is plan-time evidence when planned. Create/open/retrieve report returned `Path.FullName`. Duplicate global names are `target_ambiguous` (use path). `delete_folder` previews contents recursively, capped at 200 with `contentsTruncated`, with `library_deletes_folder_contents`; unreadable check evidence is `library_state_unverifiable`. Library folder names are ordinal; PLC groups OrdinalIgnoreCase. Library writes do not drop library-read snapshots. Typed `libraryWriteOutcome` is validated by `LibraryWritePayloadContract.ValidateEnvelopeOutcome` before the permissive reader; malformed evidence is `protocol_error`. `LibraryVerificationPolicy` derives resolved identity keys from success results/failure outcomes; shared verification is `trusted`/`superseded` without an additional native read-back. An attempted failure without outcome falls back to library-level supersession unless it is a precondition refusal. See [Library reference](docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md#writes-library_write).
- **Library type writes**: `create_type`, `create_type_version`, `release_type_version`, `set_default_type_version`, `synchronize_type`, `instantiate_type_version`, `delete_type`, `delete_type_version` support FB, FC and PLC UDT. `create_type` takes a plain PLC source without `scope`, checks whole-project-library name collision and existing type linkage, then exports and converts it under `ExclusiveAccess` and one project transaction; failed linkage rolls back conversion. `create_type_version` takes a linked PLC instance, SimaticML XML and fresh `xml:sha256` `expectedContentHash`; mismatch is `state_changed`. It stages operation-owned content and calls `Versions.CreateFromDocuments(new DirectoryInfo(path), ...)` without a test environment; never pass the DirectoryInfo returned by `Directory.CreateDirectory`. `replaceInWork` defaults false; Portal refuses an existing in-work version without replacement. All type operations are content-only and never prompt. Release is project-only, selects in-work, never automatically creates/releases dependencies, and rejects a number not higher than every released version (`library_version_not_higher`); omitted number uses Portal's prefilled value. Version components are nonnegative Int32 values, exactly three parts and not `0.0.0`; Portal's safe release range beyond probed values is unknown. Exact `versionNumber` or `versionGuid` is required by `set_default_type_version`; it does not accept `inWork`. Other omitted version selectors use the default. Release/set-default/synchronize return defaults before/after and never restore them. Instantiate/synchronize include dependencies; native placement may reuse a same-type instance or choose a suffixed name for a plain-object collision, so inspect `instancePaths`. Deletes scan for instances/master copies only in previews/planned calls; native apply decides refusal. A compile-first workflow is recommended for library-placed PLC objects; Portal can require compilation for inconsistent instances, but uncompiled status alone does not prove export failure. Type content changes use `plc_read` → `create_type_version` → release, never `plc_write`. Type results/outcomes carry native receipts separately from shared verification.
- **Worker methods** are dispatched by `method` string in `WorkerRequest` — add new operations in `TiaMcpServer.OpennessWorker/Program.cs` switch expression, then register them in their owning domain catalog and invoker. A worker method is not automatically a public operation; Network, PLC, HMI and Library each use their own request, catalog, and invoker.
- **Contract types** live in `TiaMcpServer.Contracts` (netstandard2.0) so both host and worker can share them — no Siemens dependencies here
- **Worker payload JSON** goes through `WorkerJson.SerializePayload` with `ExplicitNullPayload`. Payload contracts write null members and are decoded through the worker-payload reader. A member omitted when null must be declared conditional with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` and registered in `ConditionalMemberRegisterTests`.
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
