# Project Enumeration Completeness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `read_hardware_config` and `browse_project_tree` discover devices in nested device groups, and make `browse_project_tree` return the complete PLC system-block hierarchy with explicit non-provenance semantics.

**Architecture:** Add one net48 worker-local `ProjectDeviceEnumerator` consumed by both readers, then keep user-block and system-block recursion type-correct behind a shared block-node builder. Preserve the current public request contracts, flat device presentation, best-effort diagnostics, and unpaged result-budget behavior; validate public shapes through FakeWorker while reserving real Openness claims for a separately authorized read-only run.

**Tech Stack:** C# 12 host/tests on .NET 8, C# worker on .NET Framework 4.8, Siemens TIA Portal V21 Openness APIs, xUnit, FakeWorker newline-delimited JSON IPC, PowerShell 7 for verification.

**Spec:** `docs/superpowers/specs/2026-08-28-issue-31-project-completeness-pagination-design.md` — this plan implements PR 1 only.

## Global Constraints

- Work only on `enhancement/project-enumeration-completeness`; pagination belongs to a later branch from updated `main`.
- Do not add `pageSize`, `cursor`, pagination metadata, a new structured envelope, or automatic partial results.
- `HardwareConfigReader` and `ProjectTreeWalker` must consume one shared ordered device enumerator.
- Enumerate direct devices first, then recursively enumerate `DeviceUserGroup.Devices` and `DeviceUserGroup.Groups` in TIA collection order.
- Grouped devices remain ordinary flat `Device` nodes; do not add `DeviceFolder` nodes.
- System folders use `nodeType: "SystemBlockFolder"`; contained blocks retain `OB`, `FB`, `FC`, `GlobalDB`, `InstanceDB`, `ArrayDB`, or `Block` and add `details.IsSystemBlock: "true"`.
- `IsSystemBlock` means hierarchy membership only. Do not infer Siemens, Safety, library, or author provenance and do not add `HeaderAuthor`.
- Preserve current best-effort behavior: hardware warnings stay in `HardwareConfigInfo.Messages`; project-tree per-item `EngineeringException` failures stay on stderr; `browse_project_tree` remains a bare array.
- Preserve the current unpaged 60,000-character per-result and 180,000-character batch-document behavior.
- Static source-contract tests and FakeWorker fixtures are not live Openness evidence.
- Do not run a live TIA project without separate authorization, and never mutate a project during this work.
- Each future implementation commit requires explicit commit authorization under repository policy; the commit commands below define review boundaries, not present authorization.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/ProjectDeviceEnumerator.cs` | Single ordered traversal of direct and recursively grouped devices. |
| `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs` | Select/filter devices from the shared traversal while preserving name evidence and messages. |
| `TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs` | Render every enumerated device and separately recurse user and system block groups. |
| `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs` | RED-capable source contracts for net48-only traversal patterns and shared-helper use. |
| `TiaMcpServer.FakeWorker/Program.cs` | Typed fixture for complete hardware and project-tree public shapes. |
| `TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs` | Structured hardware result assertion for the grouped-device fixture. |
| `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs` | Standalone tree assertion for flat grouped devices and system-block semantics. |
| `docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md` | Device-facing completeness and scope boundary. |
| `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md` | Project-tree node semantics and diagnostics boundary. |
| `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` | Complete hardware enumeration and the existing unpaged size limitation. |
| `docs/ARCHITECTURE.md` | Shared device-enumeration seam and separate system-group walker. |

---

### Task 1: Share recursive device enumeration

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/ProjectDeviceEnumerator.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs:93-108`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs:16-42`

**Interfaces:**
- Consumes: `Siemens.Engineering.Project.Devices`, `Project.DeviceGroups`, `DeviceUserGroup.Devices`, and `DeviceUserGroup.Groups`.
- Produces: `internal static IEnumerable<Device> ProjectDeviceEnumerator.Enumerate(Project project)` in direct-then-depth-first group order.
- Produces: `private static ProjectTreeNode ProjectTreeWalker.WalkDevice(Device device)` so direct and grouped devices have the same public shape.

- [ ] **Step 1: Write the failing shared-traversal source contracts**

Create `ProjectTraversalSourceContractTests.cs` with the repository-source helper and these tests:

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTraversalSourceContractTests
{
    [Fact]
    public void ProjectDeviceEnumerator_TraversesDirectAndNestedGroupsInOrder()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectDeviceEnumerator.cs");

        Assert.Contains("foreach (Device device in project.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup group in project.DeviceGroups)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in group.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (DeviceUserGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var device in Enumerate(childGroup))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareAndTreeReaders_UseTheSameProjectDeviceEnumerator()
    {
        var hardware = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs");
        var tree = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

        Assert.Contains("foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))", hardware, StringComparison.Ordinal);
        Assert.Contains("foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", hardware, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (Device device in project.Devices)", tree, StringComparison.Ordinal);
        Assert.Contains("rootNodes.Add(WalkDevice(device));", tree, StringComparison.Ordinal);
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(pathSegments)}'.");
    }
}
```

- [ ] **Step 2: Run the focused test and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests"
```

Expected: FAIL because `ProjectDeviceEnumerator.cs` does not exist and both readers still loop over `project.Devices` directly. Record the failing assertion or `FileNotFoundException`; do not treat compilation or restore failure as the behavioral RED.

- [ ] **Step 3: Add the minimal ordered enumerator**

Create `ProjectDeviceEnumerator.cs`:

```csharp
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class ProjectDeviceEnumerator
{
    public static IEnumerable<Device> Enumerate(Project project)
    {
        foreach (Device device in project.Devices)
        {
            yield return device;
        }

        foreach (DeviceUserGroup group in project.DeviceGroups)
        {
            foreach (var device in Enumerate(group))
            {
                yield return device;
            }
        }
    }

    private static IEnumerable<Device> Enumerate(DeviceUserGroup group)
    {
        foreach (Device device in group.Devices)
        {
            yield return device;
        }

        foreach (DeviceUserGroup childGroup in group.Groups)
        {
            foreach (var device in Enumerate(childGroup))
            {
                yield return device;
            }
        }
    }
}
```

- [ ] **Step 4: Make both readers consume the helper**

In `HardwareConfigReader.SelectDevices`, replace only the enumeration source and retain the existing one-read name evidence:

```csharp
var candidates = new List<(Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)>();
foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))
{
    var nameEvidence = ReadTypedIdentityString(() => device.Name, "Device name");
    candidates.Add((device, nameEvidence));
}
```

In `ProjectTreeWalker.Walk`, route all devices through one node builder:

```csharp
public List<ProjectTreeNode> Walk(Project project)
{
    var rootNodes = new List<ProjectTreeNode>();

    foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))
    {
        rootNodes.Add(WalkDevice(device));
    }

    return rootNodes;
}

private static ProjectTreeNode WalkDevice(Device device)
{
    var children = new List<ProjectTreeNode>();

    foreach (var plcSoftware in PlcSoftwareLocator.FindInDevice(device))
    {
        children.Add(WalkPlcSoftware(device.Name, plcSoftware));
    }

    return new ProjectTreeNode
    {
        Name = device.Name,
        NodeType = "Device",
        Details = new Dictionary<string, string>
        {
            ["Path"] = device.Name
        },
        Children = children
    };
}
```

Do not expose the containing group name and do not create a `DeviceFolder` node.

- [ ] **Step 5: Run focused and regression tests and observe GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests|FullyQualifiedName~HardwareDeviceSelectionTests"
```

Expected: PASS. `HardwareDeviceSelectionTests.HardwareConfigReader_SelectDevices_ReadsNameOnceIntoEvidence` must continue proving that the helper change did not re-read device names.

- [ ] **Step 6: Build the real worker shape against Siemens stubs**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
```

Expected: exit 0. This is the compile-time check for the exact `DeviceUserGroup` composition members; source-contract GREEN alone is insufficient.

- [ ] **Step 7: Commit the task after explicit authorization**

```powershell
git add TiaMcpServer.OpennessWorker/Openness/ProjectDeviceEnumerator.cs TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs
git commit -m "feat: enumerate devices in nested groups"
```

---

### Task 2: Traverse PLC system-block groups

**Files:**
- Modify: `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs:44-172`

**Interfaces:**
- Consumes: `PlcBlockSystemGroup.SystemBlockGroups`, `PlcSystemBlockGroup.Blocks`, and `PlcSystemBlockGroup.Groups`.
- Produces: `WalkSystemBlockGroup(PlcSystemBlockGroup group, string path, string? softwareUnitName)`.
- Produces: `BuildBlockNode(PlcBlock block, string path, string? softwareUnitName, bool isSystemBlock)`, the sole functional block-type mapping.

- [ ] **Step 1: Add failing source contracts for the system hierarchy and marker semantics**

Add these tests to `ProjectTraversalSourceContractTests`:

```csharp
[Fact]
public void ProjectTreeWalker_TraversesEverySystemBlockGroupWithItsOwnTypedWalker()
{
    var source = ReadRepositorySource(
        "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

    Assert.Contains("group is PlcBlockSystemGroup systemGroup", source, StringComparison.Ordinal);
    Assert.Contains("foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)", source, StringComparison.Ordinal);
    Assert.Contains("WalkSystemBlockGroup(childGroup", source, StringComparison.Ordinal);
    Assert.Contains("foreach (PlcBlock block in group.Blocks)", source, StringComparison.Ordinal);
    Assert.Contains("foreach (PlcSystemBlockGroup childGroup in group.Groups)", source, StringComparison.Ordinal);
}

[Fact]
public void ProjectTreeWalker_MarksSystemMembershipWithoutChangingFunctionalBlockTypes()
{
    var source = ReadRepositorySource(
        "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs");

    Assert.Contains("NodeType = \"SystemBlockFolder\"", source, StringComparison.Ordinal);
    Assert.Contains("details[\"IsSystemBlock\"] = \"true\";", source, StringComparison.Ordinal);
    Assert.Contains("BuildBlockNode(block, path, softwareUnitName, isSystemBlock: false)", source, StringComparison.Ordinal);
    Assert.Contains("BuildBlockNode(block, path, softwareUnitName, isSystemBlock: true)", source, StringComparison.Ordinal);
    Assert.Equal(1, source.Split("NodeType = block switch", StringSplitOptions.None).Length - 1);
    Assert.DoesNotContain("HeaderAuthor", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests"
```

Expected: FAIL on missing `PlcSystemBlockGroup` traversal, `SystemBlockFolder`, and `IsSystemBlock` evidence.

- [ ] **Step 3: Extract the shared block-node builder**

Replace the block-node construction inside `WalkBlockGroup` with:

```csharp
foreach (PlcBlock block in group.Blocks)
{
    try
    {
        children.Add(BuildBlockNode(block, path, softwareUnitName, isSystemBlock: false));
    }
    catch (EngineeringException ex)
    {
        Console.Error.WriteLine($"Skipping a block while walking block group '{group.Name}': {ex.Message}");
    }
}
```

Add one shared builder containing the existing functional node mapping:

```csharp
private static ProjectTreeNode BuildBlockNode(
    PlcBlock block,
    string path,
    string? softwareUnitName,
    bool isSystemBlock)
{
    var details = new Dictionary<string, string>
    {
        ["Path"] = CombinePath(path, block.Name),
        ["Number"] = block.Number.ToString(),
        ["ProgrammingLanguage"] = block.ProgrammingLanguage.ToString()
    };

    if (softwareUnitName is not null)
    {
        details["SoftwareUnit"] = softwareUnitName;
    }

    if (isSystemBlock)
    {
        details["IsSystemBlock"] = "true";
    }

    return new ProjectTreeNode
    {
        Name = block.Name,
        NodeType = block switch
        {
            OB => "OB",
            FB => "FB",
            FC => "FC",
            GlobalDB => "GlobalDB",
            InstanceDB => "InstanceDB",
            ArrayDB => "ArrayDB",
            _ => "Block"
        },
        Details = details
    };
}
```

- [ ] **Step 4: Add type-correct system recursion with per-item degradation**

After the existing `PlcBlockGroup.Groups` loop in `WalkBlockGroup`, add:

```csharp
if (group is PlcBlockSystemGroup systemGroup)
{
    foreach (PlcSystemBlockGroup childGroup in systemGroup.SystemBlockGroups)
    {
        try
        {
            children.Add(WalkSystemBlockGroup(
                childGroup,
                CombinePath(path, childGroup.Name),
                softwareUnitName));
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine(
                $"Skipping a system block group while walking block group '{group.Name}': {ex.Message}");
        }
    }
}
```

Add the separate typed walker:

```csharp
private static ProjectTreeNode WalkSystemBlockGroup(
    PlcSystemBlockGroup group,
    string path,
    string? softwareUnitName)
{
    var children = new List<ProjectTreeNode>();

    foreach (PlcBlock block in group.Blocks)
    {
        try
        {
            children.Add(BuildBlockNode(block, path, softwareUnitName, isSystemBlock: true));
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine(
                $"Skipping a block while walking system block group '{group.Name}': {ex.Message}");
        }
    }

    foreach (PlcSystemBlockGroup childGroup in group.Groups)
    {
        try
        {
            children.Add(WalkSystemBlockGroup(
                childGroup,
                CombinePath(path, childGroup.Name),
                softwareUnitName));
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine(
                $"Skipping a nested system block group while walking system block group '{group.Name}': {ex.Message}");
        }
    }

    return new ProjectTreeNode
    {
        Name = group.Name,
        NodeType = "SystemBlockFolder",
        Details = new Dictionary<string, string>
        {
            ["Path"] = path
        },
        Children = children
    };
}
```

- [ ] **Step 5: Run focused regressions and the stub build**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests|FullyQualifiedName~ProjectTreeFilterTests"
dotnet build TiaMcpServer.sln -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
```

Expected: both commands exit 0. The unchanged pure `ProjectTreeFilterTests` prove the additive nodes still obey existing `depth` and `startPath` filtering after the worker returns the tree.

- [ ] **Step 6: Commit the task after explicit authorization**

```powershell
git add TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs
git commit -m "feat: include system blocks in project tree"
```

---

### Task 3: Prove the public shapes through FakeWorker

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`

**Interfaces:**
- Consumes: existing `Success`, `ToCamelCaseJson`, `NetworkReadTools.NetworkRead`, and `ProjectReadTools.BrowseProjectTree` paths.
- Produces: FakeWorker scenario key `project-enumeration-completeness` for `read_hardware_config` and `browse_project_tree` only.
- Produces: public evidence that grouped hardware is an ordinary `devices[]` member and grouped tree entries are flat `Device` nodes with nested `SystemBlockFolder` data.

- [ ] **Step 1: Add the failing structured hardware assertion**

In `NetworkIoMapFakeWorkerTests`, add a second scenario constant and test:

```csharp
private const string ProjectCompletenessScenario = "project-enumeration-completeness";

[Fact]
public async Task NetworkRead_ProjectCompletenessFixtureReturnsGroupedDeviceAsOrdinaryHardware()
{
    using var client = CreateClient();

    var result = await NetworkReadTools.NetworkRead(
        client,
        new[] { ReadHardware("complete", ProjectCompletenessScenario) });

    Assert.False(result.IsError);
    var operation = AssertOneCanonicalDocument(result)
        .GetProperty("batch")
        .GetProperty("operations")[0];
    Assert.Equal("succeeded", operation.GetProperty("status").GetString());

    var devices = operation.GetProperty("result").GetProperty("devices");
    Assert.Equal(2, devices.GetArrayLength());
    var grouped = devices.EnumerateArray().Single(
        device => device.GetProperty("name").GetString() == "Grouped ET200");
    Assert.False(grouped.TryGetProperty("group", out _));
    Assert.False(grouped.TryGetProperty("deviceFolder", out _));
}
```

- [ ] **Step 2: Add the failing standalone project-tree assertion**

In `ProjectStandaloneToolTests`, add:

```csharp
[Fact]
public async Task BrowseProjectTree_ProjectCompletenessFixtureKeepsDevicesFlatAndMarksSystemBlocks()
{
    using var client = CreateClient(FakeWorkerLocator.Locate());

    var response = await ProjectReadTools.BrowseProjectTree(
        client,
        projectPath: "project-enumeration-completeness");
    using var document = JsonDocument.Parse(PayloadFromEnvelope(response));
    var nodes = document.RootElement
        .EnumerateArray()
        .SelectMany(Descendants)
        .ToArray();

    Assert.Contains(nodes, node =>
        node.GetProperty("nodeType").GetString() == "Device"
        && node.GetProperty("name").GetString() == "Grouped ET200");
    Assert.DoesNotContain(nodes, node => node.GetProperty("nodeType").GetString() == "DeviceFolder");

    var systemFolder = Assert.Single(nodes.Where(
        node => node.GetProperty("nodeType").GetString() == "SystemBlockFolder"));
    Assert.Equal("System blocks", systemFolder.GetProperty("name").GetString());

    var systemBlock = Assert.Single(nodes.Where(
        node => node.GetProperty("name").GetString() == "SafeFB"));
    Assert.Equal("FB", systemBlock.GetProperty("nodeType").GetString());
    Assert.Equal("true", systemBlock.GetProperty("details").GetProperty("IsSystemBlock").GetString());
}

private static IEnumerable<JsonElement> Descendants(JsonElement node)
{
    yield return node;
    if (!node.TryGetProperty("children", out var children))
    {
        yield break;
    }

    foreach (var child in children.EnumerateArray())
    {
        foreach (var descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}
```

- [ ] **Step 3: Run both tests and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "Name~ProjectCompletenessFixture"
```

Expected: FAIL because FakeWorker has no `project-enumeration-completeness` scenario. Confirm the failure is the unknown scenario/failed operation, not a test compilation problem.

- [ ] **Step 4: Add one typed FakeWorker scenario for both reads**

Add this switch arm near the other read fixtures:

```csharp
case "project-enumeration-completeness":
    Respond(ReadMethod(line) switch
    {
        "read_hardware_config" => Success(ToCamelCaseJson(ProjectCompletenessHardware())),
        "browse_project_tree" => Success(ToCamelCaseJson(ProjectCompletenessTree())),
        _ => $$"""{"success":false,"error":"unexpected project completeness method '{{ReadMethod(line)}}'"}"""
    });
    break;
```

Add typed fixture builders with no folder/provenance member on hardware devices:

```csharp
static HardwareConfigInfo ProjectCompletenessHardware() => new()
{
    Devices = new List<DeviceInfo>
    {
        new() { Name = "Direct PLC", TypeIdentifier = "OrderNumber:CPU" },
        new() { Name = "Grouped ET200", TypeIdentifier = "OrderNumber:ET200" },
    },
};

static List<ProjectTreeNode> ProjectCompletenessTree() => new()
{
    new()
    {
        Name = "Direct PLC",
        NodeType = "Device",
        Details = new Dictionary<string, string> { ["Path"] = "Direct PLC" },
    },
    new()
    {
        Name = "Grouped ET200",
        NodeType = "Device",
        Details = new Dictionary<string, string> { ["Path"] = "Grouped ET200" },
        Children = new List<ProjectTreeNode>
        {
            new()
            {
                Name = "PLC_Grouped",
                NodeType = "PlcSoftware",
                Details = new Dictionary<string, string> { ["Path"] = "Grouped ET200" },
                Children = new List<ProjectTreeNode>
                {
                    new()
                    {
                        Name = "Blocks",
                        NodeType = "BlockFolder",
                        Details = new Dictionary<string, string> { ["Path"] = "PLC_Grouped/Blocks" },
                        Children = new List<ProjectTreeNode>
                        {
                            new()
                            {
                                Name = "System blocks",
                                NodeType = "SystemBlockFolder",
                                Details = new Dictionary<string, string>
                                {
                                    ["Path"] = "PLC_Grouped/Blocks/System blocks"
                                },
                                Children = new List<ProjectTreeNode>
                                {
                                    new()
                                    {
                                        Name = "SafeFB",
                                        NodeType = "FB",
                                        Details = new Dictionary<string, string>
                                        {
                                            ["Path"] = "PLC_Grouped/Blocks/System blocks/SafeFB",
                                            ["Number"] = "200",
                                            ["ProgrammingLanguage"] = "F_LAD",
                                            ["IsSystemBlock"] = "true",
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        },
    },
};
```

- [ ] **Step 5: Run public-shape and regression tests and observe GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "Name~ProjectCompletenessFixture|FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~NetworkIoMapFakeWorkerTests"
```

Expected: PASS. The test must inspect the canonical structured hardware result and the standalone tree payload, not merely the raw FakeWorker response literal.

- [ ] **Step 6: Commit the task after explicit authorization**

```powershell
git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs
git commit -m "test: cover complete project enumeration shapes"
```

---

### Task 4: Document the complete traversal and current payload boundary

**Files:**
- Modify: `docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md:5-36`
- Modify: `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md:5-11`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md:23-28,217-221,387-393`
- Modify: `docs/ARCHITECTURE.md:90-112`

**Interfaces:**
- Consumes: the public shapes proved in Tasks 1-3.
- Produces: current user-facing semantics without describing the later pagination contract.

- [ ] **Step 1: Update the device and project operation summaries**

In `DEVICES_OPERATIONS_SUMMARY.md`, make the two read rows state:

```markdown
| `execute_read_batch` / `network_read` | `read_hardware_config` | Recursively discovers devices both at project root and in nested device groups, then reads device items, network interfaces, nodes, subnets, and IO systems. Optional `deviceName` filter and opt-in structured I/O extraction (`includeIoDetails`, `includeTagMatches`) — see [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md). |
| `browse_project_tree` | `browse_project_tree` | Recursively discovers the same direct and grouped devices as flat `Device` nodes; accepts optional `projectPath`, `depth`, and `startPath`. |
```

Retain the non-goal for **managing** device groups, but disambiguate it from reads:

```markdown
- Device-group creation, deletion, rename, or moving devices between groups. Existing grouped devices are discovered by read operations but group folders are not exposed as tree nodes.
```

In `PROJECT_OPERATIONS_SUMMARY.md`, add immediately after the operation table:

```markdown
`browse_project_tree` returns grouped and ungrouped devices with the same flat `Device` shape. PLC system-block groups use `nodeType: "SystemBlockFolder"`; contained blocks keep their functional block type and carry `details.IsSystemBlock: "true"`. That marker records TIA hierarchy membership only and is not an author, vendor, library, or provenance claim.

The tree remains a bare array. Per-item Openness failures are best-effort worker stderr diagnostics rather than caller-visible warning objects; use `depth` and `startPath` to bound large trees.
```

- [ ] **Step 2: Update the network operation payload guidance**

In `NETWORK_OPERATIONS_SUMMARY.md`, describe grouped-device completeness in the operation row and add this payload-size guidance:

```markdown
Recursive group traversal can make an unfiltered hardware result substantially larger than earlier versions because previously omitted devices are now present. The current unpaged contract still applies the 60,000-character per-result budget and may omit the whole operation with `reason: "resultExceededItemCharLimit"`. Until a paged request is explicitly available, use `deviceName`, disable optional detail flags where possible, and place the hardware read in its own `network_read` call. No device is silently skipped merely to fit the budget.
```

Update the result-shape table to say that `devices[]` includes project-root and recursively grouped devices and that `messages[]` remains the hardware degradation channel.

- [ ] **Step 3: Record the internal traversal seam in architecture**

Add a focused subsection near the read-tool catalog in `ARCHITECTURE.md`:

```markdown
### Project enumeration completeness

The net48 worker owns one ordered `ProjectDeviceEnumerator`: direct `Project.Devices` first, followed by a depth-first walk of `Project.DeviceGroups`. `HardwareConfigReader` and `ProjectTreeWalker` both consume it, preventing their definitions of a complete project from drifting. The public project tree deliberately flattens grouped devices into ordinary `Device` nodes.

PLC user block groups and system block groups are different Openness types. `ProjectTreeWalker` therefore keeps separate recursive walkers and shares only block-node construction. `SystemBlockFolder` and `IsSystemBlock` encode system-hierarchy membership, not provenance. Hardware degradation uses `HardwareConfigInfo.Messages`; project-tree per-item failures retain the existing stderr-only best-effort boundary.
```

- [ ] **Step 4: Validate wording, links, and scope**

Run:

```powershell
rg -n "ProjectDeviceEnumerator|SystemBlockFolder|IsSystemBlock|resultExceededItemCharLimit" docs/ARCHITECTURE.md docs/SupportedOperations
git diff --check
```

Expected: the first command finds all four concepts and `git diff --check` exits 0. Read every modified paragraph once for the exact distinction between enumeration and device-group management.

- [ ] **Step 5: Commit the task after explicit authorization**

```powershell
git add docs/ARCHITECTURE.md docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md
git commit -m "docs: document complete project enumeration"
```

---

### Task 5: Run full verification and prepare the live acceptance gate

**Files:**
- Verify only: all files changed by Tasks 1-4
- Create only after an authorized live run: `docs/superpowers/acceptance/reports/2026-08-28-project-enumeration-completeness-live.md`

**Interfaces:**
- Consumes: completed PR 1 implementation and tests.
- Produces: fresh automated evidence plus a precise read-only live runbook; it does not fabricate live evidence when no authorized V21 project was run.

- [ ] **Step 1: Run all focused PR 1 tests**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests|Name~ProjectCompletenessFixture|FullyQualifiedName~HardwareDeviceSelectionTests|FullyQualifiedName~ProjectTreeFilterTests"
```

Expected: exit 0 with zero failed tests.

- [ ] **Step 2: Run the serial stub build**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
```

Expected: exit 0 with no compile errors. Report warnings exactly rather than calling the build clean if warnings remain.

- [ ] **Step 3: Run the complete test project**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
```

Expected: exit 0 with zero failed tests. If an unrelated known flake occurs, record its exact name and rerun that exact test; do not modify unrelated code under this scope.

- [ ] **Step 4: Inspect scope and repository state**

Run:

```powershell
git diff --check
git status --short
git diff --stat main...HEAD
```

Then review the branch diff and confirm:

- no pagination field, cursor codec, response envelope, or write path was added;
- no `DeviceFolder` node was added;
- only system-hierarchy blocks receive `IsSystemBlock`;
- no authorship or vendor inference was added;
- source-contract/FakeWorker evidence is described as non-live; and

- [ ] **Step 5: Stop at the live-TIA authorization gate unless separately approved**

Do not run this step as part of ordinary tests. With separate authorization and an explicitly selected read-only V21 project:

1. Start the built MCP host with `--access-mode read-only` and bind it to the authorized project.
2. Call `browse_project_tree` with `depth: 1`; record the total root `Device` count and at least two known devices that live inside nested device groups.
3. Call `read_hardware_config` separately for those known grouped devices using exact `deviceName` filters; record that each returns exactly one matching device.
4. Call `browse_project_tree` with the exact PLC block-folder `startPath`; record every `SystemBlockFolder`, the system-block count, representative functional node types, and `details.IsSystemBlock: "true"`.
5. Record worker stderr separately from returned hardware `messages` and do not describe either channel as complete if the result was truncated or omitted.
6. Save project identity, tool inputs, counts, representative safe identities, timings, result-budget outcomes, and the exact tested commit in a new acceptance report. Add that report to `docs/superpowers/README.md` and `docs/README.md` before committing it.

The live report must say explicitly that the run was read-only. It must not include confidential project content beyond user-approved safe identities and aggregate counts.

- [ ] **Step 6: Report the evidence boundary**

The completion report must separate:

```text
Automated: source-contract tests, FakeWorker public shapes, stub build, full net8 test suite.
Live Openness: not run unless the separately authorized Step 5 completed.
Mutation/commissioning: not applicable; PR 1 is read-only enumeration.
```

Do not create an empty verification commit. If authorized live evidence adds a report, commit only that report and its two index entries with a separately approved commit message.
