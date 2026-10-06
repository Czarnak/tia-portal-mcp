# Project overview

MCP server for Siemens TIA Portal V21. Exposes 6 tools in read-only, 16 in read-write (startup default), and 16 in full mode. Read-write permits in-project edits, compile, and lifecycle with one confirmation prompt per actual call. Full runs lifecycle without server elicitation and adds OnlineControl (PLC run/stop). The installer defaults to read-only. Windows-only, requires TIA Portal V21 with Openness enabled.

## Two-process architecture (critical to understand)

`bind_project` retains default/null/explicit `bind` behavior and adds six explicit inspections:
`list_portals`, `list_server_connections`, `list_server_groups`, `list_server_projects`,
`list_local_sessions`, `get_lock_state`. Counts stay 6/16/16. Discovery never adopts; inventory
uses persistent Portal-only attachment, never switches/adopts/opens a project. Selectors are exact
raw-key validated; root group requires `{isRoot:true,name:null}`. Verified foreign-PID refusal is
local/NotSent and preserves binding/cursors; genuine context/transport loss still invalidates.
Sessions cover `currentMachineCurrentUser`; locks are non-atomic observations. Typed inspection
is conditional inside the budgeted binding value (60,000/180,000 characters), not a new envelope.
No lifecycle elicitation/write audit, ALS21 adoption, session content/markings or mutation is added.
See [Multiuser reference](docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).
PR3 received exact live authorization on 2026-10-06. All six inspection actions succeeded against
three disposable session copies and a standalone AP21; all five remote inventories also succeeded
with zero open projects. Healthy verified binding/cursors and worker-loss invalidation/recovery
were observed. Full Task 5 acceptance remains pending matrix cases in the
[partial live report](docs/superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md).

The host (`TiaMcpServer`, net10.0) and the worker (`TiaMcpServer.OpennessWorker`, net48) are separate processes. Siemens Openness DLLs use .NET Framework remoting and **cannot run in a .NET 10 process** — this is why the split exists.

- Host communicates with worker via newline-delimited JSON over stdin/stdout
- The host builds the worker and copies it to `openness-worker/` subdirectory automatically
- The worker restarts automatically after crash or timeout
- `ref/` contains compile-time Siemens stubs so CI can build without TIA Portal installed

**Do not try to run Openness code directly from the host process** — always go through the worker.

## Solution structure

| Project | TFM | Role |
| --------- | ----- | ------ |
| `TiaMcpServer` | net10.0 | MCP stdio server, tool registration, batch engine, safety tokens, CLI (doctor) |
| `TiaMcpServer.Contracts` | netstandard2.0 | Shared DTOs (`WorkerRequest`, `WorkerResponse`, all info/result types) |
| `TiaMcpServer.OpennessWorker` | net48 | Worker that loads `Siemens.Engineering.*`, handles all TIA Portal operations |
| `TiaMcpServer.Tests` | net10.0 | xunit tests; links host source files directly via `<Compile Include>` (not a project reference) |
| `TiaMcpServer.FakeWorker` | net10.0 | Scripted worker stand-in for IPC integration tests |

## Build and test

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1          # -m:1 serializes builds — required to avoid parallel worker build conflicts
dotnet test TiaMcpServer.Tests
```

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
never bind or switch. Only `open_project` and `create_project` open a project; there are no implicit
opens. Worker ownership does not survive detach. Read-only never opens, creates, saves or closes.
Lifecycle confirmation follows access mode. Network uses guarded writes with no server elicitation; legacy batch tools retain tokens.

Lifecycle and Network use the guarded single-call pipeline. Lifecycle confirms every actual
read-write call; Network has `ConfirmsEveryCall=false`, no acknowledge guards, and zero server
elicitation in read-write, full, or dry runs. Legacy generic batches retain preview/apply tokens,
which are server-side consistency checks and never proof of user consent.

- **Generic batch data writes**: call `preview_write_batch` (returns `safetyToken`), then `apply_write_batch` with `confirm=true` + the unchanged operation list and token
- **Network writes**: `network_write(operations, dryRun=false)` executes by default; explicitly set `dryRun:true` for preview. Require exact already-open verified binding and one project; process up to 50 unique-ID items in caller order, stop at first failure, never roll back or replay. Connected deletion is informational; incomplete consequence inventory blocks every mode. Reject legacy `confirm`, `safetyToken`, `acknowledge`, unknown roots and nonboolean `dryRun` at SDK/wrapper entry: normal MCP error, no write audit. Entered validation/binding/guard denials use a canonical root error and one audit v2 record. Read-write actual confirmation is `none`, full actual is `policy`; previews/denials use `none`, and info/block satisfaction is null.
- **Project lifecycle writes** (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`): available in read-write and full. `dryRun=true` resolves targets, effects, and guards without mutation or elicitation. Every actual read-write call requires one form elicitation `accept` plus boolean `confirm:true`, even with only info guards or none. Missing capability, decline, cancel, timeout, or transport failure denies with `access_denied`. Full runs without server elicitation and satisfies acknowledge guards by policy. Block guards stop the call in every mode. Public confirmation arguments and safety tokens are absent.
- Safety tokens are single-use, expire in 10 minutes, and are bound to the exact tool name + host binding revision + requested input + current project state; project-scoped writes additionally require the complete verified project identity
- Reordering, changing input, or project state changes invalidate the token
- Token apply-time state read, token consumption, mutation, verification, and audit run under one pinned project-binding lease. Lifecycle uses explicit preparation and the same lease, re-resolving state after elicitation so an accepted consequence cannot silently change.
- Lifecycle returns one canonical document with typed `result` and `verification`; rejection has top-level `error` and `isError:true`, while attempted mutation/verification failure has `success:false`, `error:null`, and `isError:false`. Inspect possible mutation before retrying.
- Lifecycle calls append one audit v2 record, including dry runs and blocked calls, under `%LOCALAPPDATA%\TiaMcpServer\audit`; confirmation records `user`, `policy`, or `none`, and guard satisfaction is `user`, `policy`, or null. Legacy writes retain their audit stream. Client-returned acceptance does not prove a human saw a prompt.
- The removed startup switch is rejected with: `--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.`
- Doctor reports Warning for an unbound writable session with remediation naming `bind_project`. Project-tree cursors reject binding ID/revision changes as `cursor_binding_mismatch`.
- **No new token-bound write surfaces.** The redesign retires tokens in favor of one guarded
  single-call pipeline (validate, verified binding, resolve targets, guards, `dryRun`, mutate,
  verify, audit). Do not add a new snapshot reader, `SafetyRead` catalog entry, or token-bound
  write tool; a new write domain builds on that pipeline once redesign Phase 1 has landed.

Network read/write roots declare `contractVersion:"1.0"`, warnings arrays and explicit nulls.
Write phases are `preview`, `applied`, `blocked`, `error`; attempted failures have `error:null`
and MCP `isError:false`. Sparse settings keys are `Address`, `SubnetMask`, `PnDeviceName`,
`Subnet`, `IoSystem`; a requested skip fails the item/call but retains typed results.
Budget complete canonical responses at 180,000 characters and individual values at 60,000.
Write admission reserves aggregate encoded IDs/protected summaries before binding; the 256-character
per-ID limit remains unchanged, with no smaller per-ID limit. Root `omission` is null when complete;
whole diagnostic/value omissions can make delivery success false while trusted execution/item/
verification summaries remain true. Audit stores exact delivered document/hash and actual item statuses.
Inspect with original exact selectors before retry; omission metadata is not a selector.

## Structured JSON contract rules (Network Phase 2 and beyond)

`network_read`/`network_write` were the first tools on the opt-in canonical JSON contract
(`TiaMcpServer/Json/CanonicalJson.cs`, `TiaMcpServer/Tools/StructuredToolResult.cs`,
`TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs`), and `browse_project_tree`, `plc_read` and `read_cross_references` also use it. These
rules are durable for any future tool that migrates onto it — not just Network:

- **Reuse the shared gate.** A new structured tool builds on `StructuredToolResult` /
  `StructuredOperationBatch`; do not hand-roll a parallel canonical-JSON mechanism for a new
  domain. Network-only `CanonicalWriteSafety` is retired; retain generic-batch safety infrastructure.
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
  This is exactly the Phase 1 defect Phase 2 removed for Network.
- **Every tool is structured or registered as legacy.**
  `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs` fails when a tool is neither
  on this contract nor listed in its legacy register with a reason. When a tool migrates, remove it
  from that register and add a success probe and a rejection probe. The target envelope and
  migration order are in `docs/roadmap/json-contract.md`; the three batch tools are excluded from
  it pending their split into domain read/write tools (write-safety redesign §4.9 and Phase 4).

See `docs/ARCHITECTURE.md` §7a for the full seam description and the exact host-to-worker
selector boundary, and `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` for the concrete
Network contract these rules describe in the abstract.

## Key conventions

- **`global.json`** pins stable .NET SDK 10.0.400 with `rollForward: latestFeature` and disallows prerelease SDKs — use `dotnet` commands, not version-specific aliases
- **Tests link host source files** via `<Compile Include>` instead of a host project reference. Network/Tools files are explicitly enumerated; add new files to `TiaMcpServer.Tests.csproj` when needed. `Safety/Pipeline` uses a glob. Check actual project wiring before assuming a new source file is tested.
- **Worker methods** are dispatched by `method` string in `WorkerRequest` — add new operations in `TiaMcpServer.OpennessWorker/Program.cs` switch expression, then register them in their owning domain catalog and invoker. A worker method is not automatically a generic batch operation; network operations use their own request, catalog, and invoker.
- **Contract types** live in `TiaMcpServer.Contracts` (netstandard2.0) so both host and worker can share them — no Siemens dependencies here
- **Worker payload JSON** goes through `WorkerJson.SerializePayload`. A new payload contract writes null members and is decoded through the worker-payload reader; `[LegacyNullOmission]` is only for the reasons in `LegacyNullOmissionReason` (a payload that omits nulls cannot go through the reader), and adding or removing one updates `WorkerPayloadNullPolicyRegisterTests`
- Siemens DLLs are **never committed** to the repo or the NuGet package

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
| `docs/superpowers/` | **historical** specs, plans, and acceptance reports — not current documentation |

Two rules that keep this from re-rotting:

- **A new document under `docs/` is not complete until it is listed in `docs/README.md`.** That
  index is the entry point; an unlisted document is unreachable in practice.
- **`README.md` is also the NuGet package readme**, declared via `<PackageReadmeFile>` in
  `TiaMcpServer/TiaMcpServer.csproj`. Relative links do not resolve on nuget.org, so every
  cross-document link in `README.md` must be an absolute
  `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL. Every other document uses
  relative links.

`README.md` is a landing page: pitch, tool list, write-safety model, architecture summary, quick
start, and the documentation map. Procedure belongs in `docs/`, not in the README.
