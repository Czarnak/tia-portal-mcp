# Configurable worker timeout (PR 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the worker request timeout be raised at startup from 5 to at most 60 minutes, through
`--worker-timeout-minutes` or `TIA_MCP_WORKER_TIMEOUT_MINUTES`, reported at startup and by `doctor`.

**Architecture:** A new `WorkerTimeoutParser` in `TiaMcpServer/Cli` mirrors `PayloadBudgetParser` and shares
its command-line scan through one extracted helper. `Program` passes the parsed value into the existing
`OpennessWorkerClient(requestTimeout:)` parameter; `doctor` parses the same option and renders it. No worker,
contract or tool change.

**Tech Stack:** .NET 10 host, xunit, the existing process-launch startup tests.

**Spec:** [docs/superpowers/specs/2026-10-15-vci-tools-design.md](../specs/2026-10-15-vci-tools-design.md), §2.

## Global Constraints

- Option `--worker-timeout-minutes <n>` (also `--worker-timeout-minutes=<n>`); environment variable `TIA_MCP_WORKER_TIMEOUT_MINUTES`.
- Precedence: command line > environment variable > default. Parsed once at startup.
- Raise-only: default and floor **5**, maximum **60**, whole minutes only (`NumberStyles.None`, invariant culture).
- One value for every worker request, `hello` included; no per-method override.
- Out-of-range or malformed values fail startup with exit code 1 and `Error: <message>` on stderr, as the budget options do.
- Error text: `Invalid <source> value '<text>'. Expected a whole number of minutes from 5 to 60.`; `<flag> requires a value.`; `<flag> may be specified only once.`
- Startup stderr line: `TIA MCP worker timeout: <n> minutes`.
- `doctor` text line `Worker timeout: <n> minutes (<source>)`; JSON members `workerTimeoutMinutes` (number) and `workerTimeoutSource` (`"default"`, `"option"`, `"environment"`).
- Commits: short one-line conventional messages, no body or trailers; commit after each task step that changes code.
- Build/test with PowerShell, not Bash (`/p:` flags): `dotnet build TiaMcpServer.slnx -m:1`, `dotnet test TiaMcpServer.Tests`.

## Review Focus

1. A valid command-line value with a malformed environment value: the command line wins and the environment is ignored, not reported as an error (test in Task 1).
2. Number forms a user might type — `+10`, `10.5`, ` 10 `, `1e1`, `010`: only plain digits parse; surrounding whitespace is trimmed; `010` is 10 (test in Task 1).
3. `doctor` run with a malformed `TIA_MCP_WORKER_TIMEOUT_MINUTES`: it must fail with the same message the server would print at startup, not silently fall back the way the access-mode environment variable does (test in Task 3).
4. The flag passed through to the generic host builder: `--worker-timeout-minutes 10` must not break host configuration; the server starts and shuts down cleanly with exit code 0 (test in Task 2).
5. A raised timeout delays detection of a worker that hangs during `hello`; the docs must say so, because nothing else will (Task 4).

---

### Task 0: Branch

- [x] Work on `feat/configurable-worker-timeout` (renamed from `feature/vci` on 2026-10-15, locally and on `origin`), so the VCI spike report, the spec and this plan ship with PR 1. PR 2 is planned after PR 1 merges.

### Task 1: `WorkerTimeoutParser` and the shared option reader

**Files:**
- Create: `TiaMcpServer/Cli/StartupOptionReader.cs`
- Create: `TiaMcpServer/Cli/WorkerTimeoutParser.cs`
- Modify: `TiaMcpServer/Cli/PayloadBudgetParser.cs` (its private `Resolve` scan moves to the reader)
- Test: `TiaMcpServer.Tests/Cli/WorkerTimeoutParserTests.cs`; existing `PayloadBudgetParserTests.cs` must stay green unchanged

The test project already globs `TiaMcpServer/Cli/*.cs`; no csproj edit.

**Interfaces:**
- Produces:
  - `internal static class StartupOptionReader` with `static (string? Value, string? Error) ReadSingle(string[] args, string flag)`. It scans `flag <v>` and `flag=<v>` case-insensitively, returns `(null, null)` when absent, and returns the "requires a value" and "only once" errors with today's exact `PayloadBudgetParser` wording.
  - `public enum WorkerTimeoutSource { Default, Option, Environment }`
  - `public sealed record WorkerTimeoutParseResult(bool IsValid, int Minutes, WorkerTimeoutSource Source, string? Error)` with `TimeSpan Timeout => TimeSpan.FromMinutes(Minutes)` and a `static string SourceName(WorkerTimeoutSource)` returning `default` / `option` / `environment`.
  - `public static class WorkerTimeoutParser` with constants `Flag = "--worker-timeout-minutes"`, `EnvVar = "TIA_MCP_WORKER_TIMEOUT_MINUTES"`, `DefaultMinutes = 5`, `MaxMinutes = 60`, and `static WorkerTimeoutParseResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null)`.

- [ ] **Step 1: Write failing tests** in `WorkerTimeoutParserTests`:
  - `ReturnsDefaultWhenNeitherSet` → `IsValid`, `Minutes == 5`, `Source == Default`.
  - `ReadsCommandLine` (`["--worker-timeout-minutes", "10"]` and `["--worker-timeout-minutes=10"]`) → 10, `Option`.
  - `ReadsEnvironment` (env `"30"`) → 30, `Environment`.
  - `CommandLineWinsOverMalformedEnvironment` (args `10`, env `"abc"`) → valid, 10, `Option`.
  - `AcceptsBounds` (`5`, `60`, `" 10 "`, `"010"`) → valid.
  - `RejectsOutOfRangeOrMalformed` theory over `4`, `61`, `0`, `-5`, `+10`, `10.5`, `1e1`, `abc` → invalid; the error equals `Invalid --worker-timeout-minutes value '<text>'. Expected a whole number of minutes from 5 to 60.`
  - `RejectsMalformedEnvironment` → the error names `TIA_MCP_WORKER_TIMEOUT_MINUTES`.
  - `RejectsMissingValue` (flag last; flag followed by `--read-only`; `--worker-timeout-minutes=`) → `--worker-timeout-minutes requires a value.`
  - `RejectsRepeatedFlag` → `--worker-timeout-minutes may be specified only once.`
  - `IgnoresOtherOptions` (`--max-item-chars 100000 --access-mode full`) → default.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerTimeoutParserTests`. Expected: FAIL, compile error (type missing).
- [ ] **Step 3: Extract `StartupOptionReader.ReadSingle`** from `PayloadBudgetParser.Resolve`, then make `Resolve` call it. Run `--filter FullyQualifiedName~PayloadBudgetParserTests`. Expected: PASS, unchanged.
- [ ] **Step 4: Implement `WorkerTimeoutParser.Parse`** on top of `ReadSingle`, with the same command-line, then environment, then default order and the validation shape `PayloadBudgetParser.Validate` uses.
- [ ] **Step 5: Run** both test classes. Expected: PASS.
- [ ] **Step 6: Commit** `feat: parse configurable worker timeout`

### Task 2: Server startup wiring

**Files:**
- Modify: `TiaMcpServer/Program.cs` (after the budget parse, around lines 62-72, and the `OpennessWorkerClient` registration at line 86)
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs:57`, so the default derives from `WorkerTimeoutParser.DefaultMinutes` (one source of the value 5)
- Create: `TiaMcpServer.Tests/Cli/StartupProcess.cs` (the `RunStartupAsync` helper and its `StartupResult`, moved out of `RemovedOptionTests`)
- Modify: `TiaMcpServer.Tests/Cli/RemovedOptionTests.cs` (to use the helper)
- Test: `TiaMcpServer.Tests/Cli/WorkerTimeoutStartupTests.cs`

**Interfaces:**
- Consumes: `WorkerTimeoutParser.Parse`, `WorkerTimeoutParseResult.Timeout` (Task 1).
- Produces: `internal static class StartupProcess` with `static Task<StartupResult> RunAsync(IReadOnlyDictionary<string, string>? environment, params string[] args)`. It also removes `TIA_MCP_WORKER_TIMEOUT_MINUTES` from the child environment unless the caller sets it, in addition to the variables `RemovedOptionTests` removes today.

- [ ] **Step 1: Move the helper.** Move `RunStartupAsync` and `StartupResult` to `StartupProcess` unchanged apart from the environment parameter. Run `--filter FullyQualifiedName~RemovedOptionTests`. Expected: PASS. Commit `test: share startup process helper`.
- [ ] **Step 2: Write failing tests** in `WorkerTimeoutStartupTests` (process-based, so they need a current solution build):
  - `StartupReportsDefaultTimeout` → stderr contains `TIA MCP worker timeout: 5 minutes`.
  - `StartupReportsRaisedTimeout` (`--worker-timeout-minutes 10` and `--worker-timeout-minutes=10`) → exit code 0 after stdin closes (the healthy-start signal `RemovedOptionTests` uses) and stderr contains `TIA MCP worker timeout: 10 minutes` (Review Focus 4).
  - `StartupUsesEnvironmentTimeout` (env `30`) → `30 minutes`.
  - `StartupRejectsInvalidTimeout` (`--worker-timeout-minutes 61`) → exit code 1, stderr contains `Error: Invalid --worker-timeout-minutes value '61'. Expected a whole number of minutes from 5 to 60.`
- [ ] **Step 3: Build and run.** `dotnet build TiaMcpServer.slnx -m:1`, then `--filter FullyQualifiedName~WorkerTimeoutStartupTests`. Expected: FAIL (no line, no rejection).
- [ ] **Step 4: Wire it in `Program.Main`.** Parse after the budget options with the same error exit. Print the startup line after the budget line. Pass `requestTimeout: workerTimeout.Timeout` to `OpennessWorkerClient`. Switch the client's `DefaultRequestTimeout` to `TimeSpan.FromMinutes(WorkerTimeoutParser.DefaultMinutes)`. `HostArgumentFilter` stays unchanged: the budget flags already pass through to the host builder harmlessly.
- [ ] **Step 5: Build and run** the startup tests, then the whole suite with `dotnet test TiaMcpServer.Tests`. Expected: PASS.
- [ ] **Step 6: Commit** `feat: apply configurable worker timeout at startup`

### Task 3: `doctor` reports the timeout

**Files:**
- Modify: `TiaMcpServer/Cli/DoctorCliParser.cs` (accept and skip the flag and its value; resolve the timeout)
- Modify: `TiaMcpServer/Cli/DoctorCommand.cs` (pass the resolution to both renderers)
- Modify: `TiaMcpServer/Diagnostics/DoctorTextRenderer.cs`, `TiaMcpServer/Diagnostics/DoctorJsonRenderer.cs`
- Test: `TiaMcpServer.Tests/Diagnostics/DoctorCliParserTests.cs`, `DoctorTextRendererTests.cs`, `DoctorJsonRendererTests.cs`, `DoctorCommandTests.cs`

**Interfaces:**
- Consumes: `WorkerTimeoutParser.Parse`, `WorkerTimeoutParseResult`, `WorkerTimeoutParseResult.SourceName` (Task 1).
- Produces:
  - `DoctorCliOptions` gains a non-positional `WorkerTimeoutParseResult? WorkerTimeout { get; init; }`, so the existing positional constructor calls stay untouched.
  - `DoctorTextRenderer.Render(..., McpAccessMode? accessMode = null, WorkerTimeoutParseResult? workerTimeout = null)`
  - `DoctorJsonRenderer.Render(..., McpAccessMode? accessMode = null, WorkerTimeoutParseResult? workerTimeout = null)`

- [ ] **Step 1: Write failing tests:**
  - Parser:
    - `--worker-timeout-minutes 10` and the `=` form are no longer "Unknown doctor argument". The result is `WorkerTimeout.Minutes == 10`, `Source == Option`, and `--project` still parses after it.
    - A malformed value (on the command line, or in the environment through an injectable environment function or the same environment hook the parser tests use) gives `Valid == false` with the Task 1 error text (Review Focus 3).
  - Text renderer: with `Minutes 10, Option`, output contains `Worker timeout: 10 minutes (option)` directly under the access-mode block; with `null`, no such line.
  - JSON renderer: `workerTimeoutMinutes == 10` and `workerTimeoutSource == "option"` after `accessMode`; both absent for `null`.
  - Command: `doctor --worker-timeout-minutes 61` exits 2 with `error: Invalid --worker-timeout-minutes value '61'...`.
- [ ] **Step 2: Run** the four test classes. Expected: FAIL.
- [ ] **Step 3: Implement.** `DoctorCliParser` calls `WorkerTimeoutParser.Parse(args)` once, maps an invalid result to its `ParseError`, and skips the flag and its value in the argument loop. If the parser needs the environment injectable for the test, add an optional `Func<string, string?>` parameter to `Parse`, defaulting to `Environment.GetEnvironmentVariable`. `DoctorCommand` passes `options.WorkerTimeout` to the renderers. Add the flag to `UsageText`.
- [ ] **Step 4: Run** the four classes, then the whole suite. Expected: PASS.
- [ ] **Step 5: Commit** `feat: report worker timeout in doctor`

### Task 4: Live smoke check and documentation

Docs come last, after the live check (project rule).

- [ ] **Step 1: Live smoke check** on a machine with TIA Portal V21, using the locally built host, not the installed plugin (the installed binary does not reflect local builds):
  - `doctor --worker-timeout-minutes 15` shows `Worker timeout: 15 minutes (option)`.
  - The server started with `--worker-timeout-minutes 15` prints the startup line; `get_project_status` and one `compile_check` on a disposable standalone project succeed.
  - Record the outcome in the PR description. No acceptance report file; no project names anywhere.
- [ ] **Step 2: Docs:**
  - `docs/guides/mcp-client-configuration.md`: a "Worker timeout" section after "Response size limits", with a table row in the same shape (`--worker-timeout-minutes <n>`, `TIA_MCP_WORKER_TIMEOUT_MINUTES`, 5), the 5–60 rule, the client-side limit (60 s default in the MCP TypeScript SDK unless the client raises it; `MCP_TOOL_TIMEOUT` in Claude Code), the note that an abandoned call keeps running and its lifecycle/write audit keeps the outcome, and the note that a raised value also delays detection of a hung worker start (Review Focus 5).
  - `docs/guides/troubleshooting.md`: a `worker_timeout` entry pointing to that section.
  - `docs/ARCHITECTURE.md`: the startup-options paragraph near line 67.
  - `AGENTS.md`: one sentence in "Two-process architecture" next to "The worker restarts automatically after crash or timeout".
- [ ] **Step 3: Verify** `dotnet build TiaMcpServer.slnx -m:1` and `dotnet test TiaMcpServer.Tests` pass. Every touched doc uses relative links; `README.md` is untouched.
- [ ] **Step 4: Commit** `docs: configurable worker timeout`

**Not in this PR:** an installer option (`tia-mcp install` writes no budget options today either), MCP progress notifications, per-method timeouts.
