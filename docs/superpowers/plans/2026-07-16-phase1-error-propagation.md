# Phase 1 — Error Propagation Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `"Error:"` string-prefix failure convention with a structured `WorkerCallResult` threaded through client → batch → safety → tools, surface worker stderr as agent-visible warnings, make hardware-config fallbacks distinguishable from real values, add actionable `Win32Exception` handling, reject misspelled batch-item JSON properties, and narrow the bare `catch (Exception)` blocks in `EquipmentCatalogSearcher`.

**Architecture:** The host (`TiaMcpServer`, net8.0) spawns a net48 worker per request over stdin/stdout JSON. Today every layer re-derives failure from a `result.StartsWith("Error:")` check on plain strings. We introduce one record — `WorkerCallResult(Success, Payload, Error, Warnings)` — produced by `OpennessWorkerClient` and consumed structurally everywhere. Agent-facing text stays byte-identical (failures still render as `"Error: …"` via `ToText()`), but classification never depends on text again. A new fake-worker console project gives the first integration coverage of the IPC layer.

**Tech Stack:** C# — net8.0 host + tests (xunit), netstandard2.0 contracts, net48 worker (Siemens Openness V21). System.Text.Json everywhere.

**Covers IMPROVEMENT_PLAN.md items:** 1.1, 1.3, 1.4, 1.5, 1.6, 1.7 (1.2 and 2.2 already done).

## Global Constraints

- Target frameworks are fixed: host/tests net8.0, `TiaMcpServer.Contracts` netstandard2.0, `TiaMcpServer.OpennessWorker` net48. No records or `Array.Empty`-style API changes in Contracts beyond what already compiles there (nullable annotations ARE enabled in Contracts — `string?` is fine).
- The test project compiles host sources via `<Compile Include>` links (NOT a ProjectReference) — every NEW host file consumed by linked files must also be linked into `TiaMcpServer.Tests.csproj`.
- Tests must never require Siemens DLLs or a running TIA Portal. Worker-project changes are verified by `dotnet build TiaMcpServer.sln` only.
- Agent-facing failure text keeps the `"Error: "` prefix rendering (produced by `WorkerCallResult.ToText()`), so existing docs/behavior stay stable; only the *classification* becomes structural.
- Host must never write to stdout except MCP protocol — all logging goes to stderr (`AddConsole` already routes via `LogToStandardErrorThreshold`).
- Existing suite is 146 green tests; every task ends with the full suite green.
- Commit format: `<type>: <description>` (feat, fix, refactor, test, chore, docs). Work happens on branch `26-07-16-improvement-phase1`.
- Build: `dotnet build TiaMcpServer.sln`. Test: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- TDD note: pure-logic changes (Tasks 1, 4, 5, 6, 7) are strictly test-first. The client flip (Task 2) is a behavior-preserving signature refactor guarded by the existing 146 tests; its NEW behavior (warnings, Win32) gets dedicated integration tests in Task 3 via the fake worker — including the misclassification regression test that fails against pre-1.1 code.

## Scoping decisions (read before executing)

1. **1.3 messages-array routing** is applied to `HardwareConfigReader` only (done as part of Task 7 / item 1.4, which restructures that file anyway). `ProjectTreeWalker`, `TagTableReader`, `EquipmentCatalogSearcher` etc. keep writing to stderr — after Task 2/3 those lines surface to the agent as `warnings` on every tool result, which achieves 1.3's goal without breaking the `browse_project_tree` payload shape (a bare JSON array; wrapping it is deferred to Phase 2.3 payload-bounds work).
2. **Stderr warnings are capped at 20 lines** (+ a `(+N more…)` marker) so a degraded big-project read cannot flood a small model's context.
3. **1.7 fully subsumes Phase 3 item 3.4** (merge the searcher's private reflection helpers into `OpennessReflection`) — doing 1.7 without the merge would mean editing ~90 lines that get deleted weeks later.
4. `WorkerCallResult` lives in the host (`TiaMcpServer.Worker`) — the worker process protocol (`WorkerResponse`) is unchanged; stderr is a host-side capture.

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `TiaMcpServer/Worker/WorkerCallResult.cs` | Create | The structured result record + `ToText()` rendering |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Modify | Return `WorkerCallResult`, centralize exception mapping (incl. `Win32Exception`), capture stderr → warnings, `ILogger`, injectable worker path |
| `TiaMcpServer/Batch/BatchWorkerInvoker.cs` | Modify | Return-type flip only |
| `TiaMcpServer/Batch/BatchExecutionEngine.cs` | Modify | Structural success check; delete `IsFailure` |
| `TiaMcpServer/Batch/BatchOperationResult.cs` | Modify | Add `Warnings` |
| `TiaMcpServer/Batch/BatchResultFormatter.cs` | Modify | Emit per-item `warnings` |
| `TiaMcpServer/Batch/BatchTools.cs` | Modify | Structural checks in state-read + engine wiring |
| `TiaMcpServer/Safety/WriteSafetyTooling.cs` | Modify | Accept `WorkerCallResult` in `ValidateForApplyAsync` / `CreatePreview` / `BuildApplyResult` |
| `TiaMcpServer/Tools/ProjectLifecycleTools.cs` | Modify | Drop all `StartsWith("Error:")` checks |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Modify | `[JsonUnmappedMemberHandling(Disallow)]` |
| `TiaMcpServer/Program.cs` | Modify | Explicit client factory registration with logger |
| `TiaMcpServer.FakeWorker/` (new project) | Create | Scriptable fake worker exe for IPC integration tests |
| `TiaMcpServer.Contracts/HardwareConfigInfo.cs`, `DeviceInfo.cs`, `DeviceItemInfo.cs` | Modify | `Messages` list + nullable fallbacks |
| `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs` | Modify | Route degradation into `Messages`, nullable reads |
| `TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs` | Modify | Narrow catches, delete private reflection helpers |
| `TiaMcpServer.OpennessWorker/Openness/OpennessReflection.cs` | Modify | Add description-taking broad-but-bounded `ReadProperty` overload |
| `TiaMcpServer.OpennessWorker/Program.cs` | Modify | Catch-all includes exception type name |
| `TiaMcpServer.Tests/*` | Create/Modify | New: `WorkerCallResultTests`, `OpennessWorkerClientIntegrationTests`, `BatchOperationRequestJsonTests`; updates to engine/formatter/safety/lifecycle/hardware tests |

---

### Task 1: `WorkerCallResult` record

**Files:**

- Create: `TiaMcpServer/Worker/WorkerCallResult.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link new file)
- Test: `TiaMcpServer.Tests/WorkerCallResultTests.cs`

**Interfaces:**

- Consumes: nothing.
- Produces: `WorkerCallResult(bool Success, string Payload, string? Error, IReadOnlyList<string> Warnings)` with static `Ok(string payload, IReadOnlyList<string>? warnings = null)`, `Fail(string error, IReadOnlyList<string>? warnings = null)`, and instance `string ToText()`. Every later task uses exactly these names.

- [ ] **Step 1: Create the branch**

```powershell
git checkout -b 26-07-16-improvement-phase1
```

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/WorkerCallResultTests.cs`:

```csharp
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class WorkerCallResultTests
{
    [Fact]
    public void Ok_CarriesPayloadWithoutError()
    {
        var result = WorkerCallResult.Ok("{\"a\":1}");

        Assert.True(result.Success);
        Assert.Equal("{\"a\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fail_CarriesErrorAndEmptyPayload()
    {
        var result = WorkerCallResult.Fail("boom");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Payload);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void ToText_RendersPayloadOnSuccess()
    {
        Assert.Equal("data", WorkerCallResult.Ok("data").ToText());
    }

    [Fact]
    public void ToText_RendersErrorPrefixOnFailure()
    {
        Assert.Equal("Error: boom", WorkerCallResult.Fail("boom").ToText());
    }

    [Fact]
    public void Ok_PayloadStartingWithErrorPrefixStaysSuccessful()
    {
        // The whole point of item 1.1: payload text must never drive classification.
        var result = WorkerCallResult.Ok("Error: literal block comment content, not a failure");

        Assert.True(result.Success);
    }

    [Fact]
    public void Warnings_AreAttachedToBothShapes()
    {
        Assert.Single(WorkerCallResult.Ok("x", new[] { "w1" }).Warnings);
        Assert.Single(WorkerCallResult.Fail("e", new[] { "w1" }).Warnings);
    }
}
```

- [ ] **Step 3: Link the (not yet existing) source file and run to verify failure**

Add to the second `<ItemGroup>` of `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, directly after the `OpennessWorkerClient.cs` line:

```xml
    <Compile Include="..\TiaMcpServer\Worker\WorkerCallResult.cs" Link="Host\WorkerCallResult.cs" />
```

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: FAIL — compile error, `WorkerCallResult` does not exist.

- [ ] **Step 4: Write the implementation**

Create `TiaMcpServer/Worker/WorkerCallResult.cs`:

```csharp
namespace TiaMcpServer.Worker;

/// <summary>
/// Structured outcome of one TIA Openness worker invocation. Replaces the "Error:"
/// string-prefix convention: success/failure is carried structurally and payload text
/// never drives classification. <see cref="Warnings"/> carries non-fatal degradation
/// notes captured from the worker's stderr.
/// </summary>
public sealed record WorkerCallResult(
    bool Success,
    string Payload,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public static WorkerCallResult Ok(string payload, IReadOnlyList<string>? warnings = null)
        => new(true, payload, null, warnings ?? Array.Empty<string>());

    public static WorkerCallResult Fail(string error, IReadOnlyList<string>? warnings = null)
        => new(false, string.Empty, error, warnings ?? Array.Empty<string>());

    /// <summary>Agent-facing text for boundaries where an MCP tool returns a plain string.</summary>
    public string ToText()
        => Success ? Payload : $"Error: {Error}";
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 152 tests (146 + 6 new).

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Worker/WorkerCallResult.cs TiaMcpServer.Tests/WorkerCallResultTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add WorkerCallResult structured worker outcome record"
```

---

### Task 2: Flip `OpennessWorkerClient` to `WorkerCallResult` (items 1.1 client layer, 1.3 host capture, 1.5 host)

This is one atomic compile unit: the client's return types, `BatchWorkerInvoker`'s declared types, and `.ToText()` bridges at every downstream string boundary must change together. Behavior is preserved exactly (rendered text identical), so the existing suite is the safety net. New behavior (warnings, Win32) is asserted in Task 3.

**Files:**

- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs` (whole file)
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs` (return types + fallback arms)
- Modify: `TiaMcpServer/Batch/BatchTools.cs` (bridges)
- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs` (bridges)
- Modify: `TiaMcpServer/Program.cs` (DI factory)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (logging package)

**Interfaces:**

- Consumes: `WorkerCallResult` from Task 1.
- Produces: every public client method now returns `Task<WorkerCallResult>`; new constructor `OpennessWorkerClient(ProjectSessionBinding projectSessionBinding, ILogger<OpennessWorkerClient>? logger = null, string? workerExecutablePath = null)`. Tasks 3–5 rely on both.

- [ ] **Step 1: Rewrite the client's plumbing core**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`:

1. Add usings: `using System.ComponentModel;` and `using Microsoft.Extensions.Logging;`.
2. Replace the constructor/fields section:

```csharp
    private readonly ProjectSessionBinding _projectSessionBinding;
    private readonly ILogger<OpennessWorkerClient>? _logger;
    private readonly string? _workerExecutablePathOverride;

    public OpennessWorkerClient(
        ProjectSessionBinding projectSessionBinding,
        ILogger<OpennessWorkerClient>? logger = null,
        string? workerExecutablePath = null)
    {
        _projectSessionBinding = projectSessionBinding;
        _logger = logger;
        _workerExecutablePathOverride = workerExecutablePath;
    }
```

1. Replace `SendBoundProjectRequestAsync` (delete its try/catch — exception mapping is centralized below):

```csharp
    private async Task<WorkerCallResult> SendBoundProjectRequestAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload)
    {
        if (!_projectSessionBinding.TryResolve(projectPath, out var effectiveProjectPath, out var bindingError))
        {
            return WorkerCallResult.Fail(bindingError!);
        }

        var request = new WorkerRequest
        {
            Method = method,
            ProjectPath = effectiveProjectPath
        };
        configure(request);

        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        return result.Success && string.IsNullOrEmpty(result.Payload)
            ? result with { Payload = emptyPayload }
            : result;
    }
```

1. Add the single centralized exception-mapping entry point (this is where item 1.5's `Win32Exception` handling lives — it is the launch failure raised by `Process.Start` when .NET FX 4.8 is missing or the worker folder is corrupt):

```csharp
    private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)
    {
        try
        {
            var (response, stderrWarnings) = await SendAsync(request).ConfigureAwait(false);
            foreach (var warning in stderrWarnings)
            {
                _logger?.LogWarning("TIA Openness worker stderr: {Line}", warning);
            }

            return response.Success
                ? WorkerCallResult.Ok(response.Payload ?? string.Empty, stderrWarnings)
                : WorkerCallResult.Fail(
                    response.Error ?? "The TIA Openness worker failed without an error message.",
                    stderrWarnings);
        }
        catch (Win32Exception ex)
        {
            return WorkerCallResult.Fail(
                $"Failed to launch the TIA Openness worker process ({ex.Message}). "
                + "Verify that .NET Framework 4.8 is installed and that the 'openness-worker' folder "
                + "beside the MCP server executable is complete; rebuild or reinstall if files are missing.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return WorkerCallResult.Fail(ex.Message);
        }
    }
```

1. Make `SendAsync`/`SendUnguardedAsync` instance methods returning stderr alongside the response. `WorkerGate` stays `static`. Replace both methods:

```csharp
    private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendAsync(WorkerRequest request)
    {
        await WorkerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SendUnguardedAsync(request).ConfigureAwait(false);
        }
        finally
        {
            WorkerGate.Release();
        }
    }

    private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendUnguardedAsync(WorkerRequest request)
    {
        var workerPath = _workerExecutablePathOverride ?? LocateWorkerExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            WorkingDirectory = Path.GetDirectoryName(workerPath) ?? AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the TIA Openness worker process.");

        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(WorkerTimeout);
        var responseLineTask = process.StandardOutput.ReadLineAsync();
        var completed = await Task.WhenAny(responseLineTask, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token))
            .ConfigureAwait(false);

        if (completed != responseLineTask)
        {
            TryKill(process);
            throw new TimeoutException($"TIA Openness worker did not respond within {WorkerTimeout.TotalMinutes:N0} minutes.");
        }

        var responseLine = await responseLineTask.ConfigureAwait(false);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var stderrLines = SplitStderrLines(stderr);

        if (string.IsNullOrWhiteSpace(responseLine))
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "No response was written." : stderr.Trim();
            throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
        }

        var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
        return (response ?? throw new InvalidOperationException("TIA Openness worker returned an empty response."), stderrLines);
    }

    // A degraded read of a large project can emit hundreds of "Skipping X" lines; cap what
    // reaches the agent so warnings cannot flood a small model's context.
    private const int MaxStderrWarningLines = 20;

    private static IReadOnlyList<string> SplitStderrLines(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return Array.Empty<string>();
        }

        var lines = stderr.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > MaxStderrWarningLines)
        {
            var dropped = lines.Count - MaxStderrWarningLines;
            lines = lines.Take(MaxStderrWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
    }
```

Add `using System.Linq;` only if implicit usings do not already cover it (net8.0 `ImplicitUsings` include it — they do; skip).

- [ ] **Step 2: Flip every wrapper method's return type**

Mechanical rule for the ~24 simple wrappers (`BrowseProjectTreeAsync` … `GetProjectStatusAsync`, `SaveProjectAsync`, `ArchiveProjectAsync`): change `Task<string>` → `Task<WorkerCallResult>` in the signature; bodies are unchanged because they just return `SendBoundProjectRequestAsync(...)`. The two validation early-returns change:

- `ReadCrossReferencesAsync`: `return Task.FromResult($"Error: {filterError}");` → `return Task.FromResult(WorkerCallResult.Fail(filterError!));`
- `ArchiveProjectAsync`: `return Task.FromResult($"Error: {modeError}");` → `return Task.FromResult(WorkerCallResult.Fail(modeError!));`

Then rewrite the four methods with post-processing, deleting their try/catch blocks (centralized now) and `FormatWorkerError` (delete the method entirely):

```csharp
    public async Task<WorkerCallResult> OpenProjectAsync(string projectPath, bool forceRebind)
    {
        if (!CanBind(projectPath, forceRebind, out var bindingError))
        {
            return WorkerCallResult.Fail(bindingError!);
        }

        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "open_project",
                ProjectPath = projectPath,
                Confirm = true,
                ForceRebind = forceRebind,
                AllowTiaConfirmations = true
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        if (!_projectSessionBinding.Bind(projectPath, forceRebind, out var bindError))
        {
            return WorkerCallResult.Fail(bindError!, result.Warnings);
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public async Task<WorkerCallResult> CreateProjectAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "create_project",
                ProjectDirectory = projectDirectory,
                ProjectName = projectName,
                Author = author,
                Comment = comment,
                Confirm = true,
                AllowTiaConfirmations = true
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        var projectPath = TryReadProjectPath(result.Payload);
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            _projectSessionBinding.Bind(projectPath!, forceRebind: true, out _);
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public async Task<WorkerCallResult> SaveProjectAsAsync(
        string? projectPath,
        string targetDirectory,
        string targetName,
        bool rebind)
    {
        var result = await SendBoundProjectRequestAsync(
            "save_project_as",
            projectPath,
            request =>
            {
                request.TargetDirectory = targetDirectory;
                request.TargetName = targetName;
                request.Rebind = rebind;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}").ConfigureAwait(false);

        if (rebind && result.Success)
        {
            var copiedProjectPath = TryReadProjectPath(result.Payload);
            if (!string.IsNullOrWhiteSpace(copiedProjectPath))
            {
                _projectSessionBinding.Bind(copiedProjectPath!, forceRebind: true, out _);
            }
        }

        return result;
    }

    public async Task<WorkerCallResult> CloseProjectAsync(string? projectPath, bool saveBeforeClose)
    {
        var result = await SendBoundProjectRequestAsync(
            "close_project",
            projectPath,
            request =>
            {
                request.SaveBeforeClose = saveBeforeClose;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}").ConfigureAwait(false);

        if (result.Success && _projectSessionBinding.Clear(projectPath, out _) is false)
        {
            _projectSessionBinding.Clear(null, out _);
        }

        return result;
    }
```

Guard `TryReadProjectPath` against malformed payloads (the "wrap SaveProjectAsAsync like its siblings" part of 1.5 — parsing previously happened outside any try/catch in that method):

```csharp
    private static string? TryReadProjectPath(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("projectPath", out var projectPath) &&
                projectPath.ValueKind == JsonValueKind.String)
            {
                return projectPath.GetString();
            }

            if (document.RootElement.TryGetProperty("project", out var project) &&
                project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("path", out var statusPath) &&
                statusPath.ValueKind == JsonValueKind.String)
            {
                return statusPath.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
```

- [ ] **Step 3: Flip `BatchWorkerInvoker` declared types**

In `TiaMcpServer/Batch/BatchWorkerInvoker.cs`: change both method signatures to `Task<WorkerCallResult>` and both fallback arms:

```csharp
    public static Task<WorkerCallResult> ReadCurrentStateAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        // ... all existing arms unchanged ...
        _ => Task.FromResult(WorkerCallResult.Fail($"Unsupported batch write operation '{op.Operation}'.")),
    };

    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        // ... all existing arms unchanged ...
        _ => Task.FromResult(WorkerCallResult.Fail($"Unsupported batch operation '{op.Operation}'.")),
    };
```

Add `using TiaMcpServer.Worker;` — already present.

- [ ] **Step 4: Bridge `BatchTools` with `.ToText()` (temporary until Task 4)**

In `TiaMcpServer/Batch/BatchTools.cs`:

- Both engine wirings: `op => BatchWorkerInvoker.InvokeAsync(workerClient, op)` → `async op => (await BatchWorkerInvoker.InvokeAsync(workerClient, op).ConfigureAwait(false)).ToText()`
- In `ReadCombinedCurrentStateAsync`: replace the loop body:

```csharp
            var state = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
            if (!state.Success)
            {
                return (string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {state.Error}");
            }

            states.Add(new BatchCurrentState(op.OperationId, op.Operation, state.Payload));
```

(The old message interpolated a string that itself began with `"Error: "` — the new interpolation renders identically.)

- [ ] **Step 5: Bridge `ProjectLifecycleTools` with `.ToText()` (temporary until Task 5)**

Apply exactly these per-site edits:

1. `GetProjectStatus`: `return await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);` → `return (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText();`
2. Every `readCurrentState` lambda passed to `ValidateForApplyAsync` that calls the client (in `SaveProject`, `SaveProjectAs`, `ArchiveProject`, `CloseProject`): `() => workerClient.GetProjectStatusAsync(projectPath)` → `async () => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText()`
3. Every preview that reads `currentState` from the client (in `PreviewSaveProject`, `PreviewSaveProjectAs`, `PreviewArchiveProject`, `PreviewCloseProject`): `var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);` → `var currentState = (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText();`
4. Every operation result (in `OpenProject`, `CreateProject`, `SaveProject`, `SaveProjectAs`, `ArchiveProject`, `CloseProject`): append `.ToText()` bridging via an intermediate, e.g. in `OpenProject`:

```csharp
            var result = (await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false)).ToText();
```

and in `SaveProjectAs` the verification call: `await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false)` → `(await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false)).ToText()` — same pattern for the `status` reads in `OpenProject`, `CreateProject`, `SaveProject`, `ArchiveProject`.

The `result.StartsWith("Error:", …)` checks still work on the bridged text — they are removed structurally in Task 5.

- [ ] **Step 6: DI registration in host `Program.cs`**

Replace `builder.Services.AddSingleton<OpennessWorkerClient>();` with:

```csharp
            builder.Services.AddSingleton(sp => new OpennessWorkerClient(
                sp.GetRequiredService<ProjectSessionBinding>(),
                sp.GetRequiredService<ILogger<OpennessWorkerClient>>()));
```

- [ ] **Step 7: Add the logging package to the test project**

`TiaMcpServer.Tests.csproj`, first `<ItemGroup>`:

```xml
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />
```

- [ ] **Step 8: Build the full solution and run all tests**

Run: `dotnet build TiaMcpServer.sln`
Expected: 0 errors (worker project untouched).

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 152 tests, unchanged behavior.

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "refactor: thread WorkerCallResult through OpennessWorkerClient with centralized exception mapping"
```

---

### Task 3: Fake-worker integration harness (verifies 1.1 regression, 1.3 warnings, 1.5 Win32)

**Files:**

- Create: `TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj`
- Create: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.sln` (add project), `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (build-order reference)
- Test: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Interfaces:**

- Consumes: `OpennessWorkerClient(binding, logger: null, workerExecutablePath: …)` from Task 2; `ProjectSessionBinding(null)` auto-binds any first requested path, so the *scenario name is passed as the `projectPath` argument* and echoed to the fake worker in the request JSON.
- Produces: nothing used later — pure coverage.

- [ ] **Step 1: Create the fake worker project**

`TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`TiaMcpServer.FakeWorker/Program.cs`:

```csharp
using System.Text.Json;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// The test encodes the scenario in the request's projectPath field.
var line = Console.In.ReadLine();
if (line is null)
{
    return;
}

string? scenario = null;
try
{
    using var doc = JsonDocument.Parse(line);
    if (doc.RootElement.TryGetProperty("projectPath", out var p))
    {
        scenario = p.GetString();
    }
}
catch (JsonException)
{
    scenario = "malformed-request";
}

switch (scenario)
{
    case "ok":
        Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
        break;
    case "ok-with-stderr":
        Console.Error.WriteLine("Skipping device 'X' while reading hardware configuration: access denied.");
        Console.Error.WriteLine("Skipping subnet 'Y' while reading hardware configuration: not supported.");
        Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
        break;
    case "error-prefix-payload":
        Respond("""{"success":true,"payload":"Error: literal payload text, not a failure"}""");
        break;
    case "worker-error":
        Respond("""{"success":false,"error":"boom"}""");
        break;
    case "malformed":
        Console.Out.WriteLine("this is not json");
        Console.Out.Flush();
        break;
    case "silent-exit":
        Console.Error.WriteLine("worker crashed during attach");
        break;
    default:
        Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
        break;
}

void Respond(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
}
```

- [ ] **Step 2: Wire it into the solution and test build order**

```powershell
dotnet sln TiaMcpServer.sln add TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj
```

In `TiaMcpServer.Tests.csproj`, add next to the Contracts `ProjectReference`:

```xml
    <ProjectReference Include="..\TiaMcpServer.FakeWorker\TiaMcpServer.FakeWorker.csproj" ReferenceOutputAssembly="false" />
```

- [ ] **Step 3: Write the integration tests**

Create `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`:

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Spawns the real IPC pipeline against TiaMcpServer.FakeWorker. One class so xunit
/// runs these sequentially; the client's static WorkerGate serializes sends anyway.
/// </summary>
public class OpennessWorkerClientIntegrationTests
{
    private static string LocateFakeWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.FakeWorker", "bin", configuration, "net8.0",
                    "TiaMcpServer.FakeWorker.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent!;
        }

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }

    private static OpennessWorkerClient CreateClient(string? workerPath = null)
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: workerPath ?? LocateFakeWorker());

    [Fact]
    public async Task Success_ReturnsStructuredPayload()
    {
        var result = await CreateClient().GetProjectStatusAsync("ok");

        Assert.True(result.Success);
        Assert.Equal("{\"hello\":true}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task StderrLines_SurfaceAsWarnings()
    {
        var result = await CreateClient().GetProjectStatusAsync("ok-with-stderr");

        Assert.True(result.Success);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Skipping device 'X'"));
    }

    [Fact]
    public async Task PayloadStartingWithErrorPrefix_IsNotMisclassified()
    {
        // Regression test for item 1.1: before WorkerCallResult this payload was treated as failure.
        var result = await CreateClient().GetProjectStatusAsync("error-prefix-payload");

        Assert.True(result.Success);
        Assert.StartsWith("Error:", result.Payload);
    }

    [Fact]
    public async Task WorkerReportedError_IsStructuredFailure()
    {
        var result = await CreateClient().GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
        Assert.Equal("Error: boom", result.ToText());
    }

    [Fact]
    public async Task MalformedResponse_IsFailureNotCrash()
    {
        var result = await CreateClient().GetProjectStatusAsync("malformed");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SilentExit_SurfacesStderrDetailInError()
    {
        var result = await CreateClient().GetProjectStatusAsync("silent-exit");

        Assert.False(result.Success);
        Assert.Contains("worker crashed during attach", result.Error);
    }

    [Fact]
    public async Task NonExecutableWorkerPath_ProducesActionableWin32Message()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"tia-fake-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(bogus, "not an executable");
        try
        {
            var result = await CreateClient(workerPath: bogus).GetProjectStatusAsync("ok");

            Assert.False(result.Success);
            Assert.Contains(".NET Framework 4.8", result.Error);
            Assert.Contains("openness-worker", result.Error);
        }
        finally
        {
            File.Delete(bogus);
        }
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet build TiaMcpServer.sln && dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter OpennessWorkerClientIntegrationTests`
Expected: PASS — 7 tests. (If `PayloadStartingWithErrorPrefix_IsNotMisclassified` fails, Task 2 was not applied correctly — that exact scenario failed before 1.1.)

Then the full suite: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 159 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "test: add fake-worker IPC integration harness covering stderr warnings and Win32 launch failure"
```

---

### Task 4: Structural batch pipeline — engine, result, formatter, BatchTools (item 1.1 batch layer + 1.3 surface)

**Files:**

- Modify: `TiaMcpServer/Batch/BatchExecutionEngine.cs`, `BatchOperationResult.cs`, `BatchResultFormatter.cs`, `BatchTools.cs`
- Test: `TiaMcpServer.Tests/BatchExecutionEngineTests.cs`, `TiaMcpServer.Tests/BatchResultFormatterTests.cs`

**Interfaces:**

- Consumes: `WorkerCallResult` (Task 1), invoker returning `Task<WorkerCallResult>` (Task 2).
- Produces: `BatchExecutionEngine.ExecuteReadsAsync/ApplyWritesAsync(IReadOnlyList<BatchOperationRequest>, Func<BatchOperationRequest, Task<WorkerCallResult>>)`; `BatchOperationResult(string OperationId, string Operation, string Status, string? Result, IReadOnlyList<string>? Warnings = null)`; formatter emits `warnings` per operation (null when none).

- [ ] **Step 1: Update the engine tests to the new delegate shape (RED)**

In `BatchExecutionEngineTests.cs`, change every fake invoke delegate:

- `op => Task.FromResult($"payload-{op.OperationId}")` → `op => Task.FromResult(WorkerCallResult.Ok($"payload-{op.OperationId}"))`
- `op => Task.FromResult(op.OperationId == "b" ? "Error: not found" : "ok")` → `op => Task.FromResult(op.OperationId == "b" ? WorkerCallResult.Fail("not found") : WorkerCallResult.Ok("ok"))`
- `op => Task.FromResult("done")` → `op => Task.FromResult(WorkerCallResult.Ok("done"))`
- in the stop-on-failure test: `return Task.FromResult(op.OperationId == "b" ? "Error: boom" : "done");` → `return Task.FromResult(op.OperationId == "b" ? WorkerCallResult.Fail("boom") : WorkerCallResult.Ok("done"));`

Add `using TiaMcpServer.Worker;`. Add one new test:

```csharp
    [Fact]
    public async Task ExecuteReadsAsync_PayloadStartingWithErrorPrefix_IsNotAFailure()
    {
        var operations = new[] { Op("a", "get_block_content") };

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => Task.FromResult(WorkerCallResult.Ok("Error: literal SCL comment text")));

        Assert.Equal(BatchOperationStatus.Succeeded, results[0].Status);
    }

    [Fact]
    public async Task ExecuteReadsAsync_CopiesWarningsOntoResult()
    {
        var operations = new[] { Op("a", "browse_project_tree") };

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => Task.FromResult(WorkerCallResult.Ok("[]", new[] { "Skipping device 'X'." })));

        Assert.NotNull(results[0].Warnings);
        Assert.Single(results[0].Warnings!);
    }
```

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter BatchExecutionEngineTests`
Expected: FAIL — compile errors (delegate type mismatch).

- [ ] **Step 2: Implement engine + result record**

`BatchOperationResult.cs` — extend the record:

```csharp
/// <summary>Immutable result for a single batch item.</summary>
public sealed record BatchOperationResult(
    string OperationId,
    string Operation,
    string Status,
    string? Result,
    IReadOnlyList<string>? Warnings = null);
```

`BatchExecutionEngine.cs` — delete `IsFailure`, flip both methods:

```csharp
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Orchestrates batch execution independently of the worker. The actual per-item call is
/// injected as a delegate so ordering, per-item read failures, and write stop-on-first-failure
/// can be unit-tested without a live TIA Openness worker.
/// </summary>
public static class BatchExecutionEngine
{
    /// <summary>Reads run independently; a failing item is recorded but never stops the others.</summary>
    public static async Task<IReadOnlyList<BatchOperationResult>> ExecuteReadsAsync(
        IReadOnlyList<BatchOperationRequest> operations,
        Func<BatchOperationRequest, Task<WorkerCallResult>> invoke)
    {
        var results = new List<BatchOperationResult>(operations.Count);
        foreach (var op in operations)
        {
            var result = await invoke(op).ConfigureAwait(false);
            results.Add(ToOperationResult(op, result));
        }

        return results;
    }

    /// <summary>Writes run sequentially and stop on the first failure; later items are skipped.</summary>
    public static async Task<IReadOnlyList<BatchOperationResult>> ApplyWritesAsync(
        IReadOnlyList<BatchOperationRequest> operations,
        Func<BatchOperationRequest, Task<WorkerCallResult>> invoke)
    {
        var results = new List<BatchOperationResult>(operations.Count);
        var stopped = false;
        foreach (var op in operations)
        {
            if (stopped)
            {
                results.Add(new BatchOperationResult(op.OperationId, op.Operation, BatchOperationStatus.Skipped, null));
                continue;
            }

            var result = await invoke(op).ConfigureAwait(false);
            stopped = !result.Success;
            results.Add(ToOperationResult(op, result));
        }

        return results;
    }

    private static BatchOperationResult ToOperationResult(BatchOperationRequest op, WorkerCallResult result)
        => new(
            op.OperationId,
            op.Operation,
            result.Success ? BatchOperationStatus.Succeeded : BatchOperationStatus.Failed,
            result.ToText(),
            result.Warnings.Count > 0 ? result.Warnings : null);
}
```

- [ ] **Step 3: Formatter — emit warnings (test-first)**

Add to `BatchResultFormatterTests.cs`:

```csharp
    [Fact]
    public void ReadBatch_IncludesWarningsWhenPresent()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded, "[]",
                new[] { "Skipping device 'X'." }),
        };

        var json = BatchResultFormatter.ReadBatch(results);

        Assert.Contains("\"warnings\":[\"Skipping device 'X'.\"]", json);
    }
```

Run to see it fail, then in `BatchResultFormatter.Project(...)` change the anonymous projection:

```csharp
            .Select(r => (object)new
            {
                operationId = r.OperationId,
                operation = r.Operation,
                status = r.Status,
                result = r.Result,
                warnings = r.Warnings
            })
```

(`warnings` serializes as `null` for items without warnings — the formatter's options don't skip nulls, matching the existing `result:null` behavior on skipped items.)

- [ ] **Step 4: Remove the Task-2 bridges in `BatchTools`**

Both engine wirings go back to method-group style:

```csharp
        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => BatchWorkerInvoker.InvokeAsync(workerClient, op)).ConfigureAwait(false);
```

(same for `ApplyWritesAsync`). The `ReadCombinedCurrentStateAsync` body already consumes `WorkerCallResult` from Task 2 Step 4 — no change.

- [ ] **Step 5: Run all tests**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 162 tests. If any `BatchResultFormatterTests` or `BatchToolsTests` assert exact JSON without `warnings`, update those expected strings to include `"warnings":null` per item.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat: structural failure classification and per-item warnings in batch pipeline"
```

---

### Task 5: Structural safety + lifecycle layer (item 1.1 completion)

**Files:**

- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs`, `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- Test: `TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs`, `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs` (signature-driven updates only)

**Interfaces:**

- Consumes: `WorkerCallResult` and client signatures from Task 2.
- Produces: `WriteSafetyTooling.ValidateForApplyAsync(…, Func<Task<WorkerCallResult>> readCurrentState)`; `WriteSafetyTooling.CreatePreview(…, WorkerCallResult currentState, string? diff = null)`; `WriteSafetyTooling.BuildApplyResult(string toolName, WorkerCallResult operationResult, string? verificationName = null, string? verificationResult = null)`.

**Hash-consistency rule (critical):** the safety token binds `HashText(currentState)`. Before this task the hashed string was the raw client payload; after it, it must be `WorkerCallResult.Payload` — the same bytes. Never hash `ToText()` output of a failure (failures abort before hashing anyway).

- [ ] **Step 1: Flip `WriteSafetyTooling` (three members)**

```csharp
    public static async Task<WriteSafetyApplyContext> ValidateForApplyAsync(
        string? safetyToken,
        string previewToolName,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        Func<Task<WorkerCallResult>> readCurrentState)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return WriteSafetyApplyContext.Invalid(
                $"Safety token required. Call {previewToolName} first, review the preview, then pass its safetyToken with confirm=true.");
        }

        var currentState = await readCurrentState().ConfigureAwait(false);
        if (!currentState.Success)
        {
            return WriteSafetyApplyContext.Invalid(
                $"Could not read current state before write. Error: {currentState.Error}");
        }

        var validation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            toolName,
            projectPath,
            target,
            requestedInput,
            currentState.Payload,
            previewToolName);

        return validation.IsValid
            ? WriteSafetyApplyContext.Valid(currentState.Payload)
            : WriteSafetyApplyContext.Invalid(validation.Error);
    }

    public static string CreatePreview(
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        WorkerCallResult currentState,
        string? diff = null)
    {
        if (!currentState.Success)
        {
            return $"Could not read current state before preview. Error: {currentState.Error}";
        }

        return WriteSafetyService.Shared.CreatePreview(
            toolName,
            projectPath,
            target,
            summary,
            requestedInput,
            currentState.Payload,
            diff);
    }

    public static string BuildApplyResult(
        string toolName,
        WorkerCallResult operationResult,
        string? verificationName = null,
        string? verificationResult = null)
    {
        return JsonSerializer.Serialize(
            new
            {
                toolName,
                success = operationResult.Success,
                operationResult = operationResult.ToText(),
                warnings = operationResult.Warnings.Count > 0 ? operationResult.Warnings : null,
                verification = verificationName is null
                    ? null
                    : new
                    {
                        name = verificationName,
                        result = verificationResult
                    }
            },
            JsonOptions);
    }
```

Add `using TiaMcpServer.Worker;`.

- [ ] **Step 2: Rewrite `ProjectLifecycleTools` call sites structurally**

Per-tool rules (apply to all six apply tools + four client-reading previews; `PreviewOpenProject`/`PreviewCreateProject` wrap their local state strings):

- Local state producers wrap in `Ok(...)`:
  - `WriteSafetyTooling.DescribePathState(projectPath)` → `WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath))` (as `CreatePreview` arg and inside `readCurrentState` lambdas: `() => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)))`)
  - Same for `DescribeProjectCreationState(projectDirectory, projectName)`.
- Client-reading previews drop the Task-2 `.ToText()` bridge and pass the result straight through, e.g. `PreviewSaveProject`:

```csharp
            var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
            var target = new { projectPath };
            var requestedInput = new { projectPath };
            return WriteSafetyTooling.CreatePreview(
                "save_project",
                projectPath,
                target,
                "Save the active TIA Portal project.",
                requestedInput,
                currentState);
```

- `readCurrentState` lambdas that call the client drop the bridge: `() => workerClient.GetProjectStatusAsync(projectPath)`.
- Apply tools use the structural result end-to-end. Full example, `SaveProject` (same shape for `OpenProject`, `CreateProject`, `SaveProjectAs`, `ArchiveProject`; `CloseProject` passes `null` verification as today):

```csharp
            var result = await workerClient.SaveProjectAsync(projectPath).ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("save_project", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", status);
```

- `GetProjectStatus` keeps its Task-2 `.ToText()` (that IS its string boundary).
- After this step, `grep -n "StartsWith(\"Error:\"" TiaMcpServer/` must return ZERO hits in `TiaMcpServer/` (host project). Verify:

```powershell
Get-ChildItem TiaMcpServer -Recurse -Filter *.cs | Select-String 'StartsWith("Error:'
```

Expected: no output.

- [ ] **Step 3: Fix test compile fallout**

`WriteToolSafetyTokenTests.cs` / `ProjectLifecycleToolTests.cs` / `BatchToolsTests.cs` call MCP tool methods whose signatures did NOT change — most tests compile untouched. Any test that calls `WriteSafetyTooling.CreatePreview`/`ValidateForApplyAsync`/`BuildApplyResult` directly must wrap state strings with `WorkerCallResult.Ok(...)` / use `Func<Task<WorkerCallResult>>` / pass a `WorkerCallResult`. Do not weaken assertions — the expected message texts are unchanged by design.

- [ ] **Step 4: Run all tests**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 162 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "refactor: remove Error-prefix sniffing from safety and lifecycle layers"
```

---

### Task 6: Reject unknown JSON properties on batch items (item 1.6)

**Files:**

- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs`
- Test: `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs` (new)

**Interfaces:**

- Consumes: nothing new.
- Produces: deserializing a batch item with any unknown property throws `JsonException` naming the property. Enforced by a type-level attribute, so it holds under ANY `JsonSerializerOptions`/resolver the MCP SDK uses.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchOperationRequestJsonTests
{
    // Mirrors the camelCase + case-insensitive binding the MCP SDK uses for tool arguments.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MisspelledOptionalProperty_IsRejectedNotSilentlyDropped()
    {
        // "ip_adress" is the exact trap from the audit: a typo that previously succeeded
        // silently and left the device unconfigured while reporting success.
        var json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ip_adress":"192.168.0.10"}""";

        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions));

        Assert.Contains("ip_adress", ex.Message);
    }

    [Fact]
    public void KnownCamelCaseProperties_StillDeserialize()
    {
        var json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ipAddress":"192.168.0.10"}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions);

        Assert.NotNull(request);
        Assert.Equal("192.168.0.10", request!.IpAddress);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter BatchOperationRequestJsonTests`
Expected: FAIL — `MisspelledOptionalProperty_IsRejectedNotSilentlyDropped` (no exception thrown; unknown members are skipped by default).

- [ ] **Step 3: Implement**

In `BatchOperationRequest.cs`, add `using System.Text.Json.Serialization;` and the attribute:

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class BatchOperationRequest
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 164 tests.

- [ ] **Step 5: Manual note for the reviewer (do not skip)**

The attribute is honored by System.Text.Json's contract metadata regardless of which options instance the ModelContextProtocol SDK passes, so SDK-side binding also rejects the property. The resulting MCP tool error is STJ's own message ("The JSON property 'ip_adress' could not be mapped…"), which names the property — acceptable. If a future live smoke test shows the SDK swallows the message, file a follow-up; do not add SDK-specific handling now.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "fix: reject unknown JSON properties on batch items instead of silently dropping them"
```

---

### Task 7: `HardwareConfigReader` — nullable fallbacks + messages (items 1.4 + 1.3 routing)

**Files:**

- Modify: `TiaMcpServer.Contracts/HardwareConfigInfo.cs`, `TiaMcpServer.Contracts/DeviceInfo.cs`, `TiaMcpServer.Contracts/DeviceItemInfo.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Test: `TiaMcpServer.Tests/HardwareConfigInfoTests.cs`

**Interfaces:**

- Consumes: nothing from earlier tasks (independent — contracts + worker only).
- Produces: `HardwareConfigInfo.Messages : List<string>`; `DeviceInfo.Name/TypeIdentifier : string?`; `DeviceItemInfo.Name/TypeIdentifier : string?`, `PositionNumber : int?`. Worker serializer already omits nulls (`WhenWritingNull`), so unknown values disappear from the payload instead of masquerading as `""`/`0`.

- [ ] **Step 1: DTO tests first (RED)**

Add to `HardwareConfigInfoTests.cs` (adjust the existing test that asserts `PositionNumber == 0` default to expect `null`):

```csharp
    [Fact]
    public void MessagesRoundTrip()
    {
        var config = new HardwareConfigInfo
        {
            Messages = { "Could not read device 'X' type identifier: access denied." }
        };

        var json = JsonSerializer.Serialize(config);
        var roundTripped = JsonSerializer.Deserialize<HardwareConfigInfo>(json)!;

        Assert.Equal(
            "Could not read device 'X' type identifier: access denied.",
            Assert.Single(roundTripped.Messages));
    }

    [Fact]
    public void UnreadableValues_AreNullNotFallbackDefaults()
    {
        var item = new DeviceItemInfo();

        Assert.Null(item.Name);
        Assert.Null(item.TypeIdentifier);
        Assert.Null(item.PositionNumber);
    }
```

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter HardwareConfigInfoTests`
Expected: FAIL — no `Messages` property; `PositionNumber` non-nullable.

- [ ] **Step 2: Update the contracts**

`HardwareConfigInfo.cs` — add:

```csharp
    /// <summary>Non-fatal degradation notes: members that could not be read and were omitted.</summary>
    public List<string> Messages { get; set; } = new List<string>();
```

`DeviceInfo.cs`:

```csharp
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }
```

`DeviceItemInfo.cs`:

```csharp
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    public int? PositionNumber { get; set; }
```

Run the filtered tests again — Expected: PASS. Fix any other test in `HardwareConfigInfoTests` that asserted the old `string.Empty`/`0` defaults.

- [ ] **Step 3: Rewrite `HardwareConfigReader` to thread a messages list**

Transformation rules for the whole file (mirror `CrossReferenceReader`'s pass-the-list pattern):

1. Every private helper gains a trailing `List<string> messages` parameter; `Read` seeds it from the result:

```csharp
    public static HardwareConfigInfo Read(Project project)
    {
        var result = new HardwareConfigInfo();

        foreach (Device device in project.Devices)
        {
            try
            {
                result.Devices.Add(ReadDevice(device, result.Messages));
            }
            catch (EngineeringException ex)
            {
                result.Messages.Add($"Skipped a device while reading hardware configuration: {ex.Message}");
            }
        }

        foreach (Subnet subnet in project.Subnets)
        {
            try
            {
                result.Subnets.Add(ReadSubnet(subnet, result.Messages));
            }
            catch (EngineeringException ex)
            {
                result.Messages.Add($"Skipped a subnet while reading hardware configuration: {ex.Message}");
            }
        }

        return result;
    }
```

1. Every `Console.Error.WriteLine($"Skipping {description}: {ex.Message}")` in this file becomes `messages.Add($"Could not read {description}: {ex.Message}")` (loop-level catches use "Skipped a/an …" as above). No `Console.Error` calls remain in this file.
2. The two leaf helpers flip to nullable:

```csharp
    private static string? ReadString(Func<string> read, string description, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            messages.Add($"Could not read {description}: {ex.Message}");
            return null;
        }
    }

    private static int? ReadInt(Func<int> read, string description, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            messages.Add($"Could not read {description}: {ex.Message}");
            return null;
        }
    }
```

1. `ReadAttribute` / `ReadPropertyOrAttribute` keep their `string?` returns but route to `messages` instead of stderr (same added parameter).
2. Where a possibly-null name feeds a later description, coalesce for the description only, e.g. in `ReadDeviceItem`:

```csharp
        var itemName = ReadString(() => item.Name, "device item name", messages);
        var itemDescription = itemName ?? "(unnamed)";
        var itemInfo = new DeviceItemInfo
        {
            Name = itemName,
            TypeIdentifier = ReadString(() => item.TypeIdentifier, $"device item '{itemDescription}' type identifier", messages),
            PositionNumber = ReadInt(() => item.PositionNumber, $"device item '{itemDescription}' position number", messages),
            Address = ReadAttribute((IEngineeringObject)item, "Address", $"device item '{itemDescription}' address", messages)
        };
```

Apply the same coalescing in `ReadDevice` (`deviceInfo.Name ?? "(unnamed)"`), `ReadNode`, `ReadSubnet`, `ReadNetworkInterface` (`interfaceName` may now be null → `ReadPropertyOrAttribute(...) ?? string.Empty` stays for `NetworkInterfaceInfo.Name` since that DTO field is unchanged), `ReadIoSystem`, `ReadConnectedSubnetName`, `ReadIoSystemName`, `FindParentDeviceName`.
6. `ReadProperty`/`ReadEnumerableProperty` (delegating to `OpennessReflection`) are unchanged — their stderr output surfaces via the Task 2 warnings channel.

- [ ] **Step 4: Build the worker and run all tests**

Run: `dotnet build TiaMcpServer.sln`
Expected: 0 errors (this is the only verification for the net48 reader — it cannot be unit-tested without Siemens DLLs).

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 166 tests.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "fix: distinguish unreadable hardware-config values from real defaults via nullables and messages"
```

---

### Task 8: Worker-side error fidelity — searcher catch narrowing + catch-all type name (items 1.7 + 1.5 worker)

**Files:**

- Modify: `TiaMcpServer.OpennessWorker/Openness/OpennessReflection.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`

**Interfaces:**

- Consumes: nothing from earlier tasks (independent).
- Produces: `OpennessReflection.ReadProperty(object? instance, string propertyName, string description)` — broad-but-bounded overload catching `EngineeringException` + `TargetInvocationException` only. Unexpected exception types (e.g. `AmbiguousMatchException`) now propagate to the worker's catch-all and surface as `"<TypeName>: <message>"` failures instead of empty search results — that is the intended behavior change.

- [ ] **Step 1: Add the description-taking overload to `OpennessReflection`**

```csharp
    /// <summary>
    /// Broad-but-bounded variant for reflection over unverified SDK surfaces: additionally
    /// swallows <see cref="TargetInvocationException"/> regardless of inner type. Anything
    /// else (e.g. AmbiguousMatchException) is a bug and must propagate.
    /// </summary>
    public static object? ReadProperty(object? instance, string propertyName, string description)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            return instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(instance);
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            return null;
        }
    }
```

Also update the class `<remarks>` (the "call sites keep their own broader helpers" sentence no longer applies — replace with: `Reflection over unverified SDK surfaces uses the description-taking ReadProperty overload below.`).

- [ ] **Step 2: Gut `EquipmentCatalogSearcher`'s private helpers**

1. Delete the private `ReadProperty(object?, string, string)` method (lines ~223–256) and the private `Enumerate(object?, string)` method (lines ~258–319).
2. Redirect: `ReadStringProperty` becomes

```csharp
    private static string? ReadStringProperty(object instance, string propertyName, string description)
    {
        return OpennessReflection.ReadProperty(instance, propertyName, description)?.ToString();
    }
```

1. Every remaining `ReadProperty(x, name, desc)` call in the file → `OpennessReflection.ReadProperty(x, name, desc)`; every `Enumerate(x, desc)` call → `OpennessReflection.Enumerate(x, desc)`.
2. Narrow the root-loop catch in `Search` — replace the two catch blocks with one filtered catch:

```csharp
            try
            {
                Traverse(catalog, string.Empty, query, results, seenEntries, visited);
            }
            catch (Exception ex) when (ex is EngineeringException or TargetInvocationException)
            {
                Console.Error.WriteLine($"Skipping hardware catalog root while searching equipment catalog: {ex.Message}");
            }
```

1. Remove now-unused `using System.Collections;` and `using System.Runtime.CompilerServices;` stays (used by `RuntimeHelpers`). Keep all `// UNVERIFIED SDK CALL` comments that still describe live call sites.

- [ ] **Step 3: Include the exception type name in the worker catch-all**

`TiaMcpServer.OpennessWorker/Program.cs`, in `HandleLine`:

```csharp
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return Failure($"{ex.GetType().Name}: {ex.Message}");
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build TiaMcpServer.sln`
Expected: 0 errors, 0 new warnings in the worker project.

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
Expected: PASS — 166 tests (unchanged; worker not under test).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "fix: narrow equipment-catalog reflection catches and name exception types in worker errors"
```

---

### Task 9: Docs, plan bookkeeping, graph update, final verification

**Files:**

- Modify: `README.md`, `docs/IMPROVEMENT_PLAN.md`
- Run: graphify update

**Interfaces:** none.

- [ ] **Step 1: README**

In the section describing batch tool results (grep for `execute_read_batch` in README.md), add one sentence:

> Every operation result may carry a `warnings` array — non-fatal degradation notes captured from the TIA Openness worker (e.g. members skipped while reading a protected device). A populated `warnings` array means the payload may be partial. `read_hardware_config` additionally reports unreadable members in a payload-level `messages` array, and omits values it could not read instead of returning `0`/empty-string placeholders.

- [ ] **Step 2: Mark Phase 1 items done in `docs/IMPROVEMENT_PLAN.md`**

Append `— DONE 2026-07-16` to rows 1.1, 1.3, 1.4, 1.5, 1.6, 1.7 (keep the existing style used by Phase 0 rows). In the "Testing gaps" section, annotate the fake-worker bullet: mark stderr propagation, malformed JSON, and Win32Exception launch failure as covered (timeout path and persistent-worker restart remain open for 2.1).

- [ ] **Step 3: Final verification**

```powershell
dotnet build TiaMcpServer.sln
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
Get-ChildItem TiaMcpServer -Recurse -Filter *.cs | Select-String 'StartsWith("Error:'
```

Expected: build clean; all tests pass (166); the grep returns nothing.

- [ ] **Step 4: Update the knowledge graph** (project CLAUDE.md requirement)

```powershell
graphify update .
```

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "docs: mark improvement-plan phase 1 done and document warnings/messages channels"
```

---

## Self-Review Results

- **Spec coverage:** 1.1 → Tasks 1, 2, 4, 5 (+ regression tests in 3, 4); 1.3 → Tasks 2 (capture), 3 (tests), 4/5 (surfacing), 7 (hardware messages routing); 1.4 → Task 7; 1.5 → Task 2 (Win32 + `SaveProjectAsAsync` guard), Task 8 (type name); 1.6 → Task 6; 1.7 → Task 8. Testing-gap item (fake worker harness) → Task 3.
- **Deliberate scope notes:** timeout-path integration test skipped (needs an injectable `WorkerTimeout`; defer to 2.1 persistent-worker work). `browse_project_tree` payload keeps its bare-array shape; its degradation reaches the agent via `warnings`.
- **Type consistency check:** `WorkerCallResult.Ok/Fail/ToText/Warnings`, `Func<BatchOperationRequest, Task<WorkerCallResult>>`, `BatchOperationResult(…, IReadOnlyList<string>? Warnings = null)`, `CreatePreview(…, WorkerCallResult currentState, string? diff = null)` used consistently across Tasks 1–5. Hash input is `Payload` on both preview and apply paths (Task 5 rule).
- **Task ordering:** 1 → 2 → 3 → 4 → 5 sequential; 6, 7, 8 independent of each other and of 3–5 (all require only Task 2's merge state for a clean rebase — 6 and 7 don't even need that); 9 last.
