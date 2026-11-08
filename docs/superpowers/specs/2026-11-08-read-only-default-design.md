# Read-only as the default access mode (PR 1 of 2)

**Date:** 2026-11-08
**Size:** small change (short design)
**Branch:** `claude/read-only-default-mode`

## Intent

An MCP client or agent that starts tia-mcp without saying which access mode it wants currently gets
`read-write`. Starting with fewer permissions is the safer default: whoever wants edits, compilation
or lifecycle must ask for them explicitly. The installer already writes `read-only` unless told
otherwise; this change makes the server itself, its worker and `doctor` agree with it.

This is PR 1 of a two-PR hardening effort. PR 2 adds an admin-owned access-mode ceiling in
`HKLM`, a user hardening guide and the README trim. PR 2 has its own spec.

## Goals

- When no mode is given (no `--access-mode`/`--read-only`/`--read-write`, no
  `TIA_MCP_ACCESS_MODE`), the host, the worker and `doctor` all resolve to `read-only`.
- Startup output and `doctor` say where the access mode came from (command line, environment
  variable, or default), and the default case tells the user how to enable edits.
- No product code path falls back to `read-write`.

## Non-goals

- The access-mode ceiling, the hardening guide and the README trim (all PR 2).
- Configuring or restricting agents/MCP clients (the user's responsibility).
- Any change to what each mode permits, to the tool surface per mode, or to precedence
  (CLI > environment > default stays as it is).
- Changing `tia-mcp install`: it already always writes an explicit `--access-mode`, default `read-only`.

## Anti-goals

- A missing mode must never silently grant write access anywhere (host, worker, worker client).
- Do not require an explicit mode: an MCP config without one must still start, read-only.

## Constraints

- Breaking CLI behavior: a manual configuration with no mode loses write tools. Installed
  configurations are unaffected because the installer always writes `--access-mode`. The PR is
  marked **Breaking change** with migration notes in its Summary.
- The host speaks MCP over stdout, so every new message goes to stderr, like the existing
  `TIA MCP access mode:` line.
- `doctor` shares `AccessModeParser`, so the default changes for it automatically.
- The worker can be started directly (bypassing the host); its own default must also be read-only.

## Approaches considered

1. **Change the default only.** Flip the fallback in the three places, update docs and tests.
   Cheapest, but a user who suddenly sees nine tools gets no hint why.
2. **Change the default and report the source (chosen).** As 1, plus startup and `doctor` report
   whether the mode came from the command line, the environment variable or the default, with a
   fix hint for the default. Mirrors how `doctor` already reports the worker timeout source
   (`WorkerTimeoutSource`, `workerTimeoutSource`).
3. **Require an explicit mode.** Rejected: contradicts the requirement that read-only is the
   default, and breaks every configuration without a mode.

The user picked approach 2.

## Design

### Default resolution

| Place | File | Today | After |
| --- | --- | --- | --- |
| Host parser (also `doctor`) | `TiaMcpServer/Cli/AccessModeParser.cs` | `ReadWrite` | `ReadOnly` |
| Worker | `TiaMcpServer.OpennessWorker/Worker/WorkerOperationAuthorization.cs` (`ParseAccessMode`) | `ReadWrite` | `ReadOnly` |
| Worker client (no policy passed) | `TiaMcpServer/Worker/OpennessWorkerClient.cs` | `ReadWrite` | `ReadOnly` |

The worker's existing fail-closed rules for malformed or conflicting arguments stay as they are.
Its doc comment ("Missing configuration preserves the historical read-write default") is updated.
Production always passes an explicit policy to `OpennessWorkerClient` and an explicit
`--access-mode` to the worker, so for those two the change only affects direct launches and tests.

### Mode source

- Add `AccessModeSource { Default, Option, Environment }` next to `AccessModeParser`, with a
  `SourceName` helper returning `"default"`, `"option"`, `"environment"` (same names as
  `WorkerTimeoutParseResult.SourceName`).
- `AccessModeParseResult` gains a `Source` member. `--read-only`, `--read-write`,
  `--access-mode <m>` and `--access-mode=<m>` are all `Option`.

### Startup output (stderr)

```text
TIA MCP access mode: READ-ONLY (default)
No access mode was given. Use --access-mode read-write or --access-mode full (or TIA_MCP_ACCESS_MODE) to enable edits, compilation and lifecycle.
Project opening, compilation, writes and lifecycle operations are disabled.
```

- The first line always carries the source in parentheses: `(default)`, `(option)` or `(environment)`.
- The hint line appears only when the source is `default`.
- The third line is the existing read-only line, unchanged.
- Existing assertions that check `Contains("TIA MCP access mode: READ-WRITE")` still pass.

### `doctor`

- Text: `Access mode: READ-ONLY (default)`, same source suffix as startup.
- JSON: a new string member `accessModeSource` next to `accessMode`, with the same three values,
  written whenever `accessMode` is written (mirrors `workerTimeoutSource`).

### Documentation

Update every statement of the old default. Found today:

- `README.md` line 19 ("21 in `read-write` (the startup default)").
- `AGENTS.md` line 3 ("21 in read-write (startup default)").
- `docs/guides/installation.md` lines 193 ("server startup default") and 223 ("default (read-write)").
- `docs/ARCHITECTURE.md` line 61 ("`read-write` by default") and line 266 ("Missing worker
  configuration preserves the historical read-write default").

Also document the new source suffix and `accessModeSource` where `doctor` output is described.
The README gets factual fixes only; trimming it is PR 2.

### Decision record (builder's call, proposed)

Add ADR-0017, "Read-only is the default access mode". It meets the admission test in
`docs/adr/README.md`: there was a considered alternative (requiring an explicit mode, or keeping
read-write), and a future maintainer could plausibly flip it back by mistake. PR 2 records the
ceiling in its own ADR.

### Tests

- Flip the tests that pin the old default to pin read-only, including
  `ReadOnlyModeTests` (`OpennessWorkerClient_NoPolicy_DefaultsToReadWrite`,
  `WorkerOperationAuthorization_DefaultsToReadWrite`, `DoctorCliParser_NoEnvVar_DefaultsToReadWrite`
  and the empty-argument parser cases) and the `RemovedOptionTests` startup assertions.
- New tests: the source is reported correctly for each of the four CLI forms, the environment
  variable and the default; the hint line appears only for the default; `doctor` text and JSON
  carry the source.
- Tests that build `OpennessWorkerClient` without a policy and need writes pass an explicit
  `read-write` policy. The `FakeWorkerClients` test helper keeps its own read-write default
  because it is test support, not product code.

## Calls made by Claude (please check)

- Source names reuse the worker-timeout vocabulary (`option`/`environment`/`default`) instead of
  new words, so `doctor` reads consistently.
- The worker prints nothing new on a direct launch; it just resolves to read-only.
- The `FakeWorkerClients` test helper keeps read-write as its default.
- ADR-0017 in this PR rather than one combined ADR in PR 2, because PR 1 merges on its own.
- No version bump in this PR; release versioning stays with the maintainer.

## Left to the builder

- The exact wording of the hint line, provided it names `--access-mode` and `TIA_MCP_ACCESS_MODE`.
- Where `AccessModeSource` lives (same file as the parser, or its own file).
- Finding any further docs or tests that pin the old default beyond the lists above.

## Implementation checklist

This change is small enough that this ordered checklist replaces a separate implementation plan.
Product code is about 40 lines; most of the work is tests and docs.

1. **Flip the three defaults** (host parser, worker, worker client) and run the tests to see what breaks.
2. **Repair the broken tests.** Tests that pin the old default now pin read-only. Of the 16 tests that build
   `OpennessWorkerClient` without a policy, the ones that need writes get an explicit `read-write` policy.
3. **Add the mode source and startup output:** `AccessModeSource`, `AccessModeParseResult.Source`, the
   `(source)` suffix and the default-only hint line in `Program.cs`, with tests for all four CLI forms,
   the environment variable and the default.
4. **Add the `doctor` output:** pass the source through `DoctorCommand` into `DoctorTextRenderer` and
   `DoctorJsonRenderer` (`accessModeSource`), with tests.
5. **Update the docs:** fix the six statements of the old default, document the source suffix and
   `accessModeSource`, add ADR-0017 to `docs/adr/` and its index, then run `verify-docs.py`.
6. **Prune before merge:** delete this spec in a final `docs: prune working files` commit.

## Verification

- `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`
- `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
- `python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`
- Implementation and verification run on the maintainer's machine (this cloud session has no
  .NET SDK).
- Live TIA Portal check: not needed for the default itself. The worker change is one fallback
  value covered by unit tests, so the PR is "stub build only; the maintainer verifies".
