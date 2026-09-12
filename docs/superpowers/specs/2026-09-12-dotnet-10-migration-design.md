# .NET 10 Migration and Dependency Alignment Design

**Status:** Proposed for review. This document authorizes no implementation, GitHub mutation, or
live TIA Portal run.

## Goal

Move the MCP host, test suite, and FakeWorker from .NET 8 to .NET 10 while retaining the required
`net48` Siemens Openness worker and `netstandard2.0` shared-contract boundary. Incorporate the
useful version intent from the open Dependabot pull requests, update superseded patch versions to
the current stable releases, and preserve the public MCP tool contract and package layouts.

## Current state

- `TiaMcpServer`, `TiaMcpServer.Tests`, and `TiaMcpServer.FakeWorker` target `net8.0`.
- `TiaMcpServer.Contracts` targets `netstandard2.0` so both modern .NET and `net48` can consume it.
- `TiaMcpServer.OpennessWorker` targets `net48`; Siemens Openness must remain out of the modern
  .NET host process.
- `global.json` names SDK `8.0.400` but uses `latestMajor`, so a machine with .NET 10 installed can
  already select a .NET 10 SDK while the projects still target .NET 8.
- CI and release jobs explicitly install `8.0.x`.
- The NuGet tool has a canonical `tools/net8.0/any/` package layout. The separate release binary is
  an explicit self-contained `win-x64` publish with `PackAsTool=false`.

## Dependabot input

Seven open Dependabot PRs reduce to three logical upgrades:

| Dependency | Dependabot proposal | PRs | Design decision |
| --- | --- | --- | --- |
| `Microsoft.SourceLink.GitHub` | `8.0.0` -> `10.0.400` | [#48](https://github.com/Czarnak/tia-portal-mcp/pull/48) | Adopt the .NET 10 line, using current stable `10.0.401`. |
| `ModelContextProtocol` | `1.2.0` -> `2.2.0` | [#49](https://github.com/Czarnak/tia-portal-mcp/pull/49), [#51](https://github.com/Czarnak/tia-portal-mcp/pull/51), [#52](https://github.com/Czarnak/tia-portal-mcp/pull/52) | Adopt `2.2.0`, with explicit protocol/schema compatibility tests because this is a major SDK upgrade. |
| `System.Text.Json` | `8.0.5` -> `10.0.11` | [#50](https://github.com/Czarnak/tia-portal-mcp/pull/50), [#56](https://github.com/Czarnak/tia-portal-mcp/pull/56), [#57](https://github.com/Czarnak/tia-portal-mcp/pull/57) | Adopt the .NET 10 line, using current stable `10.0.12`. |

All seven PRs currently report non-mergeable and have no associated pull-request workflow run.
They are evidence for dependency intent, not merge-ready implementation branches. Landing newer
versions on `main` should supersede them; closing or commenting on PRs remains a separate remote
action requiring explicit approval.

## Dependency baseline

Use one coherent .NET 10 dependency baseline:

- `ModelContextProtocol` `2.2.0` in host and tests.
- `System.Text.Json` `10.0.12` in contracts and Openness worker.
- `Microsoft.SourceLink.GitHub` `10.0.401` in the host.
- `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`,
  `Microsoft.Extensions.Logging`, and `Microsoft.Extensions.Logging.Abstractions` `10.0.12` where
  directly referenced. These companion upgrades are currently suppressed by the intentional
  Dependabot major-version ignore and must therefore be part of the manual framework migration.
- Keep `Microsoft.Win32.Registry` `5.0.0` and the current xUnit, test SDK, runner, and coverage
  packages unless restore/build evidence identifies an incompatibility. Removing or broadly
  refreshing unrelated packages is outside scope.

Patch versions must be rechecked at implementation start. Patch-only successors in the same major
line may replace the values above if they are stable and all verification is rerun; no new major
line is admitted by this design.

## Runtime and framework boundaries

The cutover is deliberately asymmetric:

| Project | Before | After |
| --- | --- | --- |
| `TiaMcpServer` | `net8.0` | `net10.0` |
| `TiaMcpServer.Tests` | `net8.0` | `net10.0` |
| `TiaMcpServer.FakeWorker` | `net8.0` | `net10.0` |
| `TiaMcpServer.Contracts` | `netstandard2.0` | unchanged |
| `TiaMcpServer.OpennessWorker` | `net48` | unchanged |

Do not multi-target the host. A `net8.0;net10.0` transition would duplicate global-tool package
assets, path-sensitive tests, and release validation without preserving the only compatibility
boundary that matters: the `net48` worker. The migration is an atomic release boundary for the
modern process, with the IPC contract retaining cross-framework compatibility.

Use SDK `10.0.400` as the minimum in `global.json`, set `rollForward` to `latestFeature`, and set
`allowPrerelease` to `false`. This accepts serviced .NET 10 SDK feature bands while preventing an
installed .NET 11 SDK from silently becoming the build toolchain. CI and publish use `10.0.x`.

## Compatibility risks and controls

### System.Text.Json worker graph

`System.Text.Json` 10 supports `netstandard2.0` and .NET Framework 4.6.2 or later, so the contracts
and `net48` worker remain supported. Its .NET Framework dependency graph adds
`System.IO.Pipelines.dll`. Today the package verifier deliberately rejects that file and the
`doctor` companion-file list omits it.

The migration must turn `System.IO.Pipelines.dll` into a required worker companion, update the
package-verifier test from “unexpected file fails” to “missing required file fails,” and continue
to derive/copy the worker payload from a clean `net48` build output. This is a required runtime
asset, not permission to relax the exact package allowlist.

.NET 10 also checks `System.Text.Json` property-name conflicts more strictly. Existing canonical
JSON, cursor, worker payload, and structured-result tests remain the primary regression suite; any
conflict must be fixed in the the specific DTO or serializer contract, not by globally weakening
serializer validation.

### ModelContextProtocol 2.2

The server uses stdio/stream transports, not HTTP, so the v2 HTTP session-default changes are not
directly applicable. Relevant controls are:

- every advertised tool must retain a non-null object `inputSchema`;
- exact read-only/read-write tool names, counts, annotations, and representative schemas remain
  unchanged;
- representative structured calls must continue to emit matching text and `structuredContent`
  from the same canonical JSON document;
- the in-process protocol harness must use the SDK's supported v2 client/server construction APIs;
- no Roots, Sampling, Tasks, OAuth, or HTTP migration work is added without evidence that this
  repository uses those surfaces.

### .NET tool packaging

.NET 10 can create RID-specific tool packages when a tool project declares `RuntimeIdentifiers`.
The global-tool package must remain framework-dependent and platform-agnostic at
`tools/net10.0/any/`; it must not acquire a RID-specific package layout. The standalone release
continues to be the explicit `win-x64`, self-contained, single-file distribution with
`PackAsTool=false`.

Tests and the package verifier must pin both sides of this boundary. Do not add project-level
`RuntimeIdentifiers`. If a later SDK nevertheless changes pack behavior, explicitly set
`CreateRidSpecificToolPackages=false` and `UseAppHost=false` only after a RED package-layout test
demonstrates the need.

## Delivery strategy

Use one migration PR with reviewable, independently verified commits rather than merging the seven
Dependabot PRs. This keeps the runtime, SDK, dependency graph, package allowlist, and documentation
under one release boundary while preserving bisectable steps.

Rejected alternatives:

1. **Merge Dependabot PRs independently.** Their branches are non-mergeable, duplicate one
   another, contain superseded patch versions, and have no PR workflow evidence.
2. **Land dependencies before the framework migration.** Technically possible on `net8.0`, but MCP
   2.2 changes protocol behavior and System.Text.Json changes worker assets. Publishing that
   intermediate state would create a second compatibility boundary for no user benefit.
3. **Dual-target .NET 8 and .NET 10.** It multiplies global-tool and path-sensitive verification
   while leaving the `net48` worker boundary unchanged.

## Verification and evidence boundary

Mandatory offline/static evidence:

1. clean restore with the .NET 10 SDK;
2. serial Release solution build with Siemens reference stubs;
3. full Release tests and the existing 80% line-coverage gate;
4. focused MCP `tools/list` and `tools/call` protocol tests;
5. clean NuGet pack plus strict package verification for `tools/net10.0/any/`;
6. self-contained `win-x64` publish inspection, including the complete net48 worker payload and no
   Siemens DLLs;
7. `git diff --check` and final working-tree review.

No live TIA Portal operation is part of implementation authorization. After the offline gate is
green, a separately authorized read-only V21 acceptance can start the packaged host, list tools,
call `get_project_status`, and make one bounded project-tree read. No write preview or mutation is
needed to qualify this runtime migration.

## Documentation boundary

Normal users need clear .NET 10 runtime requirements for the framework-dependent global tool; the
self-contained `win-x64` archive includes its runtime. They do not install
`ModelContextProtocol`, modify PLC projects, or change ordinary MCP client configuration solely
because an internal package was upgraded.

Custom integrations that compile against the C# SDK need a separate note to review MCP 2.2
breaking changes and retest protocol negotiation, tool schemas, and structured results. Keep this
distinct from the existing v3 `browse_project_tree` API migration guidance.
