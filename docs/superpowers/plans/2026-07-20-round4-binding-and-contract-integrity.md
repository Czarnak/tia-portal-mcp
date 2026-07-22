# Round 4 — Session Binding and Contract Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the session-binding gap found in live TIA Portal V21 testing, stop the test suite polluting the production audit trail, and make "field declared but never forwarded" bugs unrepresentable.

**Architecture:** Three independent threads. (1) The worker reports which project it actually operated on; the host binds to that ground truth instead of adopting whatever path the caller asked for, and read operations refuse to open a project alongside a different one. (2) `WriteSafetyService` moves from a static singleton to constructor injection so tests can redirect the audit directory. (3) `BatchOperationSpec` gains a declared-optional-field list, inapplicable fields become validation errors, and a FakeWorker echo test proves that every declared field actually reaches the worker.

**Tech Stack:** C# / .NET 8 (host, tests), .NET Framework 4.8 (Openness worker), netstandard2.0 (Contracts), xunit, `System.Text.Json`, ModelContextProtocol SDK.

**Spec:** `docs/superpowers/specs/2026-07-20-round4-binding-and-contract-integrity-design.md`

## Global Constraints

- **SDK:** `global.json` pins .NET SDK 8.0.400 with `rollForward: latestMajor`. Use `dotnet`, never `dotnet8`.
- **Build serialization:** always `dotnet build TiaMcpServer.sln -m:1`. The `-m:1` is required — parallel builds conflict over the worker output directory.
- **Build without TIA Portal installed:** append `/p:UseTiaPortalReferenceStubs=true`. Use this unless you have TIA Portal V21 locally.
- **`TiaMcpServer.Contracts` is netstandard2.0 and dependency-free.** No `System.Text.Json` package reference, no Siemens types. Anything placed here must compile under those constraints.
- **`TiaMcpServer.Tests` links host source files** via `<Compile Include>`, not a project reference. Editing files under `TiaMcpServer/Worker/`, `TiaMcpServer/Batch/`, `TiaMcpServer/Safety/`, `TiaMcpServer/Tools/`, `TiaMcpServer/Json/` is picked up automatically. It cannot link worker files that reference Siemens assemblies.
- **The test project cannot reference the net48 worker.** Worker logic that needs unit tests must be a pure function in `Contracts`.
- **Commit format:** `<type>: <description>` — types `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`.
- **Branch:** `fix/round4-binding-and-contract-integrity` (already created; the spec commit `e59377d` is on it).
- **Immutability:** create new objects rather than mutating. `WorkerCallResult` is a record — use `with` expressions.

## Before You Start

Build the whole solution once. Task 8 needs `TiaMcpServer.FakeWorker.exe` on disk, and several tasks
run integration tests that launch it.

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: build succeeds, 341 tests pass. If the baseline is not green, stop and report — do not
start on top of a red suite.

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `TiaMcpServer/Json/TiaJson.cs` | Modify | Freeze `Presentation` options after construction (Task 1) |
| `TiaMcpServer/Program.cs` | Modify | Register a `WriteSafetyService` instance instead of the static (Task 2) |
| `TiaMcpServer/Safety/WriteSafetyService.cs` | Modify | Delete the `Shared` static (Task 2) |
| `TiaMcpServer/Safety/WriteSafetyTooling.cs` | Modify | Accept the service as a parameter (Task 2) |
| `TiaMcpServer/Tools/ProjectLifecycleTools.cs` | Modify | Accept the injected service on 6 write tools (Task 2) |
| `TiaMcpServer/Batch/BatchTools.cs` | Modify | Accept the injected service on 2 write tools (Task 2) |
| `TiaMcpServer.Contracts/WorkerResponse.cs` | Modify | Carry `ResolvedProjectPath` (Task 3) |
| `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs` | Modify | Expose the current project path (Task 3) |
| `TiaMcpServer.OpennessWorker/Program.cs` | Modify | Stamp `ResolvedProjectPath`; apply the open policy (Tasks 3, 5) |
| `TiaMcpServer/Worker/WorkerCallResult.cs` | Modify | Carry `ResolvedProjectPath` (Task 4) |
| `TiaMcpServer.Contracts/ProjectSessionBinding.cs` | Modify | Make `TryResolve` non-mutating (Task 4) |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Modify | Adopt the resolved path after success (Task 4) |
| `TiaMcpServer.Contracts/ProjectOpenPolicy.cs` | **Create** | Pure decision: use attached / open requested / refuse (Task 5) |
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | Modify | `OptionalFields`, `All`, inapplicable-field rejection (Tasks 6, 7) |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Modify | Scope the `deviceItemName` description (Task 7) |
| `TiaMcpServer.FakeWorker/Program.cs` | Modify | Add the `"echo"` scenario (Task 8) |
| `TiaMcpServer.Tests/TiaJsonTests.cs` | **Create** | Task 1 |
| `TiaMcpServer.Tests/AuditIsolationTests.cs` | **Create** | Task 2 |
| `TiaMcpServer.Tests/ProjectOpenPolicyTests.cs` | **Create** | Task 5 |
| `TiaMcpServer.Tests/BatchFieldForwardingTests.cs` | **Create** | Task 8 |

---

### Task 1: Freeze `TiaJson.Presentation`

`TiaJson.Presentation` is a public mutable `JsonSerializerOptions` whose formatting feeds the
safety-token `requestedInputHash`. A formatting change invalidates every outstanding token. The file
already says "Keep this stable" in a comment; this makes it enforceable.

**Files:**
- Modify: `TiaMcpServer/Json/TiaJson.cs:26-30`
- Test: `TiaMcpServer.Tests/TiaJsonTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaJson.Presentation` is read-only after static initialization. `JsonSerializerOptions.IsReadOnly` returns `true`. Any later mutation throws `InvalidOperationException`.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/TiaJsonTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests;

public class TiaJsonTests
{
    [Fact]
    public void Presentation_IsReadOnly()
    {
        Assert.True(TiaJson.Presentation.IsReadOnly);
    }

    [Fact]
    public void Presentation_RejectsMutation()
    {
        Assert.Throws<InvalidOperationException>(() => TiaJson.Presentation.WriteIndented = true);
    }

    [Fact]
    public void Presentation_StillSerializesCamelCaseAndCompact()
    {
        var json = JsonSerializer.Serialize(new { ProjectPath = "C:\\p.ap21" }, TiaJson.Presentation);

        Assert.Equal("{\"projectPath\":\"C:\\\\p.ap21\"}", json);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~TiaJsonTests`

Expected: FAIL. `Presentation_IsReadOnly` asserts `False` is `True`, and `Presentation_RejectsMutation` fails because no exception is thrown.

- [ ] **Step 3: Freeze the options**

In `TiaMcpServer/Json/TiaJson.cs`, replace the field initializer (lines 26-30) with a field plus a static constructor:

```csharp
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    static TiaJson()
    {
        // Frozen on purpose: audit records and the safety-token input hash are both derived
        // through these options, so a formatting change would invalidate outstanding tokens.
        Presentation.MakeReadOnly();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~TiaJsonTests`

Expected: PASS, 3 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test TiaMcpServer.Tests`

Expected: all tests pass. If anything mutates `Presentation` at runtime it will now throw — fix the mutation, not the freeze.

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer/Json/TiaJson.cs TiaMcpServer.Tests/TiaJsonTests.cs
git commit -m "fix: freeze TiaJson.Presentation so safety-token hashing cannot drift"
```

---

### Task 2: Inject `WriteSafetyService` instead of the `Shared` static

`WriteSafetyService.Shared` is reached statically from 12 call sites. `TiaMcpServer.Tests` links
those files and exercises those tools, so every `dotnet test` run appends records to
`%LOCALAPPDATA%\TiaMcpServer\audit`. Measured live: 39 of 42 records came from the test suite.
Deleting the static is the fix — with no static, no test can reach the production directory.

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs:13` (delete `Shared`)
- Modify: `TiaMcpServer/Program.cs:24`
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs:10,32,46,61`
- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs` (6 tools)
- Modify: `TiaMcpServer/Batch/BatchTools.cs:43,64,78,108,126,144`
- Modify: `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs`, `WriteToolSafetyTokenTests.cs`, `BatchToolsTests.cs`, `OpennessWorkerClientIntegrationTests.cs`
- Test: `TiaMcpServer.Tests/AuditIsolationTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `WriteSafetyService.Shared` no longer exists.
  - `WriteSafetyTooling.ValidateForApplyAsync(WriteSafetyService safety, string? safetyToken, string previewToolName, string toolName, string? projectPath, object target, object requestedInput, Func<Task<WorkerCallResult>> readCurrentState)` — service is the **first** parameter.
  - `WriteSafetyTooling.CreatePreview(WriteSafetyService safety, string toolName, string? projectPath, object target, string summary, object requestedInput, WorkerCallResult currentState, string? diff = null, string? instructions = null)` — service is the **first** parameter.
  - Every `ProjectLifecycleTools` write tool takes `WriteSafetyService safety` as its **second** parameter, after `OpennessWorkerClient workerClient`.
  - `BatchTools.PreviewWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations)` and `BatchTools.ApplyWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations, bool confirm = false, string? safetyToken = null)`.
  - `WriteSafetyService.NormalizeProjectPath` stays **static** — it is pure. Do not change its call sites.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/AuditIsolationTests.cs`. This test proves the tool layer writes only where
it is told to.

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// The tool layer must never reach a process-wide audit directory. Before DI this was impossible
/// to assert: ProjectLifecycleTools resolved WriteSafetyService.Shared, so 39 of 42 records in a
/// real machine's audit trail came from `dotnet test`.
/// </summary>
public class AuditIsolationTests
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

    [Fact]
    public async Task LifecycleTool_WritesAuditOnlyToTheInjectedDirectory()
    {
        var auditDirectory = Path.Combine(Path.GetTempPath(), "tia-audit-" + Guid.NewGuid().ToString("N"));
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
        var before = Directory.Exists(defaultDirectory)
            ? Directory.GetFiles(defaultDirectory).Length
            : 0;

        try
        {
            var safety = new WriteSafetyService(
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(10),
                auditDirectory);

            using var client = new OpennessWorkerClient(
                new ProjectSessionBinding(null),
                logger: null,
                workerExecutablePath: LocateFakeWorker());

            var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: "ok");
            using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
            var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

            await ProjectLifecycleTools.OpenProject(
                client,
                safety,
                projectPath: "ok",
                confirm: true,
                safetyToken: token);

            Assert.True(Directory.Exists(auditDirectory));
            Assert.NotEmpty(Directory.GetFiles(auditDirectory));

            var after = Directory.Exists(defaultDirectory)
                ? Directory.GetFiles(defaultDirectory).Length
                : 0;
            Assert.Equal(before, after);
        }
        finally
        {
            if (Directory.Exists(auditDirectory))
            {
                Directory.Delete(auditDirectory, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~AuditIsolationTests`

Expected: FAIL to **compile** — `ProjectLifecycleTools.OpenProject` has no overload taking a `WriteSafetyService`. That compile failure is the red state for this task.

- [ ] **Step 3: Delete the static and thread the service through `WriteSafetyTooling`**

In `TiaMcpServer/Safety/WriteSafetyService.cs`, delete line 13:

```csharp
    public static WriteSafetyService Shared { get; } = new();
```

In `TiaMcpServer/Safety/WriteSafetyTooling.cs`, change the two method signatures and their internal
calls. `ValidateForApplyAsync` becomes:

```csharp
    public static async Task<WriteSafetyApplyContext> ValidateForApplyAsync(
        WriteSafetyService safety,
        string? safetyToken,
        string previewToolName,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        Func<Task<WorkerCallResult>> readCurrentState)
    {
```

and its body's `WriteSafetyService.Shared.ValidateAndConsume(` becomes `safety.ValidateAndConsume(`.

`CreatePreview` becomes:

```csharp
    public static string CreatePreview(
        WriteSafetyService safety,
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        WorkerCallResult currentState,
        string? diff = null,
        string? instructions = null)
    {
```

and its body's `WriteSafetyService.Shared.CreatePreview(` becomes `safety.CreatePreview(`.

Leave the three `WriteSafetyService.NormalizeProjectPath(...)` calls at lines 130, 167, 169 alone.

- [ ] **Step 4: Update `Program.cs`**

In `TiaMcpServer/Program.cs`, replace line 24:

```csharp
            builder.Services.AddSingleton(new WriteSafetyService());
```

- [ ] **Step 5: Thread the service through `ProjectLifecycleTools`**

For each of the six write tools (`OpenProject`, `CreateProject`, `SaveProject`, `SaveProjectAs`,
`ArchiveProject`, `CloseProject`), add `WriteSafetyService safety` immediately after
`OpennessWorkerClient workerClient`, then update the three call sites inside each method.

`GetProjectStatus` is a read tool — leave it unchanged.

Worked example for `OpenProject`; apply the same three edits to the other five:

```csharp
        public static async Task<string> OpenProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Path to the .ap21 project file to open.")] string projectPath, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null, [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "open_project", projectPath, target, $"Open and bind TIA Portal project '{projectPath}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)), diff: null, instructions: ApplyInstructions("open_project"));
            if (!confirm) return ConfirmRequired("open_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("open_project"), "open_project", projectPath, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)))).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("open_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", status);
        }
```

Note the local `var safety = await ...` is renamed to `safetyContext` in every method — the parameter
now owns the name `safety`. Make that rename consistently or the code will not compile.

- [ ] **Step 6: Thread the service through `BatchTools`**

In `TiaMcpServer/Batch/BatchTools.cs`:

- `PreviewWriteBatch`: add `WriteSafetyService safety,` after `OpennessWorkerClient workerClient,`; change `WriteSafetyService.Shared.CreatePreview(` (line 64) to `safety.CreatePreview(`.
- `ApplyWriteBatch`: add `WriteSafetyService safety,` after `OpennessWorkerClient workerClient,`; change `WriteSafetyService.Shared.ValidateEnvelope(` (line 108) to `safety.ValidateEnvelope(`, `WriteSafetyService.Shared.ValidateAndConsume(` (line 126) to `safety.ValidateAndConsume(`, and `WriteSafetyService.Shared.AppendAudit(` (line 144) to `safety.AppendAudit(`.
- `ExecuteReadBatch`: unchanged.

- [ ] **Step 7: Update existing tests that call these tools**

Every call to a `ProjectLifecycleTools` write tool or `BatchTools.PreviewWriteBatch`/`ApplyWriteBatch`
in the test project needs a service argument. Find them:

```bash
grep -rn "ProjectLifecycleTools\.\|BatchTools.PreviewWriteBatch\|BatchTools.ApplyWriteBatch" TiaMcpServer.Tests/
```

In each affected test, construct a scoped service and pass it:

```csharp
    private static WriteSafetyService CreateSafety(string auditDirectory)
        => new(() => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, auditDirectory);
```

Give each test class a temp directory it creates and deletes, following the pattern already used in
`WriteSafetyServiceTests.cs:177`. Do not point tests at the default directory.

- [ ] **Step 8: Build and run the full suite**

Run:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: build succeeds with no reference to `WriteSafetyService.Shared` anywhere, and all tests
pass including the new `AuditIsolationTests`.

- [ ] **Step 9: Verify the static is really gone**

Run: `grep -rn "WriteSafetyService.Shared" --include=*.cs .`

Expected: no matches. If any remain, they were missed in steps 3-7.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "fix: inject WriteSafetyService instead of a static so tests stop writing the production audit trail"
```

---

### Task 3: Worker reports the resolved project path

The host cannot bind to ground truth unless the worker tells it what ground truth is. Stamp the
answer once, at the dispatch choke point, so all 22 operations get it without each remembering.

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerResponse.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs:113`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs` (the `Execute` helper, ~line 600)
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Test: `TiaMcpServer.Tests/WorkerResponseJsonTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `WorkerResponse.ResolvedProjectPath` (`string?`), serialized as `resolvedProjectPath`. Null when no project was attached. Task 4 reads it.

- [ ] **Step 1: Write the failing test**

Add to `TiaMcpServer.Tests/WorkerResponseJsonTests.cs`:

```csharp
    [Fact]
    public void Deserializes_ResolvedProjectPath()
    {
        const string json = """{"success":true,"payload":"{}","resolvedProjectPath":"C:\\proj\\SimpleProject.ap21"}""";

        var response = System.Text.Json.JsonSerializer.Deserialize<WorkerResponse>(
            json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        Assert.NotNull(response);
        Assert.Equal("C:\\proj\\SimpleProject.ap21", response!.ResolvedProjectPath);
    }

    [Fact]
    public void ResolvedProjectPath_DefaultsToNull()
    {
        const string json = """{"success":true,"payload":"{}"}""";

        var response = System.Text.Json.JsonSerializer.Deserialize<WorkerResponse>(
            json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        Assert.Null(response!.ResolvedProjectPath);
    }
```

If `WorkerResponseJsonTests.cs` already declares serializer options as a helper, reuse it instead of
inlining new options.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerResponseJsonTests`

Expected: FAIL to compile — `WorkerResponse` has no `ResolvedProjectPath`.

- [ ] **Step 3: Add the contract property**

In `TiaMcpServer.Contracts/WorkerResponse.cs`, after `Warnings`:

```csharp
    /// <summary>
    /// Absolute path of the project the worker actually operated on, or null when no project was
    /// attached. This is ground truth for session binding: the host binds to THIS, never to the
    /// path the caller requested, so a mistyped-but-real path cannot silently retarget a session.
    /// </summary>
    public string? ResolvedProjectPath { get; set; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerResponseJsonTests`

Expected: PASS.

- [ ] **Step 5: Expose the current path on the session**

In `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs`, the private helper
`TryReadCurrentProjectPath()` at line 113 becomes publicly reachable. Add alongside it:

```csharp
    /// <summary>Absolute path of the attached project, or null when nothing is attached.</summary>
    public string? CurrentProjectPath => TryReadCurrentProjectPath();
```

Leave `TryReadCurrentProjectPath` private; the property is the public surface.

Naming note: worker `Program.cs:6` declares `using WorkerTiaPortalSession = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;`.
`WorkerTiaPortalSession` and `TiaPortalSession` are the **same type** under two names — adding the
property to `TiaPortalSession` makes it available on `WorkerTiaPortalSession` in `Program.cs`.

- [ ] **Step 6: Stamp the response at the dispatch choke point**

In `TiaMcpServer.OpennessWorker/Program.cs`, the `Execute` helper (around line 600) is the single
place every response passes through. Stamp there:

```csharp
    private static WorkerResponse Execute(Func<WorkerResponse> body)
    {
        try
        {
            return Stamp(body());
        }
        catch (EngineeringException ex)
        {
            return Failure($"TIA Portal operation failed: {ex.Message}");
        }
        // ... remaining catch clauses unchanged ...
    }

    /// <summary>
    /// Records which project the worker actually operated on. Stamped in one place so all
    /// operations report it without each remembering to.
    /// </summary>
    private static WorkerResponse Stamp(WorkerResponse response)
    {
        if (response.Success)
        {
            response.ResolvedProjectPath = _sharedSession.CurrentProjectPath;
        }

        return response;
    }
```

Keep the existing catch clauses exactly as they are — failures carry no resolved path.

- [ ] **Step 7: Teach FakeWorker to report a resolved path**

In `TiaMcpServer.FakeWorker/Program.cs`, add a scenario before `default:` so Task 4 has something to
drive:

```csharp
        case "ok-with-resolved-path":
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\resolved\\Ground.ap21"}""");
            break;
```

- [ ] **Step 8: Build and run the full suite**

Run:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: report the resolved project path from the Openness worker"
```

---

### Task 4: Host binds to the resolved path, not the requested one

Today `TryResolve` adopts the first explicit path unconditionally
(`ProjectSessionBinding.cs:61-66`), and a session that never passes `projectPath` never binds at all
— which is the workflow `tia-mcp doctor` recommends. Make resolution non-mutating and bind after
success from the worker's ground truth.

**Files:**
- Modify: `TiaMcpServer/Worker/WorkerCallResult.cs`
- Modify: `TiaMcpServer.Contracts/ProjectSessionBinding.cs:49-76`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs:627-687`
- Test: `TiaMcpServer.Tests/ProjectSessionBindingTests.cs` (rewrite affected cases), `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Interfaces:**
- Consumes: `WorkerResponse.ResolvedProjectPath` (Task 3), FakeWorker scenario `"ok-with-resolved-path"` (Task 3).
- Produces:
  - `WorkerCallResult.ResolvedProjectPath` — `string?`, **init-only property**, defaults to null.
  - `ProjectSessionBinding.TryResolve` no longer mutates. Signature unchanged.

**Expected `TryResolve` behaviour after this task:**

| `BoundProjectPath` | `requestedProjectPath` | `effectiveProjectPath` | binds? | result |
|---|---|---|---|---|
| null | null | null | no | true |
| null | `X` | `X` | **no** (was: yes) | true |
| `A` | null | `A` | no | true |
| `A` | `A` | `A` | no | true |
| `A` | `B` | null | no | false, already-bound error |

- [ ] **Step 1: Write the failing tests**

Add to `TiaMcpServer.Tests/ProjectSessionBindingTests.cs`:

```csharp
    [Fact]
    public void TryResolve_DoesNotAdoptTheRequestedPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\a.ap21", out var effective, out var error));

        Assert.Equal("C:\\a.ap21", effective);
        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void TryResolve_LeavesSessionUnboundSoASecondDifferentPathIsStillAccepted()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\a.ap21", out _, out _));
        Assert.True(binding.TryResolve("C:\\b.ap21", out var effective, out var error));

        Assert.Equal("C:\\b.ap21", effective);
        Assert.Null(error);
    }

    [Fact]
    public void TryResolve_StillRejectsADifferentPathOnceBound()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\a.ap21", forceRebind: false, out _));

        Assert.False(binding.TryResolve("C:\\b.ap21", out _, out var error));

        Assert.Contains("already bound", error);
        Assert.Contains("forceRebind=true", error);
    }
```

Add to `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`:

```csharp
    [Fact]
    public async Task UnboundSession_BindsToTheWorkerReportedPathAfterSuccess()
    {
        var binding = new ProjectSessionBinding(null);
        using var boundClient = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: LocateFakeWorker());

        var result = await boundClient.GetProjectStatusAsync("ok-with-resolved-path");

        Assert.True(result.Success);
        Assert.Equal("C:\\resolved\\Ground.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public async Task FailedCall_LeavesTheSessionUnbound()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: LocateFakeWorker());

        var result = await client.GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Null(binding.BoundProjectPath);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"`

Expected: FAIL. `TryResolve_DoesNotAdoptTheRequestedPath` fails because `BoundProjectPath` is
`"C:\a.ap21"`, and `UnboundSession_BindsToTheWorkerReportedPathAfterSuccess` fails because
`BoundProjectPath` is `"ok-with-resolved-path"`.

Existing tests that assert adopt-on-resolve will also fail. That is expected — they encode the bug.
Rewrite them to the table above rather than deleting them.

- [ ] **Step 3: Carry the resolved path on `WorkerCallResult`**

In `TiaMcpServer/Worker/WorkerCallResult.cs`, add an init-only property to the record body. Do **not**
add a fifth positional parameter — that would force every `Ok`/`Fail` call site to change.

```csharp
public sealed record WorkerCallResult(
    bool Success,
    string Payload,
    string? Error,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Project the worker actually operated on, when it reported one. Ground truth for session
    /// binding — see ProjectSessionBinding.
    /// </summary>
    public string? ResolvedProjectPath { get; init; }

    // ... existing Ok / Fail / ToText members unchanged ...
}
```

- [ ] **Step 4: Make `TryResolve` non-mutating**

In `TiaMcpServer.Contracts/ProjectSessionBinding.cs`, replace the `_boundProjectPath is null` branch
(lines 61-66) so it resolves without binding:

```csharp
        if (_boundProjectPath is null)
        {
            // Deliberately does NOT adopt: a mistyped-but-real path must not retarget the session.
            // OpennessWorkerClient binds after the call succeeds, using the worker-reported path.
            effectiveProjectPath = requested;
            return true;
        }
```

- [ ] **Step 5: Propagate and adopt in the client**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `InvokeWorkerAsync` must carry the value through.
Change the success branch:

```csharp
            return response.Success
                ? WorkerCallResult.Ok(response.Payload ?? string.Empty, warnings) with
                    {
                        ResolvedProjectPath = response.ResolvedProjectPath
                    }
                : WorkerCallResult.Fail(
                    response.Error ?? "The TIA Openness worker failed without an error message.",
                    warnings);
```

Then replace the provisional-clear block in `SendBoundProjectRequestAsync` (lines 646-652) with
adoption:

```csharp
        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        if (result.Success && sessionWasUnbound && result.ResolvedProjectPath is not null)
        {
            // Bind to what the worker actually operated on, never to what the caller asked for.
            _projectSessionBinding.Bind(result.ResolvedProjectPath, forceRebind: true, out _);
        }
```

The old comment about provisional bindings and the `Clear(effectiveProjectPath, out _)` call are
deleted — nothing is bound provisionally any more, so there is nothing to roll back.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"`

Expected: PASS.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests`

Expected: all tests pass. Any remaining failure is an old test encoding adopt-on-resolve — rewrite it
against the table in this task's header.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "fix: bind the MCP session to the worker-reported project, not the requested path"
```

---

### Task 5: Read operations never open a project alongside another

`WithProject` (`TiaMcpServer.OpennessWorker/Program.cs:573-591`) calls `session.OpenProject(...)` for
every read tool, and `TiaPortalSession.cs:99-107` then opens the requested project *alongside* the
user's. Live, only TIA Portal's own refusal stopped it. The policy goes in `Contracts` as a pure
function so it is unit-testable — the net8.0 test project cannot link Siemens-referencing worker files.

`TiaPortalSession.OpenProject` is **not** changed: the alongside branch stays reachable from
`open_project`, which is token-gated.

**Files:**
- Create: `TiaMcpServer.Contracts/ProjectOpenPolicy.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs:166-173` (`SearchEquipmentCatalog`), `:573-591` (`WithProject`)
- Test: `TiaMcpServer.Tests/ProjectOpenPolicyTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public enum ProjectOpenDecision { UseAttached, OpenRequested, Refuse }`
  - `public static ProjectOpenDecision ProjectOpenPolicy.Decide(string? currentPath, string? requestedPath)`
  - `public static string ProjectOpenPolicy.RefusalMessage(string currentPath, string requestedPath)`

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/ProjectOpenPolicyTests.cs`:

```csharp
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectOpenPolicyTests
{
    [Fact]
    public void NothingAttached_NoRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide(null, null));

    [Fact]
    public void NothingAttached_WithRequest_OpensIt()
        => Assert.Equal(ProjectOpenDecision.OpenRequested, ProjectOpenPolicy.Decide(null, "C:\\a.ap21"));

    [Fact]
    public void Attached_NoRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", null));

    [Fact]
    public void Attached_SameRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", "C:\\a.ap21"));

    [Fact]
    public void Attached_SameRequestDifferentCase_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\A.ap21", "c:\\a.AP21"));

    [Fact]
    public void Attached_DifferentRequest_Refuses()
        => Assert.Equal(ProjectOpenDecision.Refuse, ProjectOpenPolicy.Decide("C:\\a.ap21", "C:\\b.ap21"));

    [Fact]
    public void Attached_WhitespaceRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", "   "));

    [Fact]
    public void RefusalMessage_NamesBothProjectsAndTheEscapeHatch()
    {
        var message = ProjectOpenPolicy.RefusalMessage("C:\\a.ap21", "C:\\b.ap21");

        Assert.Contains("C:\\a.ap21", message);
        Assert.Contains("C:\\b.ap21", message);
        Assert.Contains("open_project", message);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ProjectOpenPolicyTests`

Expected: FAIL to compile — `ProjectOpenPolicy` does not exist.

- [ ] **Step 3: Write the policy**

Create `TiaMcpServer.Contracts/ProjectOpenPolicy.cs`. Remember: netstandard2.0, no dependencies.

```csharp
using System;
using System.IO;

namespace TiaMcpServer.Contracts;

public enum ProjectOpenDecision
{
    /// <summary>Operate on whatever is already attached.</summary>
    UseAttached,

    /// <summary>Nothing is attached; opening the requested project cannot clobber anything.</summary>
    OpenRequested,

    /// <summary>A different project is attached; refuse rather than open one alongside it.</summary>
    Refuse
}

/// <summary>
/// Decides whether a non-lifecycle operation may cause TIA Portal to open a project. Read
/// operations must never open a second project alongside one the user already has open — live
/// testing against V21 showed a read tool doing exactly that, stopped only by TIA Portal's own
/// refusal. Pure so the net8.0 test project can cover it; the worker is net48 and references
/// Siemens assemblies the tests cannot load.
/// </summary>
public static class ProjectOpenPolicy
{
    public static ProjectOpenDecision Decide(string? currentPath, string? requestedPath)
    {
        var requested = Normalize(requestedPath);
        if (requested is null)
        {
            return ProjectOpenDecision.UseAttached;
        }

        var current = Normalize(currentPath);
        if (current is null)
        {
            return ProjectOpenDecision.OpenRequested;
        }

        return string.Equals(current, requested, StringComparison.OrdinalIgnoreCase)
            ? ProjectOpenDecision.UseAttached
            : ProjectOpenDecision.Refuse;
    }

    public static string RefusalMessage(string currentPath, string requestedPath)
        => $"TIA Portal currently has project '{currentPath}' open, but this request targets "
            + $"'{requestedPath}'. Read operations never switch projects. Omit projectPath to use "
            + "the open project, or call open_project to switch.";

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path!.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a resolvable path (a FakeWorker scenario keyword, for instance). Compare literally.
            return trimmed;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ProjectOpenPolicyTests`

Expected: PASS, 8 tests.

- [ ] **Step 5: Apply the policy in `WithProject`**

In `TiaMcpServer.OpennessWorker/Program.cs`, replace the body of `WithProject` (lines 573-591):

```csharp
    /// <summary>Opens an Openness session, ensures a project is available, then runs <paramref name="body"/>.</summary>
    private static WorkerResponse WithProject(WorkerRequest request, Func<Project, WorkerResponse> body)
    {
        return WithSession(request, session =>
        {
            session.EnsureConnected();

            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }

            if (session.Project is null)
            {
                return Failure("No project is open. Provide a projectPath argument or open a project in TIA Portal.");
            }

            return body(session.Project);
        });
    }

    /// <summary>
    /// Applies <see cref="ProjectOpenPolicy"/> before any non-lifecycle operation may open a
    /// project. Returns null to continue, or the failure response to return to the host.
    /// </summary>
    private static WorkerResponse? EnsureRequestedProjectOpen(WorkerTiaPortalSession session, string? requestedProjectPath)
    {
        var currentPath = session.CurrentProjectPath;
        switch (ProjectOpenPolicy.Decide(currentPath, requestedProjectPath))
        {
            case ProjectOpenDecision.OpenRequested:
                session.OpenProject(requestedProjectPath!);
                return null;
            case ProjectOpenDecision.Refuse:
                return Failure(ProjectOpenPolicy.RefusalMessage(currentPath!, requestedProjectPath!));
            default:
                return null;
        }
    }
```

- [ ] **Step 6: Apply the policy in `SearchEquipmentCatalog`**

`SearchEquipmentCatalog` uses `WithSession` and opens the project itself. Replace lines 170-173:

```csharp
            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }
```

- [ ] **Step 7: Confirm no other unguarded open remains**

Run: `grep -n "session.OpenProject" TiaMcpServer.OpennessWorker/Program.cs`

Expected: exactly one match, inside `EnsureRequestedProjectOpen`. The two matches in
`ProjectLifecycleService.cs` and the one in `TiaPortalSession.cs` are the token-gated lifecycle path
and are intentionally untouched.

- [ ] **Step 8: Build and run the full suite**

Run:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "fix: stop read operations opening a project alongside a user-opened one"
```

---

### Task 6: Declare each operation's optional field surface

`BatchOperationSpec` carries only `RequiredFields`. Each operation's *optional* surface exists solely
as `[Description]` prose, which nothing checks against `BatchWorkerInvoker`. That is how
`deviceItemName` came to be described unscoped while only `add_network_device` forwards it.

The tables below are transcribed directly from `BatchWorkerInvoker.InvokeAsync`
(`BatchWorkerInvoker.cs:32-64`) — one line per operation, the authoritative forwarding map. The
universal fields `operationId`, `operation`, and `projectPath` are excluded from every table.

**Files:**
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs:11-14,34,224-259`
- Test: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `BatchOperationSpec(string Name, BatchOperationCategory Category, IReadOnlyList<string> RequiredFields, IReadOnlyList<string> OptionalFields)`
  - `public static IReadOnlyCollection<BatchOperationSpec> BatchOperationCatalog.All`
  - Field names are camelCase strings matching `BatchOperationRequest` property names with a lowercase first letter (`blockPath`, `obEventClass` for `OBEventClass`).

- [ ] **Step 1: Write the failing test**

Add to `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`:

```csharp
    [Fact]
    public void All_ExposesEverySpec()
    {
        // 8 reads + 17 writes.
        Assert.Equal(25, BatchOperationCatalog.All.Count);
    }

    [Fact]
    public void ConfigureNetworkDevice_DoesNotDeclareDeviceItemName()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("configure_network_device", out var spec));

        Assert.DoesNotContain("deviceItemName", spec!.OptionalFields);
        Assert.Contains("ipAddress", spec.OptionalFields);
        Assert.Contains("subnetMask", spec.OptionalFields);
        Assert.Contains("pnDeviceName", spec.OptionalFields);
        Assert.Contains("subnetName", spec.OptionalFields);
        Assert.Contains("ioSystemName", spec.OptionalFields);
    }

    [Fact]
    public void AddNetworkDevice_DeclaresDeviceItemName()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("add_network_device", out var spec));

        Assert.Contains("deviceItemName", spec!.OptionalFields);
    }

    [Fact]
    public void CreateTag_DoesNotDeclareTheExternalAttributes()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("create_tag", out var spec));

        Assert.DoesNotContain("externalAccessible", spec!.OptionalFields);
        Assert.DoesNotContain("externalVisible", spec.OptionalFields);
        Assert.DoesNotContain("externalWritable", spec.OptionalFields);
        Assert.DoesNotContain("isSafety", spec.OptionalFields);
    }

    [Fact]
    public void UpdateTag_DeclaresTheExternalAttributes()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("update_tag", out var spec));

        Assert.Contains("externalAccessible", spec!.OptionalFields);
        Assert.Contains("externalVisible", spec.OptionalFields);
        Assert.Contains("externalWritable", spec.OptionalFields);
        Assert.Contains("isSafety", spec.OptionalFields);
        Assert.Contains("newName", spec.OptionalFields);
    }

    [Fact]
    public void NoSpecDeclaresAUniversalFieldAsOptional()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            Assert.DoesNotContain("operationId", spec.OptionalFields);
            Assert.DoesNotContain("operation", spec.OptionalFields);
            Assert.DoesNotContain("projectPath", spec.OptionalFields);
        }
    }

    [Fact]
    public void RequiredAndOptionalFieldsNeverOverlap()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            Assert.Empty(spec.RequiredFields.Intersect(spec.OptionalFields));
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchOperationCatalogTests`

Expected: FAIL to compile — `BatchOperationSpec` has no `OptionalFields` and the catalog has no `All`.

- [ ] **Step 3: Extend the spec record and expose `All`**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`:

```csharp
public sealed record BatchOperationSpec(
    string Name,
    BatchOperationCategory Category,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields);
```

and, next to `ReadOperationNames`:

```csharp
    /// <summary>Every registered spec. Used by the field-forwarding invariant test.</summary>
    public static IReadOnlyCollection<BatchOperationSpec> All { get; } = Specs.Values.ToArray();
```

- [ ] **Step 4: Fill in the tables**

Replace the `specs` array in `BuildSpecs()` (lines 226-256). Every `OptionalFields` entry below is
the set of non-universal parameters that operation's line in `BatchWorkerInvoker.InvokeAsync`
actually passes to the client.

```csharp
        var specs = new[]
        {
            // Reads
            new BatchOperationSpec("browse_project_tree", BatchOperationCategory.Read, None, new[] { "depth", "startPath" }),
            new BatchOperationSpec("read_hardware_config", BatchOperationCategory.Read, None, None),
            new BatchOperationSpec("search_equipment_catalog", BatchOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            new BatchOperationSpec("read_cross_references", BatchOperationCategory.Read, None, new[] { "plcName", "filter", "maxResults" }),
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, None),
            new BatchOperationSpec("list_tag_tables", BatchOperationCategory.Read, None, new[] { "plcName" }),
            new BatchOperationSpec("compile_check", BatchOperationCategory.Read, None, new[] { "blockPath", "plcName" }),
            new BatchOperationSpec("get_project_status", BatchOperationCategory.Read, None, None),

            // Data writes
            new BatchOperationSpec("update_block_logic", BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }, None),
            new BatchOperationSpec("create_tag_table", BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("delete_tag_table", BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("create_tag", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress" }),
            new BatchOperationSpec("update_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "isSafety" }),
            new BatchOperationSpec("delete_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("create_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("update_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
            new BatchOperationSpec("delete_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("add_network_device", BatchOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            new BatchOperationSpec("configure_network_device", BatchOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" }),
            new BatchOperationSpec("create_block", BatchOperationCategory.Write, new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
            new BatchOperationSpec("delete_block", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("create_block_group", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("delete_block_group", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("start_plc", BatchOperationCategory.Write, None, new[] { "plcName" }),
            new BatchOperationSpec("stop_plc", BatchOperationCategory.Write, None, new[] { "plcName" }),
        };
```

That is 8 read specs and 17 write specs, 25 in total — matching the `All_ExposesEverySpec` assertion
in Step 1. If you add or remove a spec, update that assertion in the same commit.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchOperationCatalogTests`

Expected: PASS.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests`

Expected: all tests pass. Validation behaviour has not changed yet — this task only declares.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: declare each batch operation's optional field surface in the catalog"
```

---

### Task 7: Reject fields that the chosen operation ignores

With the surface declared, a field set on an operation that never forwards it can be an error instead
of a silent drop. `ValidateBounds` already does this for exactly three fields (`depth`, `startPath`,
`maxResults`); the generic check replaces those three, keeping the range checks.

This makes `deviceItemName` on `configure_network_device` an error, and likewise
`externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` on `create_tag` — honest about
`BatchWorkerInvoker.cs:48`, which discards all four today.

**Files:**
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs` (`Validate`, `ValidateBounds`)
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs:88`
- Test: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`

**Interfaces:**
- Consumes: `BatchOperationSpec.OptionalFields`, `BatchOperationCatalog.All` (Task 6).
- Produces: `ValidateReadBatch`/`ValidateWriteBatch` reject inapplicable fields, aggregated with all other errors (one error line per offending field).

- [ ] **Step 1: Write the failing test**

Add to `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`:

```csharp
    [Fact]
    public void DeviceItemNameOnConfigureNetworkDevice_IsRejected()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "configure_network_device",
                DeviceName = "PLC_1",
                DeviceItemName = "PROFINET interface_1"
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("deviceItemName", result.Error);
        Assert.Contains("configure_network_device", result.Error);
        Assert.Contains("ipAddress", result.Error);
    }

    [Fact]
    public void ExternalAttributesOnCreateTag_AreRejected()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "create_tag",
                TableName = "Default tag table",
                Name = "Motor",
                DataType = "Bool",
                ExternalAccessible = true,
                IsSafety = false
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("externalAccessible", result.Error);
        Assert.Contains("isSafety", result.Error);
    }

    [Fact]
    public void ExternalAttributesOnUpdateTag_AreAccepted()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "update_tag",
                TableName = "Default tag table",
                Name = "Motor",
                ExternalAccessible = true,
                IsSafety = false
            }
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InapplicableFieldErrors_AggregateWithOtherErrors()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "create_tag",
                TableName = "Default tag table",
                ExternalAccessible = true
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("missing required field(s)", result.Error);
        Assert.Contains("externalAccessible", result.Error);
    }

    [Fact]
    public void UniversalFields_AreNeverRejected()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "get_project_status",
                ProjectPath = "C:\\p.ap21"
            }
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DepthOnANonTreeOperation_IsStillRejected()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "read_hardware_config", Depth = 2 }
        });

        Assert.False(result.IsValid);
        Assert.Contains("depth", result.Error);
    }

    [Fact]
    public void DepthBelowOne_IsStillRejected()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", Depth = 0 }
        });

        Assert.False(result.IsValid);
        Assert.Contains("1 or greater", result.Error);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchOperationCatalogTests`

Expected: FAIL. `DeviceItemNameOnConfigureNetworkDevice_IsRejected`,
`ExternalAttributesOnCreateTag_AreRejected`, and
`InapplicableFieldErrors_AggregateWithOtherErrors` fail because validation currently accepts those
fields. The `Depth` tests still pass — they cover the existing `ValidateBounds` behaviour that must
survive.

- [ ] **Step 3: Add the reflection-backed field reader**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`, add near the other private statics. Every
`BatchOperationRequest` field is nullable, so "non-null" is a sufficient "was it set?" test. The
reflection result is computed once.

```csharp
    private static readonly IReadOnlySet<string> UniversalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "operationId",
        "operation",
        "projectPath",
    };

    // Cached once: every settable field on the flat request DTO, keyed by its camelCase wire name.
    private static readonly (string Name, Func<BatchOperationRequest, bool> IsSet)[] AllRequestFields =
        typeof(BatchOperationRequest)
            .GetProperties()
            .Select(property => (
                Name: char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1),
                IsSet: new Func<BatchOperationRequest, bool>(op => property.GetValue(op) is not null)))
            .ToArray();

    private static IEnumerable<string> FindInapplicableFields(BatchOperationRequest op, BatchOperationSpec spec)
    {
        foreach (var field in AllRequestFields)
        {
            if (UniversalFields.Contains(field.Name) ||
                spec.RequiredFields.Contains(field.Name) ||
                spec.OptionalFields.Contains(field.Name) ||
                !field.IsSet(op))
            {
                continue;
            }

            yield return field.Name;
        }
    }
```

`OperationId` and `Operation` are non-nullable `string` defaulting to `string.Empty`, so they are
always "set"; `UniversalFields` short-circuits them before that matters.

- [ ] **Step 4: Wire the check into `Validate`**

In `Validate`, immediately after the existing missing-required-fields block (line 116) and before the
`ValidateBounds` loop:

```csharp
            foreach (var field in FindInapplicableFields(op, spec))
            {
                var valid = spec.OptionalFields.Count > 0
                    ? string.Join(", ", spec.OptionalFields)
                    : "(none)";
                errors.Add(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}'): '{field}' is not valid for "
                    + $"{op.Operation}. Valid optional fields: {valid}.");
            }
```

- [ ] **Step 5: Drop the three now-redundant `ValidateBounds` checks**

The generic check covers `depth`, `startPath`, and `maxResults` on the wrong operation, and would
otherwise emit a second error for the same mistake. In `ValidateBounds`, delete the `isTree`,
`takesMaxResults` locals and the three `yield return` blocks that use them, keeping only the range
checks:

```csharp
    private static IEnumerable<string> ValidateBounds(BatchOperationRequest op)
    {
        // Scope ("'depth' is only valid for browse_project_tree") is enforced generically by
        // FindInapplicableFields via BatchOperationSpec.OptionalFields. Only ranges remain here.
        if (op.Depth is < 1)
        {
            yield return "'depth' must be 1 or greater.";
        }

        if (op.MaxResults is < 1)
        {
            yield return "'maxResults' must be 1 or greater.";
        }
    }
```

- [ ] **Step 6: Scope the `deviceItemName` description**

In `TiaMcpServer/Batch/BatchOperationRequest.cs`, replace the attribute on line 88:

```csharp
    [Description("Optional device item name for add_network_device; defaults to deviceName when omitted. Not valid for configure_network_device.")]
    public string? DeviceItemName { get; set; }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchOperationCatalogTests`

Expected: PASS.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests`

Expected: all tests pass. If an existing test asserts the old `"'depth' is only valid for
browse_project_tree."` wording, update it to the new generic message — the behaviour (rejection) is
unchanged, only the text.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "fix: reject batch fields the chosen operation would silently discard"
```

---

### Task 8: Prove declared fields actually reach the worker

Tasks 6 and 7 declare and enforce the field surface, but nothing yet proves the declaration matches
what `BatchWorkerInvoker` forwards. This test drives the real pipeline — `BatchWorkerInvoker` →
`OpennessWorkerClient` → FakeWorker — and fails if any declared field is dropped en route.

It tests behaviour, not source text, so it survives the deferred 3.3 refactor and will validate it
when it lands.

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Test: `TiaMcpServer.Tests/BatchFieldForwardingTests.cs` (create)

**Interfaces:**
- Consumes: `BatchOperationCatalog.All`, `BatchOperationSpec.OptionalFields` (Task 6); `BatchWorkerInvoker.InvokeAsync` (existing).
- Produces: nothing consumed downstream.

- [ ] **Step 1: Add the echo scenario to FakeWorker**

FakeWorker already selects its scenario from the request's `projectPath`
(`TiaMcpServer.FakeWorker/Program.cs:11-24`). Reuse that idiom — an environment variable would be
process-global and could leak between xunit classes running in parallel.

Add before `default:`:

```csharp
        case "echo":
            // Returns the received request verbatim so tests can assert which fields survived
            // the BatchOperationRequest -> WorkerRequest hop.
            Respond(JsonSerializer.Serialize(new { success = true, payload = line }));
            break;
```

`line` is the raw request JSON; serializing it as a string value escapes it correctly.

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/BatchFieldForwardingTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Every field an operation declares must actually reach the worker. Two live instances of the
/// opposite — deviceItemName on configure_network_device, and the external* attributes on
/// create_tag — were silently discarded for months because nothing checked. Asserts by VALUE, not
/// by property name, so renaming either side of the boundary cannot make this pass vacuously.
/// </summary>
public class BatchFieldForwardingTests
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

    // This test calls BatchWorkerInvoker directly, so BatchOperationCatalog.Validate is NOT in the
    // path — but OpennessWorkerClient validates some values itself (see the filter check in
    // ReadCrossReferencesAsync). Those fields carry a real allowed value; a sentinel string would
    // be rejected before reaching the worker. The assertion still checks that the value arrives.
    private static readonly Dictionary<string, object> ValidatedFieldValues = new(StringComparer.Ordinal)
    {
        ["filter"] = "UnusedObjects",
        ["blockType"] = "FB",
        ["language"] = "SCL",
        ["obEventClass"] = "CyclicInterrupt",
        ["dataType"] = "Bool",
        ["depth"] = 7,
        ["maxResults"] = 4242,
    };

    private static object SentinelFor(PropertyInfo property, string fieldName)
    {
        if (ValidatedFieldValues.TryGetValue(fieldName, out var known))
        {
            return known;
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(int))
        {
            return 4243;
        }

        return $"__sentinel_{fieldName}__";
    }

    private static (BatchOperationRequest Request, List<object> Expected) Build(BatchOperationSpec spec)
    {
        var request = new BatchOperationRequest
        {
            OperationId = "item-1",
            Operation = spec.Name,
            ProjectPath = "echo"
        };

        var expected = new List<object>();
        foreach (var fieldName in spec.RequiredFields.Concat(spec.OptionalFields))
        {
            var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
            var property = typeof(BatchOperationRequest).GetProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Spec '{spec.Name}' declares field '{fieldName}' with no matching BatchOperationRequest property.");

            var value = SentinelFor(property, fieldName);
            property.SetValue(request, value);
            expected.Add(value);
        }

        return (request, expected);
    }

    public static TheoryData<string> AllOperations()
    {
        var data = new TheoryData<string>();
        foreach (var spec in BatchOperationCatalog.All)
        {
            data.Add(spec.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task EveryDeclaredField_ReachesTheWorker(string operationName)
    {
        Assert.True(BatchOperationCatalog.TryGetSpec(operationName, out var spec));
        var (request, expected) = Build(spec!);

        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: LocateFakeWorker());

        var result = await BatchWorkerInvoker.InvokeAsync(client, request);

        Assert.True(result.Success, result.Error);

        foreach (var value in expected)
        {
            var rendered = value switch
            {
                bool b => b ? "true" : "false",
                int i => i.ToString(),
                _ => JsonSerializer.Serialize(value).Trim('"')
            };

            Assert.True(
                result.Payload.Contains(rendered, StringComparison.Ordinal),
                $"Operation '{operationName}' declares a field whose value '{rendered}' never reached "
                + $"the worker. Echoed request: {result.Payload}");
        }
    }

    [Fact]
    public void EveryDeclaredField_HasAMatchingRequestProperty()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            foreach (var fieldName in spec.RequiredFields.Concat(spec.OptionalFields))
            {
                var propertyName = char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);
                Assert.NotNull(typeof(BatchOperationRequest).GetProperty(propertyName));
            }
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchFieldForwardingTests
```

Expected: FAIL before Step 1 is applied (no `echo` scenario → `unknown scenario 'echo'`). After
Step 1, all cases should pass, because Task 6's tables were transcribed from the forwarding map.

**If a case fails here, that is a real finding, not a test bug.** It means Task 6's table declares a
field the invoker does not forward. Fix by either removing it from `OptionalFields` or forwarding it
in `BatchWorkerInvoker` — and record which you chose in the commit message.

- [ ] **Step 4: Handle the `obEventClass` naming check**

`BatchOperationRequest.OBEventClass` does not follow the `Xxx` → `xxx` convention — naive
lower-casing of the first character yields `oBEventClass`, and `char.ToUpperInvariant` on
`obEventClass` yields `ObEventClass`, neither of which resolves.

Confirm which name the catalog and the reflection helper agree on by running:

```powershell
dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchFieldForwardingTests.EveryDeclaredField_HasAMatchingRequestProperty
```

If it fails, resolve it by renaming the property to `ObEventClass` in
`TiaMcpServer/Batch/BatchOperationRequest.cs:113` and updating the single reference in
`BatchWorkerInvoker.cs:56` (`op.OBEventClass` → `op.ObEventClass`). The JSON wire name is
`obEventClass` either way under `JsonNamingPolicy.CamelCase`, so no client-visible change — verify
with the existing `BatchOperationRequestJsonTests`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~BatchFieldForwardingTests`

Expected: PASS, one case per catalog operation plus the naming check.

- [ ] **Step 6: Run the full suite**

Run:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "test: prove every declared batch field reaches the worker"
```

---

### Task 9: Update the improvement plan and README

The plan document is the project's running record of what is done and what is outstanding. Leaving it
stale is how items get re-litigated.

**Files:**
- Modify: `docs/IMPROVEMENT_PLAN.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: outcomes of Tasks 1-8.
- Produces: nothing consumed downstream.

- [ ] **Step 1: Mark the completed items**

In `docs/IMPROVEMENT_PLAN.md`:

- Row `3.5b`: append `— DONE 2026-07-20 (Round 4, Task 2)`.
- Row `3.6` follow-ups section: mark the `deviceItemName` bullet resolved by catalog rejection (Task 7), and note that `externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` on `create_tag` now **error** rather than being silently dropped, with the forwarding decision still pending hardware.
- The "Session binding does not protect the default case" section: record that the chosen fix was worker-reported ground truth plus a read-side open policy, and that it is DONE.
- The "test suite writes into the production audit trail" section: record DONE via 3.5b, and delete the interim-mitigation paragraph — it no longer applies.
- The `TiaJson.Presentation.MakeReadOnly()` follow-up: mark DONE.
- Leave the `BatchPayloadBudget` collapse and 3.3 follow-ups untouched — still deferred.

- [ ] **Step 2: Add the next-round note**

Add to `## Deferred / explicitly not planned`:

```markdown
- **Next round (needs TIA Portal hardware):** forward `externalAccessible`/`externalVisible`/
  `externalWritable`/`isSafety` on `create_tag` if Openness V21 permits setting them at tag-creation
  time — Round 4 narrowed this to that single question by making the fields an explicit error
  instead of a silent drop. Same session should verify the `NetworkDeviceConfigurator`
  "UNVERIFIED SDK CALL" reflection paths and decide whether `deviceItemName` is meaningful for
  `configure_network_device`.
```

- [ ] **Step 3: Update the README where behaviour changed**

Two user-visible changes need documenting:

- Read operations now refuse to open a project while a different one is open. Document the message and that `open_project` is the way to switch.
- A session binds to the active project after its first successful call, so a later call naming a different project is rejected. Mention `forceRebind`.

Find the section covering project binding:

```bash
grep -n "forceRebind\|binding\|projectPath" README.md
```

- [ ] **Step 4: Verify no stale claims remain**

Run: `grep -n "Shared\|alongside" README.md docs/IMPROVEMENT_PLAN.md`

Expected: no references to `WriteSafetyService.Shared`, and no documentation claiming projects open alongside one another.

- [ ] **Step 5: Commit**

```bash
git add docs/IMPROVEMENT_PLAN.md README.md
git commit -m "docs: record Round 4 outcomes and the hardware-gated next round"
```

---

## Final Verification

- [ ] Run the full build and suite one last time:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests
```

Expected: build succeeds, all tests pass (341 baseline plus roughly 45 new).

- [ ] Confirm the statics and hazards are gone:

```bash
grep -rn "WriteSafetyService.Shared" --include=*.cs .
grep -n "session.OpenProject" TiaMcpServer.OpennessWorker/Program.cs
```

Expected: no matches for the first; exactly one match for the second, inside `EnsureRequestedProjectOpen`.

- [ ] Confirm the audit directory was not touched by the test run. Note the file count in
`%LOCALAPPDATA%\TiaMcpServer\audit` before and after `dotnet test`:

```powershell
(Get-ChildItem "$env:LOCALAPPDATA\TiaMcpServer\audit" -ErrorAction SilentlyContinue).Count
```

Expected: unchanged across a full test run. This is the concrete outcome Task 2 exists for.

## What This Plan Does Not Cover

Recorded so the next session does not have to rediscover the boundary:

- **Item D** — forwarding `externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` on `create_tag`. Needs Openness V21 to answer whether they are settable at creation time. Round 4 turns the silent drop into an error, which is the honest interim state.
- **Item I** — `NetworkDeviceConfigurator` "UNVERIFIED SDK CALL" reflection paths. Needs hardware.
- **Item F (3.3)** — collapsing the `BatchWorkerInvoker` → `OpennessWorkerClient` double dispatch (~250 lines). Design retained in `docs/superpowers/specs/2026-07-20-phase3-simplification-design.md`. Task 8's echo test will validate it when it lands.
- **Item G** — collapsing `BatchPayloadBudget.ReadBatchResponseLength` into `BatchResultFormatter.ReadBatch`. Needs a measurement of the extra serialization per budget probe first.
