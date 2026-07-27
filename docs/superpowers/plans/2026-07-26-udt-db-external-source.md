# UDT and DB External-Source Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add PLC data type (UDT) read/write operations to this MCP server using Siemens' native external-source pipeline, then extend the same pipeline to global data blocks as an opt-in format.

**Architecture:** Two new batch operations (`get_type_content`, `update_type_content`) plus a `format` field on the existing block operations. Every component splits into a Siemens-free half that is unit-tested and a thin `Siemens.Engineering`-calling shell that is covered only by a committed live-test harness. Imports go through `ExternalSourceScope`, an `IDisposable` that guarantees the `PlcExternalSource` project node it creates is deleted again.

**Tech Stack:** C# — `netstandard2.0` (Contracts), `net48` (worker, Siemens Openness V21), `net8.0` (host + xunit tests). PowerShell 7 for the live harness.

**Spec:** `docs/superpowers/specs/2026-07-26-udt-db-external-source-design.md`

## Global Constraints

- **Build serially.** `dotnet build TiaMcpServer.sln -m:1` — `-m:1` is required to avoid parallel worker-build conflicts.
- **Use PowerShell, not Bash, for any `dotnet` command carrying a `/p:` MSBuild flag.** Bash mangles `/p:` on this machine.
- **CI build must stay green without TIA Portal installed:** `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`.
- **Siemens DLLs are never committed** to the repo or the NuGet package.
- **Every new worker file that is free of `Siemens.Engineering` types MUST be added to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` as a `<Compile Include>` link.** Files that touch Siemens types must NOT be linked — the test project has no Siemens reference and will fail to compile.
- **Tool count stays at 10.** No new `[McpServerTool]` methods. New operations register in `BatchOperationCatalog` only.
- **No default changes to existing operations.** `get_block_content` and `update_block_logic` must return byte-identical results to today when `format` is omitted.
- **Commit format:** conventional commits — `<type>: <description>` where type is one of feat, fix, refactor, docs, test, chore, perf, ci.
- **Coverage floor:** 80% over the test project's compiled sources.

## Where this plan gives instructions instead of literal code, and why

Tasks 1-6 and 9 contain complete, copy-able code. Tasks 7, 10, and 11 deliberately do not, in three specific places:

1. **Mirroring existing methods whose bodies this plan's author did not read in full** — `BatchWorkerInvoker` dispatch arms, `OpennessWorkerClient.SendAsync` call shape, `BlockImportCoordinator` routing. Each such step begins by telling you to read the neighboring method and match it. Writing invented code that *looks* authoritative would be worse than this: you would copy a pattern the file does not use.
2. **Openness signatures that are unverified until Task 8 runs.** The API surface was confirmed by static inspection of `Siemens.Engineering.Step7.dll` v21.0.0.0, but exact overload shapes were not. Where a call might not compile as written, the plan says so.
3. **`DbSourceOffsetColumn`'s regex (Task 9)**, which cannot be written until the user supplies a non-optimized DB export — that fixture does not exist in the repo today.

Treat these as "read, then mirror," never as licence to improvise a new pattern. If a mirrored call does not compile, fix the call — do not redesign around it.

## File Structure

**Created — `TiaMcpServer.Contracts` (netstandard2.0):**

| File | Responsibility |
|---|---|
| `SourceFormatNames.cs` | Validate and normalize the `format` field. No default of its own. |
| `PlcTypeAddress.cs` | Parse a PLC data type path into PLC / unit / folders / type name. |

**Created — `TiaMcpServer.OpennessWorker/Openness` (net48), Siemens-free, linked into tests:**

| File | Responsibility |
|---|---|
| `SourceTextEncoding.cs` | Strip UTF-8 BOM on export; re-emit BOM + CRLF on import. |
| `PlcTypeSourcePreflight.cs` | Extract the declared object name from `.udt` / `.db` / SimaticML text. |
| `DbSourceOffsetColumn.cs` | Detect a byte-offset column in a `.db` source (Phase 2). |

**Created — `TiaMcpServer.OpennessWorker/Openness` (net48), Siemens-touching, NOT linked:**

| File | Responsibility |
|---|---|
| `PlcTypeTargetResolver.cs` | Resolve a `PlcTypeAddress` against a live project. |
| `ExternalSourceScope.cs` | Own the temp file + `PlcExternalSource` node lifetime. |
| `PlcTypeExporter.cs` | Export a `PlcType` as `.udt` or SimaticML. |
| `PlcTypeImporter.cs` | Import a `PlcType` from `.udt` or SimaticML. |
| `PlcTypePostconditionVerifier.cs` | Re-export + compile after a type write. |

**Created — scripts and tests:**

| File | Responsibility |
|---|---|
| `scripts/live-test-udt.ps1` | Phase 1 live gate. |
| `scripts/live-test-db.ps1` | Phase 2 live gate. |
| `TiaMcpServer.Tests/SourceFormatNamesTests.cs` | |
| `TiaMcpServer.Tests/PlcTypeAddressTests.cs` | |
| `TiaMcpServer.Tests/SourceTextEncodingTests.cs` | |
| `TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs` | |
| `TiaMcpServer.Tests/DbSourceOffsetColumnTests.cs` | |
| `TiaMcpServer.Tests/Fixtures/AnalogInputSettings.udt` | Real V21 export. |
| `TiaMcpServer.Tests/Fixtures/AnalogInputSettings.xml` | Real V21 export. |
| `TiaMcpServer.Tests/Fixtures/Simulation_DB.db` | Real V21 export. |

**Modified:**

| File | Change |
|---|---|
| `TiaMcpServer.Contracts/WorkerRequest.cs` | Add `TypePath`, `SourceContent`, `Format`. |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Add `TypePath`, `SourceContent`, `Format`. |
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | Register two operations; add `format` to two existing ones. |
| `TiaMcpServer/Batch/BatchSafetySnapshot.cs` | Describe `update_type_content`. |
| `TiaMcpServer/Batch/BatchWorkerInvoker.cs` | Map both operations; supply current-state readings. |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Two new client methods; `format` on two existing. |
| `TiaMcpServer.OpennessWorker/Program.cs` | Two new dispatch cases. |
| `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs` | Source-format branch for GlobalDB. |
| `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs` | Source-format route for GlobalDB. |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Link new Siemens-free worker files; copy fixtures. |
| `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` | Apply the two corrections. |

---

## PHASE 1 — UDT

### Task 1: `SourceFormatNames`

Validates the `format` field before a session binds, mirroring the existing `CrossReferenceFilterNames`.

**Files:**
- Create: `TiaMcpServer.Contracts/SourceFormatNames.cs`
- Test: `TiaMcpServer.Tests/SourceFormatNamesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaMcpServer.Contracts.SourceFormatNames` with `const string Source = "source"`, `const string Xml = "xml"`, `static IReadOnlyList<string> Allowed`, and `static bool TryNormalize(string? value, string fallback, out string normalized, out string? error)`. Note the explicit `fallback` parameter — types default to `Source`, blocks default to `Xml`, so this class holds no default of its own.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/SourceFormatNamesTests.cs`:

```csharp
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests;

public class SourceFormatNamesTests
{
    [Fact]
    public void Null_value_uses_the_caller_supplied_fallback()
    {
        var ok = SourceFormatNames.TryNormalize(null, SourceFormatNames.Source, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal("source", normalized);
        Assert.Null(error);
    }

    [Fact]
    public void Whitespace_value_uses_the_caller_supplied_fallback()
    {
        var ok = SourceFormatNames.TryNormalize("   ", SourceFormatNames.Xml, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal("xml", normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("source", "source")]
    [InlineData("SOURCE", "source")]
    [InlineData("Source", "source")]
    [InlineData("xml", "xml")]
    [InlineData("XML", "xml")]
    public void Known_values_normalize_case_insensitively(string input, string expected)
    {
        var ok = SourceFormatNames.TryNormalize(input, SourceFormatNames.Xml, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void Unknown_value_is_rejected_and_lists_the_allowed_values()
    {
        var ok = SourceFormatNames.TryNormalize("s7dcl", SourceFormatNames.Xml, out var normalized, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
        Assert.NotNull(error);
        Assert.Contains("s7dcl", error);
        Assert.Contains("source", error);
        Assert.Contains("xml", error);
    }

    [Fact]
    public void Allowed_lists_exactly_the_two_supported_formats()
    {
        Assert.Equal(new[] { "source", "xml" }, SourceFormatNames.Allowed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceFormatNamesTests"`
Expected: FAIL — compile error, `SourceFormatNames` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `TiaMcpServer.Contracts/SourceFormatNames.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Document format selector shared by the type and block read/write operations.
///
/// <para>
/// Deliberately object-kind-agnostic: <see cref="Source"/> means "Siemens' external-source text
/// for whatever this object is" — .udt for a PlcType, .db for a GlobalDB, .scl for an SCL block —
/// and the extension is always derived from the resolved object, never from the caller.
/// </para>
/// <para>
/// This class exposes no default. The default is per-operation and passed in by the caller:
/// the type operations default to <see cref="Source"/> because they are net-new surface, and the
/// block operations default to <see cref="Xml"/> because they have callers whose payloads must
/// not change. Flipping block defaults belongs to roadmap Phase 5, not here.
/// </para>
/// </summary>
public static class SourceFormatNames
{
    public const string Source = "source";
    public const string Xml = "xml";

    public static readonly IReadOnlyList<string> Allowed = new[] { Source, Xml };

    public static bool TryNormalize(string? value, string fallback, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = fallback;
            error = null;
            return true;
        }

        foreach (var allowed in Allowed)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = allowed;
                error = null;
                return true;
            }
        }

        normalized = string.Empty;
        error = $"Invalid format '{value}'. Allowed values: {string.Join(", ", Allowed)}.";
        return false;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceFormatNamesTests"`
Expected: PASS — 9 tests passed (5 from the Theory).

- [ ] **Step 5: Commit**

```bash
git add TiaMcpServer.Contracts/SourceFormatNames.cs TiaMcpServer.Tests/SourceFormatNamesTests.cs
git commit -m "feat: add SourceFormatNames for source/xml format selection"
```

---

### Task 2: `PlcTypeAddress`

Parses the exact paths `browse_project_tree` already prints for PLC data types.

**Files:**
- Create: `TiaMcpServer.Contracts/PlcTypeAddress.cs`
- Test: `TiaMcpServer.Tests/PlcTypeAddressTests.cs`
- Reference: `TiaMcpServer.Contracts/BlockAddress.cs` — mirror its shape and its error-message style.

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaMcpServer.Contracts.PlcTypeAddress` — a sealed class with `string? PlcName`, `string? UnitName`, `IReadOnlyList<string> FolderPath`, `string TypeName`, `bool IsDeterministic`, `bool UsesSoftwareUnit`, `static PlcTypeAddress Parse(string typePath)`, `string ToDisplayPath()`.

Path shapes accepted, matching `ProjectTreeWalker`'s output:

| Shape | Deterministic |
|---|---|
| `TypeName` | no |
| `PLC/TypeName` | no |
| `PLC/Types/.../TypeName` | yes |
| `PLC/Units/<unit>/Types/.../TypeName` | yes |

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/PlcTypeAddressTests.cs`:

```csharp
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests;

public class PlcTypeAddressTests
{
    [Fact]
    public void Bare_name_is_non_deterministic_with_no_plc()
    {
        var address = PlcTypeAddress.Parse("AnalogInputSettings");

        Assert.Null(address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.False(address.IsDeterministic);
    }

    [Fact]
    public void Plc_and_name_is_non_deterministic_with_a_plc()
    {
        var address = PlcTypeAddress.Parse("PLC_1/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.False(address.IsDeterministic);
    }

    [Fact]
    public void Types_segment_makes_the_address_deterministic()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Types/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Null(address.UnitName);
        Assert.Empty(address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Nested_folders_under_Types_are_captured_in_order()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Types/Sensors/Analog/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal(new[] { "Sensors", "Analog" }, address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Software_unit_path_captures_the_unit_name()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal("DriveUnit", address.UnitName);
        Assert.True(address.UsesSoftwareUnit);
        Assert.Equal(new[] { "Sensors" }, address.FolderPath);
        Assert.Equal("AnalogInputSettings", address.TypeName);
        Assert.True(address.IsDeterministic);
    }

    [Fact]
    public void Types_segment_is_matched_case_insensitively()
    {
        var address = PlcTypeAddress.Parse("PLC_1/types/AnalogInputSettings");

        Assert.True(address.IsDeterministic);
        Assert.Equal("AnalogInputSettings", address.TypeName);
    }

    [Fact]
    public void Segments_are_trimmed()
    {
        var address = PlcTypeAddress.Parse(" PLC_1 / Types / AnalogInputSettings ");

        Assert.Equal("PLC_1", address.PlcName);
        Assert.Equal("AnalogInputSettings", address.TypeName);
    }

    [Fact]
    public void Round_trips_through_ToDisplayPath()
    {
        var address = PlcTypeAddress.Parse("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings");

        Assert.Equal("PLC_1/Units/DriveUnit/Types/Sensors/AnalogInputSettings", address.ToDisplayPath());
    }

    [Fact]
    public void Non_deterministic_display_path_omits_the_Types_segment()
    {
        var address = PlcTypeAddress.Parse("PLC_1/AnalogInputSettings");

        Assert.Equal("PLC_1/AnalogInputSettings", address.ToDisplayPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_path_is_rejected(string input)
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse(input));
    }

    [Fact]
    public void Empty_segment_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1//AnalogInputSettings"));
    }

    [Fact]
    public void Types_segment_with_no_type_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1/Types"));
    }

    [Fact]
    public void A_blocks_path_is_rejected_because_it_is_not_a_type_path()
    {
        var ex = Assert.Throws<ArgumentException>(() => PlcTypeAddress.Parse("PLC_1/Blocks/Main"));

        Assert.Contains("Types", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeAddressTests"`
Expected: FAIL — compile error, `PlcTypeAddress` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `TiaMcpServer.Contracts/PlcTypeAddress.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// A path to a PLC data type (UDT), parsed into the parts needed to walk a live project.
///
/// <para>
/// Deliberately mirrors <see cref="BlockAddress"/>: same field shape, same deterministic /
/// non-deterministic distinction, same trimming rules. It accepts exactly the paths
/// ProjectTreeWalker prints for Type nodes, so a path copied out of browse_project_tree works
/// without editing.
/// </para>
/// </summary>
public sealed class PlcTypeAddress
{
    private const string TypesSegment = "Types";
    private const string UnitsSegment = "Units";

    private PlcTypeAddress(
        string? plcName,
        string? unitName,
        IReadOnlyList<string> folderPath,
        string typeName,
        bool isDeterministic)
    {
        PlcName = plcName;
        UnitName = unitName;
        FolderPath = folderPath;
        TypeName = typeName;
        IsDeterministic = isDeterministic;
    }

    public string? PlcName { get; }

    public string? UnitName { get; }

    public IReadOnlyList<string> FolderPath { get; }

    public string TypeName { get; }

    public bool IsDeterministic { get; }

    public bool UsesSoftwareUnit => UnitName is not null;

    public static PlcTypeAddress Parse(string typePath)
    {
        if (string.IsNullOrWhiteSpace(typePath))
        {
            throw new ArgumentException("Type path is required.", nameof(typePath));
        }

        var segments = SplitSegments(typePath);

        if (segments.Count == 1)
        {
            return new PlcTypeAddress(
                plcName: null,
                unitName: null,
                folderPath: Array.Empty<string>(),
                typeName: segments[0],
                isDeterministic: false);
        }

        if (segments.Count == 2 && !IsReservedSegment(segments[1]))
        {
            return new PlcTypeAddress(
                plcName: segments[0],
                unitName: null,
                folderPath: Array.Empty<string>(),
                typeName: segments[1],
                isDeterministic: false);
        }

        if (segments.Count >= 3 && IsSegment(segments[1], TypesSegment))
        {
            return FromTypeSegments(segments[0], unitName: null, segments, startIndex: 2);
        }

        if (segments.Count >= 5 &&
            IsSegment(segments[1], UnitsSegment) &&
            IsSegment(segments[3], TypesSegment))
        {
            return FromTypeSegments(segments[0], segments[2], segments, startIndex: 4);
        }

        throw new ArgumentException(
            "Type path must be 'TypeName', 'PLC/TypeName', 'PLC/Types/.../TypeName', or "
            + "'PLC/Units/Unit/Types/.../TypeName'.",
            nameof(typePath));
    }

    public string ToDisplayPath()
    {
        var segments = new List<string>();

        if (PlcName is not null)
        {
            segments.Add(PlcName);
        }

        if (UnitName is not null)
        {
            segments.Add(UnitsSegment);
            segments.Add(UnitName);
        }

        if (IsDeterministic)
        {
            segments.Add(TypesSegment);
        }

        segments.AddRange(FolderPath);
        segments.Add(TypeName);

        return string.Join("/", segments);
    }

    private static PlcTypeAddress FromTypeSegments(
        string plcName,
        string? unitName,
        IReadOnlyList<string> segments,
        int startIndex)
    {
        if (startIndex >= segments.Count)
        {
            throw new ArgumentException("Type path is missing a type name.", nameof(segments));
        }

        var folders = new List<string>();
        for (int i = startIndex; i < segments.Count - 1; i++)
        {
            folders.Add(segments[i]);
        }

        return new PlcTypeAddress(
            plcName,
            unitName,
            folders.AsReadOnly(),
            segments[segments.Count - 1],
            isDeterministic: true);
    }

    private static List<string> SplitSegments(string typePath)
    {
        var result = new List<string>();
        foreach (var rawSegment in typePath.Split('/'))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                throw new ArgumentException("Type path cannot contain empty segments.", nameof(typePath));
            }

            result.Add(segment);
        }

        return result;
    }

    private static bool IsSegment(string segment, string expected)
        => string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedSegment(string segment)
        => IsSegment(segment, TypesSegment) || IsSegment(segment, UnitsSegment);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeAddressTests"`
Expected: PASS — 14 tests passed.

Note on `PLC_1/Types` (the "missing type name" case): it has 2 segments and `segments[1]` IS reserved, so it falls through the `Count == 2` branch, fails `Count >= 3`, fails `Count >= 5`, and hits the final `throw`. That is the intended `ArgumentException`.

- [ ] **Step 5: Commit**

```bash
git add TiaMcpServer.Contracts/PlcTypeAddress.cs TiaMcpServer.Tests/PlcTypeAddressTests.cs
git commit -m "feat: add PlcTypeAddress path parsing for PLC data types"
```

---

### Task 3: `SourceTextEncoding` and test fixtures

Siemens writes external-source files as UTF-8 **with BOM** and CRLF. A BOM inside a JSON string payload is noise; CRLF must survive the round trip.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/SourceTextEncoding.cs`
- Create: `TiaMcpServer.Tests/SourceTextEncodingTests.cs`
- Create: `TiaMcpServer.Tests/Fixtures/AnalogInputSettings.udt` (copy of `priv/tia_exports/AnalogInputSettings.udt`)
- Create: `TiaMcpServer.Tests/Fixtures/AnalogInputSettings.xml` (copy of `priv/tia_exports/AnalogInputSettings.xml`)
- Create: `TiaMcpServer.Tests/Fixtures/Simulation_DB.db` (copy of `priv/tia_exports/Simulation_DB.db`)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaMcpServer.OpennessWorker.Openness.SourceTextEncoding` — `static string ForTransport(string fileText)` and `static byte[] ForFile(string transportText)`.

- [ ] **Step 1: Copy the fixtures**

```bash
mkdir -p TiaMcpServer.Tests/Fixtures
cp priv/tia_exports/AnalogInputSettings.udt TiaMcpServer.Tests/Fixtures/AnalogInputSettings.udt
cp priv/tia_exports/AnalogInputSettings.xml TiaMcpServer.Tests/Fixtures/AnalogInputSettings.xml
cp priv/tia_exports/Simulation_DB.db TiaMcpServer.Tests/Fixtures/Simulation_DB.db
```

- [ ] **Step 2: Register the fixtures and the new source file in the test project**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, inside the existing `<ItemGroup>`, next to the two existing `<None Include="Fixtures\...">` entries, add:

```xml
    <None Include="Fixtures\AnalogInputSettings.udt" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures\AnalogInputSettings.xml" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures\Simulation_DB.db" CopyToOutputDirectory="PreserveNewest" />
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\SourceTextEncoding.cs"
      Link="Linked\Openness\SourceTextEncoding.cs" />
```

- [ ] **Step 3: Write the failing test**

Create `TiaMcpServer.Tests/SourceTextEncodingTests.cs`:

```csharp
using System.Text;
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class SourceTextEncodingTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ForTransport_strips_a_leading_byte_order_mark()
    {
        var withBom = "\uFEFFTYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n";

        var result = SourceTextEncoding.ForTransport(withBom);

        Assert.StartsWith("TYPE", result);
        Assert.DoesNotContain("\uFEFF", result);
    }

    [Fact]
    public void ForTransport_preserves_CRLF_line_endings()
    {
        var withBom = "\uFEFFTYPE\r\nEND_TYPE\r\n";

        var result = SourceTextEncoding.ForTransport(withBom);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", result);
    }

    [Fact]
    public void ForTransport_leaves_text_without_a_BOM_untouched()
    {
        var result = SourceTextEncoding.ForTransport("TYPE\r\nEND_TYPE\r\n");

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", result);
    }

    [Fact]
    public void ForFile_writes_a_byte_order_mark()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\r\nEND_TYPE\r\n");

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void ForFile_normalizes_bare_LF_to_CRLF()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\nEND_TYPE\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", text);
    }

    [Fact]
    public void ForFile_does_not_double_up_existing_CRLF()
    {
        var bytes = SourceTextEncoding.ForFile("TYPE\r\nEND_TYPE\r\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.Equal("TYPE\r\nEND_TYPE\r\n", text);
        Assert.DoesNotContain("\r\r", text);
    }

    [Fact]
    public void ForFile_does_not_emit_a_second_BOM_when_the_transport_text_still_has_one()
    {
        var bytes = SourceTextEncoding.ForFile("\uFEFFTYPE\r\n");
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        Assert.StartsWith("TYPE", text);
    }

    [Fact]
    public void Real_V21_udt_export_round_trips_byte_identically()
    {
        var original = File.ReadAllBytes(FixturePath("AnalogInputSettings.udt"));
        var fileText = new UTF8Encoding(true).GetString(original);

        var roundTripped = SourceTextEncoding.ForFile(SourceTextEncoding.ForTransport(fileText));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Real_V21_db_export_round_trips_byte_identically()
    {
        var original = File.ReadAllBytes(FixturePath("Simulation_DB.db"));
        var fileText = new UTF8Encoding(true).GetString(original);

        var roundTripped = SourceTextEncoding.ForFile(SourceTextEncoding.ForTransport(fileText));

        Assert.Equal(original, roundTripped);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceTextEncodingTests"`
Expected: FAIL — compile error, `SourceTextEncoding` does not exist.

- [ ] **Step 5: Write minimal implementation**

Create `TiaMcpServer.OpennessWorker/Openness/SourceTextEncoding.cs`:

```csharp
using System.Text;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// BOM and line-ending handling for Siemens external-source text (.udt, .db, .scl).
///
/// <para>
/// TIA Portal writes these files as UTF-8 WITH a byte order mark and CRLF line endings. The BOM is
/// meaningful on disk and noise inside a JSON string payload, so it is stripped on the way out and
/// restored on the way in. Line endings are normalized to CRLF on the way in because a client that
/// edits the text through a JSON round trip may well hand back bare LF.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceTextEncoding
{
    private const char ByteOrderMark = '\uFEFF';

    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Disk text to payload text: drop the BOM, leave everything else alone.</summary>
    public static string ForTransport(string fileText)
    {
        if (string.IsNullOrEmpty(fileText))
        {
            return string.Empty;
        }

        return fileText[0] == ByteOrderMark ? fileText.Substring(1) : fileText;
    }

    /// <summary>Payload text to disk bytes: normalize to CRLF and prepend the BOM.</summary>
    public static byte[] ForFile(string transportText)
    {
        var text = transportText ?? string.Empty;

        if (text.Length > 0 && text[0] == ByteOrderMark)
        {
            text = text.Substring(1);
        }

        return Utf8WithBom.GetBytes(NormalizeToCrLf(text));
    }

    private static string NormalizeToCrLf(string text)
    {
        var builder = new StringBuilder(text.Length + 16);

        for (int i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (character == '\r')
            {
                builder.Append("\r\n");
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (character == '\n')
            {
                builder.Append("\r\n");
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceTextEncodingTests"`
Expected: PASS — 9 tests passed.

If `Real_V21_udt_export_round_trips_byte_identically` fails, inspect the fixture's actual bytes before changing the implementation — the fixture is ground truth:

```bash
xxd priv/tia_exports/AnalogInputSettings.udt | head -3
xxd priv/tia_exports/AnalogInputSettings.udt | tail -3
```

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/SourceTextEncoding.cs \
        TiaMcpServer.Tests/SourceTextEncodingTests.cs \
        TiaMcpServer.Tests/Fixtures/ \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add SourceTextEncoding for BOM and CRLF handling"
```

---

### Task 4: `PlcTypeSourcePreflight`

Extracts the declared object name so `update_type_content` can refuse to write a source whose name does not match its target. This is what makes the write **strict** rather than an accidental upsert — `GenerateBlocksFromSource` will happily create a type it does not recognize.

Handles `.udt`, `.db`, and SimaticML because Phase 2 reuses it unchanged.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs`
- Create: `TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: `SourceFormatNames` (Task 1).
- Produces: `TiaMcpServer.OpennessWorker.Openness.PlcTypeSourcePreflight` with `static bool TryReadDeclaredName(string content, string format, out string declaredName, out string? error)`.

- [ ] **Step 1: Register the new file in the test project**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, add next to the `SourceTextEncoding.cs` link:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\PlcTypeSourcePreflight.cs"
      Link="Linked\Openness\PlcTypeSourcePreflight.cs" />
```

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs`:

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class PlcTypeSourcePreflightTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Reads_the_type_name_from_a_real_V21_udt_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.udt"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Reads_the_block_name_from_a_real_V21_db_export()
    {
        var content = File.ReadAllText(FixturePath("Simulation_DB.db"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Simulation_DB", name);
    }

    [Fact]
    public void Reads_the_name_from_a_real_V21_SimaticML_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.xml"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Xml, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }

    [Fact]
    public void Accepts_an_unquoted_type_name()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "TYPE Foo\nSTRUCT\nEND_STRUCT;\nEND_TYPE\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_leading_comments_and_blank_lines()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "// generated\r\n\r\n(* banner *)\r\nTYPE \"Foo\"\r\nEND_TYPE\r\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Foo", name);
    }

    [Fact]
    public void Skips_a_leading_attribute_block_before_DATA_BLOCK()
    {
        var content = "{ DB_Accessible_From_OPC_UA := 'FALSE' }\r\nDATA_BLOCK \"Bar\"\r\nEND_DATA_BLOCK\r\n";

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("Bar", name);
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "   ", SourceFormatNames.Source, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
    }

    [Fact]
    public void Source_with_no_recognizable_declaration_is_rejected_with_a_useful_message()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "FUNCTION_BLOCK \"Nope\"\nEND_FUNCTION_BLOCK\n",
            SourceFormatNames.Source, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("TYPE", error);
        Assert.Contains("DATA_BLOCK", error);
    }

    [Fact]
    public void Xml_with_no_name_element_is_rejected()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><SW.Types.PlcStruct /></Document>",
            SourceFormatNames.Xml, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Malformed_xml_is_rejected_without_throwing()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "<Document><unclosed>", SourceFormatNames.Xml, out var name, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeSourcePreflightTests"`
Expected: FAIL — compile error, `PlcTypeSourcePreflight` does not exist.

- [ ] **Step 4: Write minimal implementation**

Create `TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs`:

```csharp
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Reads the object name a submitted document declares, so a write can refuse a document whose
/// name does not match the object it was addressed to.
///
/// <para>
/// This is what makes update_type_content strict rather than an upsert: Openness'
/// GenerateBlocksFromSource creates an object it does not recognize, so without this check a typo
/// in the path would silently create a stray type instead of failing.
/// </para>
/// <para>
/// Handles TYPE (.udt) and DATA_BLOCK (.db) in one place because the DB phase reuses it unchanged.
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class PlcTypeSourcePreflight
{
    private static readonly Regex DeclarationPattern = new Regex(
        @"^\s*(?<keyword>TYPE|DATA_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<bare>[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static bool TryReadDeclaredName(
        string content,
        string format,
        out string declaredName,
        out string? error)
    {
        declaredName = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The submitted document is empty.";
            return false;
        }

        return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
            ? TryReadFromXml(content, out declaredName, out error)
            : TryReadFromSource(content, out declaredName, out error);
    }

    private static bool TryReadFromSource(string content, out string declaredName, out string? error)
    {
        declaredName = string.Empty;

        var match = DeclarationPattern.Match(content);
        if (!match.Success)
        {
            error = "The submitted source declares no object. Expected a line beginning with "
                + "TYPE (for a PLC data type) or DATA_BLOCK (for a data block).";
            return false;
        }

        var quoted = match.Groups["quoted"];
        declaredName = quoted.Success ? quoted.Value : match.Groups["bare"].Value;
        error = null;
        return true;
    }

    private static bool TryReadFromXml(string content, out string declaredName, out string? error)
    {
        declaredName = string.Empty;

        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (Exception ex)
        {
            error = $"The submitted document is not well-formed XML: {ex.Message}";
            return false;
        }

        var name = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Name")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        if (string.IsNullOrEmpty(name))
        {
            error = "The submitted Simatic ML document has no <Name> element to identify the object.";
            return false;
        }

        declaredName = name!;
        error = null;
        return true;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeSourcePreflightTests"`
Expected: PASS — 10 tests passed.

`Reads_the_name_from_a_real_V21_SimaticML_export` depends on the first non-empty `<Name>` in the fixture being the type's own name. If it fails, open the fixture and narrow the selector to the `<AttributeList>` under `<SW.Types.PlcStruct>` rather than loosening the test:

```bash
head -20 priv/tia_exports/AnalogInputSettings.xml
```

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs \
        TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add PlcTypeSourcePreflight declared-name extraction"
```

---

### Task 5: Host request fields, catalog specs, and preview text

Registers the two operations on the batch surface. All three modified files are already linked into the test project.

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs`
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs:250-285` (the `BuildSpecs` method)
- Modify: `TiaMcpServer/Batch/BatchSafetySnapshot.cs:26-40` (the `DescribeOperation` method)
- Test: `TiaMcpServer.Tests/TypeOperationCatalogTests.cs` (create)

**Interfaces:**
- Consumes: nothing at compile time.
- Produces: batch operations `get_type_content` (Read; required `typePath`; optional `format`) and `update_type_content` (Write; required `typePath`, `sourceContent`; optional `format`). `WorkerRequest.TypePath`, `.SourceContent`, `.Format` for Task 7.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/TypeOperationCatalogTests.cs`:

```csharp
using TiaMcpServer.Batch;

namespace TiaMcpServer.Tests;

public class TypeOperationCatalogTests
{
    private static BatchOperationRequest ReadOp() => new()
    {
        OperationId = "r1",
        Operation = "get_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
    };

    private static BatchOperationRequest WriteOp() => new()
    {
        OperationId = "w1",
        Operation = "update_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
        SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
    };

    [Fact]
    public void get_type_content_is_registered_as_a_read()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("get_type_content", out var spec));
        Assert.Equal(BatchOperationCategory.Read, spec!.Category);
        Assert.Contains("get_type_content", BatchOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void update_type_content_is_registered_as_a_write()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("update_type_content", out var spec));
        Assert.Equal(BatchOperationCategory.Write, spec!.Category);
        Assert.Contains("update_type_content", BatchOperationCatalog.WriteOperationNames);
    }

    [Fact]
    public void get_type_content_accepts_a_type_path()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { ReadOp() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_accepts_an_optional_format()
    {
        var op = ReadOp();
        op.Format = "xml";

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_requires_a_type_path()
    {
        var op = ReadOp();
        op.TypePath = null;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("typePath", result.Error);
    }

    [Fact]
    public void get_type_content_rejects_a_block_path()
    {
        var op = ReadOp();
        op.BlockPath = "PLC_1/Blocks/Main";

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("blockPath", result.Error);
    }

    [Fact]
    public void update_type_content_requires_both_type_path_and_source_content()
    {
        var op = WriteOp();
        op.SourceContent = null;

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("sourceContent", result.Error);
    }

    [Fact]
    public void update_type_content_is_valid_with_both_required_fields()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { WriteOp() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_is_rejected_inside_a_write_batch()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { ReadOp() });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void update_type_content_is_rejected_inside_a_read_batch()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { WriteOp() });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void update_type_content_has_a_preview_description_naming_the_type()
    {
        var summary = BatchSafetySnapshot.DescribeOperation(WriteOp());

        Assert.Equal("Update PLC data type 'PLC_1/Types/AnalogInputSettings'.", summary);
    }

    [Fact]
    public void get_block_content_still_accepts_an_optional_format()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r2",
            Operation = "get_block_content",
            BlockPath = "PLC_1/Blocks/InputValues_DB",
            Format = "source",
        };

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void update_block_logic_still_accepts_an_optional_format()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w2",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/InputValues_DB",
            YamlContent = "--- FILE: x.xml ---\n<Document />",
            Format = "xml",
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TypeOperationCatalogTests"`
Expected: FAIL — compile error, `BatchOperationRequest` has no `TypePath`, `SourceContent`, or `Format`.

- [ ] **Step 3: Add the request fields**

In `TiaMcpServer/Batch/BatchOperationRequest.cs`, after the `ObEventClass` property (currently the last property, ending line 117), add:

```csharp

    [Description("PLC data type path, e.g. PLC_1/Types/AnalogInputSettings or PLC_1/Types/Folder/MyType. Required by get_type_content and update_type_content.")]
    public string? TypePath { get; set; }

    [Description("Siemens external-source text for the object. Required by update_type_content: a .udt declaration when format is source, or a Simatic ML document when format is xml.")]
    public string? SourceContent { get; set; }

    [Description("Document format. Valid values: source, xml. get_type_content and update_type_content default to source (.udt). get_block_content and update_block_logic default to xml and honor source for GlobalDB only.")]
    public string? Format { get; set; }
```

In `TiaMcpServer/Batch/BatchOperationRequest.cs`, extend the `Operation` property's `[Description]` so the tool schema advertises the new operations. Replace the read list `"...compile_check, get_project_status. "` with `"...compile_check, get_project_status, get_type_content. "`, and the write list `"...start_plc, stop_plc. "` with `"...start_plc, stop_plc, update_type_content. "`.

- [ ] **Step 4: Add the same three fields to the worker contract**

In `TiaMcpServer.Contracts/WorkerRequest.cs`, at the end of the `#region Block operations` block (immediately before its `#endregion`), add:

```csharp

    /// <summary>Forwarded by: get_type_content, update_type_content.</summary>
    public string? TypePath { get; set; }

    /// <summary>Forwarded by: update_type_content.</summary>
    public string? SourceContent { get; set; }

    /// <summary>
    /// Forwarded by: get_type_content, update_type_content, get_block_content,
    /// update_block_logic. Normalized by SourceFormatNames on the host before sending, so the
    /// worker never sees an unrecognized value.
    /// </summary>
    public string? Format { get; set; }
```

- [ ] **Step 5: Register the catalog specs**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`, in `BuildSpecs`, add to the Reads block after the `get_project_status` line:

```csharp
            new BatchOperationSpec("get_type_content", BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format" }),
```

Add to the Data writes block after the `stop_plc` line:

```csharp
            new BatchOperationSpec("update_type_content", BatchOperationCategory.Write, new[] { "typePath", "sourceContent" }, new[] { "format" }),
```

In the same method, add `format` as an optional field to the two existing block operations. Replace:

```csharp
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, None),
```

with:

```csharp
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format" }),
```

and replace:

```csharp
            new BatchOperationSpec("update_block_logic", BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }, None),
```

with:

```csharp
            new BatchOperationSpec("update_block_logic", BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }, new[] { "format" }),
```

- [ ] **Step 6: Add the preview description**

In `TiaMcpServer/Batch/BatchSafetySnapshot.cs`, in `DescribeOperation`, add above the `_ =>` default arm:

```csharp
        "update_type_content" => $"Update PLC data type '{op.TypePath}'.",
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TypeOperationCatalogTests"`
Expected: PASS — 13 tests passed.

- [ ] **Step 8: Run the whole suite to confirm nothing regressed**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS — all tests. `BatchToolsTests.BatchToolsHaveMcpMetadata` in particular must stay green; it asserts the tool surface.

- [ ] **Step 9: Commit**

```bash
git add TiaMcpServer.Contracts/WorkerRequest.cs \
        TiaMcpServer/Batch/BatchOperationRequest.cs \
        TiaMcpServer/Batch/BatchOperationCatalog.cs \
        TiaMcpServer/Batch/BatchSafetySnapshot.cs \
        TiaMcpServer.Tests/TypeOperationCatalogTests.cs
git commit -m "feat: register get_type_content and update_type_content batch operations"
```

---

### Task 6: Host-to-worker wiring

Connects the two operations through `OpennessWorkerClient` and `BatchWorkerInvoker`, including the current-state reading the safety token binds to.

**Files:**
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
- Modify: `TiaMcpServer/Batch/BatchPayloadBudget.cs`
- Test: `TiaMcpServer.Tests/TypeOperationInvokerTests.cs` (create)

**Interfaces:**
- Consumes: `SourceFormatNames.TryNormalize` (Task 1); `WorkerRequest.TypePath/.SourceContent/.Format` (Task 5).
- Produces: `OpennessWorkerClient.GetTypeContentAsync(string typePath, string? format, string? projectPath, CancellationToken)` and `OpennessWorkerClient.UpdateTypeContentAsync(string typePath, string sourceContent, string? format, string? projectPath, bool allowTiaConfirmations, CancellationToken)`, both returning `WorkerCallResult`. `BatchWorkerInvoker.BuildRequest(BatchOperationRequest op)` returning `WorkerRequest`.

- [ ] **Step 1: Read the surrounding code before editing**

Read these three, and match their existing shape exactly rather than inventing one:

```bash
grep -n "GetBlockContentAsync" -A 20 TiaMcpServer/Worker/OpennessWorkerClient.cs
grep -n "get_block_content\|update_block_logic" -B 4 -A 20 TiaMcpServer/Batch/BatchWorkerInvoker.cs
grep -n "get_block_content" -B 4 -A 8 TiaMcpServer/Batch/BatchPayloadBudget.cs
```

The new methods mirror `GetBlockContentAsync` / `UpdateBlockLogicAsync` — same `WorkerRequest` construction, same `SendAsync` call, same result handling. Do not introduce a new pattern.

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/TypeOperationInvokerTests.cs`. These are pure request-construction tests — no worker process involved. The FakeWorker end-to-end coverage is added separately in Step 8.

The test must assert four things:

```csharp
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests;

public class TypeOperationInvokerTests
{
    [Fact]
    public void Update_type_content_forwards_format_and_source_content()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
            SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
            Format = "source",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("update_type_content", request.Method);
        Assert.Equal("PLC_1/Types/AnalogInputSettings", request.TypePath);
        Assert.Equal("TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n", request.SourceContent);
        Assert.Equal("source", request.Format);
    }

    [Fact]
    public void Type_operations_default_format_to_source_when_omitted()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r1",
            Operation = "get_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("source", request.Format);
    }

    [Fact]
    public void Block_operations_default_format_to_xml_when_omitted()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r2",
            Operation = "get_block_content",
            BlockPath = "PLC_1/Blocks/Main",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("xml", request.Format);
    }

    [Fact]
    public void An_invalid_format_is_rejected_before_the_session_binds()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r3",
            Operation = "get_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
            Format = "s7dcl",
        };

        var ex = Assert.Throws<ArgumentException>(() => BatchWorkerInvoker.BuildRequest(op));

        Assert.Contains("s7dcl", ex.Message);
    }
}
```

If `BatchWorkerInvoker` has no `BuildRequest` seam, extract one as part of this task — a `public static WorkerRequest BuildRequest(BatchOperationRequest op)` that the existing invoke path calls. That extraction is the point: it makes request construction testable without a worker process, exactly as `BatchSafetySnapshot` made snapshot construction testable without one.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TypeOperationInvokerTests"`
Expected: FAIL — compile error or assertion failure.

- [ ] **Step 4: Implement the client methods**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`, add beside `GetBlockContentAsync`:

```csharp
    public Task<WorkerCallResult> GetTypeContentAsync(
        string typePath,
        string? format,
        string? projectPath,
        CancellationToken cancellationToken)
        => SendAsync(
            new WorkerRequest
            {
                Method = "get_type_content",
                TypePath = typePath,
                Format = format,
                ProjectPath = projectPath,
            },
            cancellationToken);

    public Task<WorkerCallResult> UpdateTypeContentAsync(
        string typePath,
        string sourceContent,
        string? format,
        string? projectPath,
        bool allowTiaConfirmations,
        CancellationToken cancellationToken)
        => SendAsync(
            new WorkerRequest
            {
                Method = "update_type_content",
                TypePath = typePath,
                SourceContent = sourceContent,
                Format = format,
                ProjectPath = projectPath,
                AllowTiaConfirmations = allowTiaConfirmations,
            },
            cancellationToken);
```

Adjust the `SendAsync` call shape to match whatever the neighboring methods actually use — the point is that these two carry no bespoke logic.

- [ ] **Step 5: Implement format normalization and dispatch in the invoker**

In `TiaMcpServer/Batch/BatchWorkerInvoker.cs`, add format normalization to request construction:

```csharp
    private static string NormalizeFormat(BatchOperationRequest op)
    {
        var fallback = op.Operation is "get_type_content" or "update_type_content"
            ? SourceFormatNames.Source
            : SourceFormatNames.Xml;

        if (!SourceFormatNames.TryNormalize(op.Format, fallback, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(op));
        }

        return normalized;
    }
```

Add the two dispatch arms alongside the existing `get_block_content` / `update_block_logic` arms, forwarding `TypePath`, `SourceContent`, and the normalized format.

- [ ] **Step 6: Add the read to the payload budget**

In `TiaMcpServer/Batch/BatchPayloadBudget.cs`, register `get_type_content` wherever `get_block_content` is registered, with the same treatment. A `.udt` payload is far smaller than a SimaticML bundle, so no new budget class is needed — it only needs to be accounted for rather than unbudgeted.

- [ ] **Step 7: Add the current-state reading**

`update_type_content`'s safety-token binding must be the type's current exported source. In `BatchWorkerInvoker`, wherever per-item current state is gathered for write operations, add an arm for `update_type_content` that calls `GetTypeContentAsync` with the same normalized format and uses the returned payload as the current-state string. This is what makes an edit inside TIA Portal between preview and apply invalidate the token.

- [ ] **Step 8: Add FakeWorker end-to-end coverage**

The spec requires the host-side batch path to be covered end to end without TIA Portal. Find the existing FakeWorker tests and copy their setup verbatim:

```bash
grep -rln "FakeWorker" TiaMcpServer.Tests/
```

Add a test class `TypeOperationFakeWorkerTests` that scripts a FakeWorker response for each new operation and asserts three things the `BuildRequest` unit tests cannot reach:

1. `execute_read_batch` with a `get_type_content` item returns the scripted `.udt` payload in the result keyed by its `operationId`.
2. `preview_write_batch` with an `update_type_content` item returns a `safetyToken` and a preview containing the text `Update PLC data type 'PLC_1/Types/AnalogInputSettings'.`
3. `apply_write_batch` with that token and `confirm=true` succeeds, and replaying the same token a second time is rejected — tokens are single-use.

Follow whatever scripting format the existing FakeWorker tests use; do not invent a second one.

- [ ] **Step 9: Run tests**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS — all tests including the four `TypeOperationInvokerTests` and the three `TypeOperationFakeWorkerTests`.

- [ ] **Step 10: Commit**

```bash
git add TiaMcpServer/Worker/OpennessWorkerClient.cs \
        TiaMcpServer/Batch/BatchWorkerInvoker.cs \
        TiaMcpServer/Batch/BatchPayloadBudget.cs \
        TiaMcpServer.Tests/TypeOperationInvokerTests.cs \
        TiaMcpServer.Tests/TypeOperationFakeWorkerTests.cs
git commit -m "feat: wire type operations through the worker client and batch invoker"
```

---

### Task 7: Worker Openness shells

The `Siemens.Engineering`-touching half. **These files must NOT be linked into the test project.** Their only coverage is Task 8's live harness — which is why Task 8 is a gate rather than a formality.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/PlcTypeTargetResolver.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/ExternalSourceScope.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/PlcTypePostconditionVerifier.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`

**Interfaces:**
- Consumes: `PlcTypeAddress` (Task 2), `SourceTextEncoding` (Task 3), `PlcTypeSourcePreflight` (Task 4), `WorkerRequest.TypePath/.SourceContent/.Format` (Task 5).
- Produces: worker methods `get_type_content` (returns raw document text) and `update_type_content` (returns a mutation result).

**API note.** The Openness surface below was confirmed by static inspection of `Siemens.Engineering.Step7.dll` v21.0.0.0 (roadmap Phase 0). Exact overload shapes are **not** proven until Task 8 runs. If a signature does not match, fix the call — do not redesign around it, and do not silently fall back to a different pipeline.

- [ ] **Step 1: Read the two files being mirrored**

```bash
cat TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs
cat TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs
```

`PlcTypeTargetResolver` mirrors `BlockTargetResolver` exactly — deterministic walk plus fuzzy fallback with an ambiguity error. `PlcTypeExporter` mirrors `BlockExporter`'s temp-directory pattern.

- [ ] **Step 2: Write `ExternalSourceScope`**

This is the highest-risk file in the phase: `ExternalSources.CreateFromFile` adds a visible node to the user's project, and it must always come back out.

Create `TiaMcpServer.OpennessWorker/Openness/ExternalSourceScope.cs`:

```csharp
using System;
using System.IO;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.ExternalSources;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Owns the temp file and the PlcExternalSource project node created for one import, and
/// guarantees both are gone afterwards.
///
/// <para>
/// ExternalSources.CreateFromFile adds a node under the PLC's "External source files" folder —
/// a visible, persistent change to the user's project that has nothing to do with what they asked
/// for. Every import path must dispose this scope, and PlcTypePostconditionVerifier asserts no
/// residual node survived.
/// </para>
/// </summary>
internal sealed class ExternalSourceScope : IDisposable
{
    private readonly string _tempDirectory;
    private PlcExternalSource? _source;
    private bool _disposed;

    private ExternalSourceScope(string tempDirectory, PlcExternalSource source, string filePath)
    {
        _tempDirectory = tempDirectory;
        _source = source;
        FilePath = filePath;
    }

    public PlcExternalSource Source =>
        _source ?? throw new ObjectDisposedException(nameof(ExternalSourceScope));

    public string FilePath { get; }

    /// <summary>Writes <paramref name="content"/> to a temp file and registers it with the PLC.</summary>
    public static ExternalSourceScope Create(PlcSoftware plcSoftware, string fileName, string content)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var filePath = Path.Combine(tempDirectory, fileName);
            File.WriteAllBytes(filePath, SourceTextEncoding.ForFile(content));

            var sourceName = Path.GetFileNameWithoutExtension(fileName)
                + "_tiamcp_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var source = plcSoftware.ExternalSourceGroup.ExternalSources.CreateFromFile(
                sourceName, filePath);

            return new ExternalSourceScope(tempDirectory, source, filePath);
        }
        catch
        {
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    /// <summary>True once the project node is gone. Read by the postcondition verifier.</summary>
    public bool ProjectNodeRemoved { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _source?.Delete();
            ProjectNodeRemoved = true;
        }
        catch (Exception ex)
        {
            // Surfaced as a worker warning rather than swallowed: a surviving node is a real,
            // user-visible change to their project that they need to know about.
            Console.Error.WriteLine(
                $"Failed to remove the temporary external source node from the project: {ex.Message}");
        }
        finally
        {
            _source = null;
            TryDeleteDirectory(_tempDirectory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless; a leftover project node is not.
        }
    }
}
```

- [ ] **Step 3: Write `PlcTypeTargetResolver`**

Create `TiaMcpServer.OpennessWorker/Openness/PlcTypeTargetResolver.cs`. Mirror `BlockTargetResolver`'s structure exactly — read it in Step 1 and follow it — resolving against `plcSoftware.TypeGroup` and `unit.TypeGroup` instead of block groups. It returns:

```csharp
internal sealed class ResolvedTypeTarget
{
    public ResolvedTypeTarget(PlcTypeGroup group, PlcType? type, string documentName)
    {
        Group = group;
        Type = type;
        DocumentName = documentName;
    }

    public PlcTypeGroup Group { get; }

    public PlcType? Type { get; }

    public string DocumentName { get; }

    /// <summary>
    /// GenerateBlocksFromSource targets a PlcTypeUserGroup; the root PlcTypeSystemGroup is not one.
    /// Live test L1.1 exists to establish whether the root case needs the parameterless overload.
    /// </summary>
    public PlcTypeUserGroup? UserGroup => Group as PlcTypeUserGroup;
}
```

Expose `ResolveForExport(Project, PlcTypeAddress)` and `ResolveForImport(Project, PlcTypeAddress)`, matching `BlockTargetResolver`'s two-method shape and its ambiguity error message for non-deterministic paths matching more than one type.

- [ ] **Step 4: Write `PlcTypeExporter`**

Create `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Exports one PlcType as either Siemens external-source text (.udt) or Simatic ML.
///
/// <para>
/// Returns raw text with no bundle envelope: unlike a block export, which carries an .xml plus a
/// companion .s7dcl/.s7res pair and therefore needs BlockBundleFormat's delimiters, a type export
/// is a single document with nothing to delimit.
/// </para>
/// </summary>
internal static class PlcTypeExporter
{
    public static string Export(Project project, string typePath, string format)
    {
        var address = PlcTypeAddress.Parse(typePath);
        var target = PlcTypeTargetResolver.ResolveForExport(project, address);

        if (target.Type is null)
        {
            throw new WorkerOperationException(
                $"No PLC data type was found at '{address.ToDisplayPath()}'.");
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-type-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
                ? ExportXml(target, tempDirectory)
                : ExportSource(project, target, tempDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static string ExportSource(Project project, ResolvedTypeTarget target, string tempDirectory)
    {
        var path = Path.Combine(tempDirectory, target.DocumentName + ".udt");
        var plcSoftware = PlcSoftwareLocator.ForType(project, target.Type!);

        plcSoftware.ExternalSourceGroup.GenerateSource(
            new List<IGenerateSource> { target.Type! },
            new FileInfo(path));

        if (!File.Exists(path))
        {
            throw new WorkerOperationException(
                $"TIA Portal reported no error but produced no source file for "
                + $"'{target.DocumentName}'. Compile the type in TIA Portal and try again.");
        }

        return SourceTextEncoding.ForTransport(File.ReadAllText(path));
    }

    private static string ExportXml(ResolvedTypeTarget target, string tempDirectory)
    {
        var path = Path.Combine(tempDirectory, target.DocumentName + ".xml");
        target.Type!.Export(new FileInfo(path), ExportOptions.None);

        return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(path));
    }
}
```

`PlcSoftwareLocator.ForType` may not exist yet — check `PlcSoftwareLocator.cs` first and add the helper there if it does not, rather than duplicating traversal logic in the exporter.

- [ ] **Step 5: Write `PlcTypeImporter`**

Create `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs`. It must, in order:

1. Parse the path and resolve the target.
2. **Refuse if the type does not exist** — `update_type_content` is strict, never an upsert.
3. Read the declared name via `PlcTypeSourcePreflight.TryReadDeclaredName` and **refuse if it does not match** the target type's name, quoting both.
4. For `xml`: stage to a temp file and call `target.Group.Types.Import(fileInfo, ImportOptions.Override)`.
5. For `source`: open an `ExternalSourceScope`, then call `GenerateBlocksFromSource`, preferring the `PlcTypeUserGroup` overload when `target.UserGroup` is non-null and the parameterless overload otherwise. Dispose the scope in a `finally`.
6. Return a result carrying the scope's `ProjectNodeRemoved` flag so the verifier can assert on it.

The two refusals in steps 2 and 3 are the whole point of the strict design — implement them before the happy path, and make each error name both the expected and the actual value.

- [ ] **Step 6: Write `PlcTypePostconditionVerifier`**

Create `TiaMcpServer.OpennessWorker/Openness/PlcTypePostconditionVerifier.cs`, mirroring `BlockPostconditionVerifier` and returning the existing `BlockPostconditionEvidence`. It re-exports the type and compiles the PLC. Compiling is what surfaces dependent blocks the type change invalidated — that is deliberately cheaper and truer than pre-counting cross-references at preview time. It must also record a warning when `ProjectNodeRemoved` is false.

- [ ] **Step 7: Add the dispatch cases**

In `TiaMcpServer.OpennessWorker/Program.cs`, add two methods beside `GetBlockContent` (line 261) and `UpdateBlockLogic` (line 271), following their exact shape — `WithProject`, `RawPayload` for the read, `Success` for the write — and register both in the `HandleLine` switch (lines 85-149).

- [ ] **Step 8: Verify both build configurations**

Run in **PowerShell**, not Bash — the `/p:` flag requires it:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: build succeeded, 0 errors.

Then the local build against real TIA assemblies:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: build succeeded, 0 errors.

If the stub build fails but the local build succeeds, the stubs lack a member the real assemblies have — report it rather than working around it, since CI depends on the stub build.

- [ ] **Step 9: Confirm the new files are NOT linked into the test project**

```bash
grep -c "PlcTypeExporter\|PlcTypeImporter\|PlcTypeTargetResolver\|ExternalSourceScope\|PlcTypePostconditionVerifier" TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
```

Expected: `0`. A non-zero count means a Siemens-touching file was linked and the test project will fail to compile.

- [ ] **Step 10: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS — all tests.

- [ ] **Step 11: Commit**

```bash
git add TiaMcpServer.OpennessWorker/
git commit -m "feat: add PlcType export and import through the Openness external-source pipeline"
```

---

### Task 8: Phase 1 live gate — `scripts/live-test-udt.ps1`

**This task blocks Phase 2.** It is the only coverage the five files from Task 7 will ever have.

**Files:**
- Create: `scripts/live-test-udt.ps1`
- Reference: `scripts/verify-doctor-package.ps1` — match its parameter handling and output style.

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: pass/fail evidence per check ID L1.1-L1.7.

**Prerequisite — needs the user.** This step cannot be completed by an agent alone. It requires TIA Portal V21 running with a project open that contains at least one UDT at the Types root and one in a nested type folder. Ask the user to confirm both exist and to supply the project path before writing the script; if only a root-level type exists, ask them to create a nested one, because L1.1 is the check most likely to fail.

- [ ] **Step 1: Write the harness**

Create `scripts/live-test-udt.ps1`. It pipes newline-delimited JSON into the built worker and asserts on the responses:

```powershell
#Requires -Version 7
<#
.SYNOPSIS
    Live round-trip test for get_type_content / update_type_content against real TIA Portal V21.

.DESCRIPTION
    Talks directly to TiaMcpServer.OpennessWorker.exe over newline-delimited JSON, bypassing the
    MCP host. This is the only coverage the Siemens-touching worker files have: they cannot be
    unit-tested because TiaMcpServer.Tests has no Siemens reference.

    Requires TIA Portal V21 running with the target project open.

.PARAMETER ProjectPath
    Absolute path to the .ap21 project file.

.PARAMETER RootTypePath
    A UDT directly under Types, e.g. PLC_1/Types/AnalogInputSettings.

.PARAMETER NestedTypePath
    A UDT inside a type folder, e.g. PLC_1/Types/Sensors/AnalogInputSettings.
    Exercises the PlcTypeUserGroup overload, which the root type does not.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $RootTypePath,
    [Parameter(Mandatory)] [string] $NestedTypePath,
    [string] $WorkerPath = "TiaMcpServer/bin/Debug/net8.0/openness-worker/TiaMcpServer.OpennessWorker.exe"
)

$ErrorActionPreference = 'Stop'
$script:Failures = @()

function Invoke-Worker {
    param([hashtable] $Request)
    $json = $Request | ConvertTo-Json -Compress -Depth 10
    $response = $json | & $WorkerPath | Select-Object -First 1
    if (-not $response) { throw "Worker returned no response for method '$($Request.method)'." }
    return $response | ConvertFrom-Json
}

function Assert-Check {
    param([string] $Id, [string] $Description, [scriptblock] $Test)
    Write-Host "[$Id] $Description ... " -NoNewline
    try {
        & $Test
        Write-Host "PASS" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL" -ForegroundColor Red
        Write-Host "      $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures += "$Id — $Description — $($_.Exception.Message)"
    }
}

function Get-TypeSource {
    param([string] $TypePath, [string] $Format = 'source')
    $response = Invoke-Worker @{
        method      = 'get_type_content'
        projectPath = $ProjectPath
        typePath    = $TypePath
        format      = $Format
    }
    if (-not $response.success) { throw "get_type_content failed: $($response.error)" }
    return $response.payload
}

function Set-TypeSource {
    param([string] $TypePath, [string] $Content, [string] $Format = 'source')
    return Invoke-Worker @{
        method                = 'update_type_content'
        projectPath           = $ProjectPath
        typePath              = $TypePath
        sourceContent         = $Content
        format                = $Format
        allowTiaConfirmations = $true
    }
}

# --- L1.1 both group kinds export -------------------------------------------------
$rootOriginal = $null
$nestedOriginal = $null

Assert-Check 'L1.1a' 'Export a type at the Types root' {
    $script:rootOriginal = Get-TypeSource -TypePath $RootTypePath
    if ($script:rootOriginal -notmatch '(?m)^\s*TYPE\b') { throw 'Payload is not a TYPE declaration.' }
}

Assert-Check 'L1.1b' 'Export a type in a nested type folder' {
    $script:nestedOriginal = Get-TypeSource -TypePath $NestedTypePath
    if ($script:nestedOriginal -notmatch '(?m)^\s*TYPE\b') { throw 'Payload is not a TYPE declaration.' }
}

# --- L1.2 unchanged round trip is lossless ----------------------------------------
Assert-Check 'L1.2' 'Unchanged round trip re-exports byte-identically' {
    $result = Set-TypeSource -TypePath $NestedTypePath -Content $script:nestedOriginal
    if (-not $result.success) { throw "update_type_content failed: $($result.error)" }
    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $script:nestedOriginal) { throw 'Re-export differs from the original.' }
}

# --- L1.3 a real edit applies ------------------------------------------------------
Assert-Check 'L1.3' 'A modified initial value survives the round trip' {
    if ($script:nestedOriginal -notmatch ':=\s*(\d+)') {
        throw 'Fixture type has no numeric initial value to mutate. Pick a different NestedTypePath.'
    }
    $original = $Matches[1]
    $mutant = [int]$original + 1
    $edited = $script:nestedOriginal -replace ":=\s*$original\b", ":= $mutant"

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $edited
    if (-not $result.success) { throw "update_type_content failed: $($result.error)" }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -notmatch ":=\s*$mutant\b") { throw "Edited value $mutant is absent after re-export." }
}

# --- L1.4 no residual external source node ----------------------------------------
Assert-Check 'L1.4' 'No residual PlcExternalSource node remains' {
    $tree = Invoke-Worker @{
        method      = 'browse_project_tree'
        projectPath = $ProjectPath
    }
    if (-not $tree.success) { throw "browse_project_tree failed: $($tree.error)" }
    $rendered = $tree | ConvertTo-Json -Depth 30
    if ($rendered -match '_tiamcp_') { throw 'A temporary external source node survived in the project.' }
}

# --- L1.5 strict preflight ---------------------------------------------------------
Assert-Check 'L1.5a' 'A name mismatch is rejected and changes nothing' {
    $before = Get-TypeSource -TypePath $NestedTypePath
    $wrongName = $script:nestedOriginal -replace '(?m)^(\s*TYPE\s+)("?)([A-Za-z_][A-Za-z0-9_]*)\2', '$1"NotTheTargetName"'

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $wrongName
    if ($result.success) { throw 'Name mismatch was accepted; the write should be strict.' }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $before) { throw 'Project changed despite the rejection.' }
}

Assert-Check 'L1.5b' 'A nonexistent type path is rejected' {
    $result = Set-TypeSource -TypePath 'PLC_1/Types/DefinitelyNotARealType' -Content $script:nestedOriginal
    if ($result.success) { throw 'Nonexistent type was accepted; update must never create.' }
}

# --- L1.6 xml fallback stays reachable ---------------------------------------------
Assert-Check 'L1.6' 'format=xml round-trips' {
    $xml = Get-TypeSource -TypePath $NestedTypePath -Format 'xml'
    if ($xml -notmatch '<Document') { throw 'format=xml did not return a Simatic ML document.' }

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $xml -Format 'xml'
    if (-not $result.success) { throw "xml import failed: $($result.error)" }
}

# --- L1.7 restore and compile ------------------------------------------------------
Assert-Check 'L1.7a' 'Original content is restored byte-identically' {
    $result = Set-TypeSource -TypePath $NestedTypePath -Content $script:nestedOriginal
    if (-not $result.success) { throw "restore failed: $($result.error)" }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $script:nestedOriginal) { throw 'Restored content differs from the original.' }
}

Assert-Check 'L1.7b' 'Project compiles without errors' {
    $result = Invoke-Worker @{ method = 'compile_check'; projectPath = $ProjectPath }
    if (-not $result.success) { throw "compile_check failed: $($result.error)" }
}

# --- summary ------------------------------------------------------------------------
Write-Host ''
if ($script:Failures.Count -eq 0) {
    Write-Host 'All Phase 1 live checks passed.' -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures.Count) check(s) FAILED:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'L1.1 and L1.4 are blocking. If either failed, do not start Phase 2.' -ForegroundColor Yellow
exit 1
```

- [ ] **Step 2: Build the worker so the harness has something to run**

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: build succeeded. Confirm `TiaMcpServer.OpennessWorker.exe` exists under `openness-worker/`.

- [ ] **Step 3: Ask the user to run the harness**

The agent cannot do this step. Ask the user to open their project in TIA Portal V21 and run:

```powershell
pwsh scripts/live-test-udt.ps1 `
    -ProjectPath "C:\path\to\Project.ap21" `
    -RootTypePath "PLC_1/Types/AnalogInputSettings" `
    -NestedTypePath "PLC_1/Types/Sensors/AnalogInputSettings"
```

Ask them to paste the full output back.

- [ ] **Step 4: Evaluate the gate honestly**

- **All checks pass** → Phase 2 starts.
- **L1.1b or L1.4 fails** → **STOP.** Do not start Phase 2, and do not paper over it. L1.1b failing means `GenerateBlocksFromSource` will not accept the resolved group and the import design needs rework. L1.4 failing means imports leave debris in the user's project, which is a correctness bug, not a cosmetic one.
- **Any other check fails** → fix the specific cause and re-run the whole harness. Do not weaken an assertion to make it pass.

Report the actual output. If evidence is thinner than the claim, get more evidence rather than softening the wording.

- [ ] **Step 5: Commit**

```bash
git add scripts/live-test-udt.ps1
git commit -m "test: add Phase 1 live round-trip harness for PLC data types"
```

---

## PHASE 2 — DB

**Do not start until Task 8's gate is green.**

### Task 9: `DbSourceOffsetColumn`

Non-optimized DBs export a per-variable byte offset column. A client that adds, removes, or reorders a member leaves every subsequent offset stale.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/DbSourceOffsetColumn.cs`
- Create: `TiaMcpServer.Tests/DbSourceOffsetColumnTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `static bool HasOffsetColumn(string dbSource)`.

**Prerequisite — needs the user.** The `Simulation_DB.db` fixture is an **optimized** DB and therefore has no offset column. Before writing this task, ask the user to export one **non-optimized** DB from TIA Portal V21 to `priv/tia_exports/`, then copy it into `TiaMcpServer.Tests/Fixtures/`. Without it, the detector is written against a guess at the offset syntax. Do not proceed on a guess — ask.

- [ ] **Step 1: Obtain and inspect the non-optimized fixture**

Once the user supplies it:

```bash
head -30 priv/tia_exports/<NonOptimized>.db
```

Write the detector against the syntax actually present, not an assumed one.

- [ ] **Step 2: Register the file and fixture in the test project**

```xml
    <None Include="Fixtures\NonOptimized_DB.db" CopyToOutputDirectory="PreserveNewest" />
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\DbSourceOffsetColumn.cs"
      Link="Linked\Openness\DbSourceOffsetColumn.cs" />
```

- [ ] **Step 3: Write the failing test**

Create `TiaMcpServer.Tests/DbSourceOffsetColumnTests.cs`:

```csharp
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class DbSourceOffsetColumnTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void An_optimized_db_export_has_no_offset_column()
    {
        var content = File.ReadAllText(FixturePath("Simulation_DB.db"));

        Assert.False(DbSourceOffsetColumn.HasOffsetColumn(content));
    }

    [Fact]
    public void A_non_optimized_db_export_has_an_offset_column()
    {
        var content = File.ReadAllText(FixturePath("NonOptimized_DB.db"));

        Assert.True(DbSourceOffsetColumn.HasOffsetColumn(content));
    }

    [Fact]
    public void Empty_content_has_no_offset_column()
    {
        Assert.False(DbSourceOffsetColumn.HasOffsetColumn(string.Empty));
    }

    [Fact]
    public void A_udt_source_has_no_offset_column()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.udt"));

        Assert.False(DbSourceOffsetColumn.HasOffsetColumn(content));
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~DbSourceOffsetColumnTests"`
Expected: FAIL — compile error, `DbSourceOffsetColumn` does not exist.

- [ ] **Step 5: Write the implementation against the real fixture syntax**

Create `TiaMcpServer.OpennessWorker/Openness/DbSourceOffsetColumn.cs` with a `HasOffsetColumn` that matches the offset syntax observed in Step 1. Keep it a single compiled `Regex` with an XML doc comment explaining what a stale offset means, matching `PlcTypeSourcePreflight`'s style.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~DbSourceOffsetColumnTests"`
Expected: PASS — 4 tests passed.

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/DbSourceOffsetColumn.cs \
        TiaMcpServer.Tests/DbSourceOffsetColumnTests.cs \
        TiaMcpServer.Tests/Fixtures/NonOptimized_DB.db \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: detect the byte-offset column in non-optimized DB sources"
```

---

### Task 10: DB source export and import

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs:52-104`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs` (forward `request.Format`)
- Test: covered by Task 11's live harness plus the existing catalog tests from Task 5.

**Interfaces:**
- Consumes: `ExternalSourceScope`, `PlcTypeSourcePreflight`, `SourceTextEncoding` (Phase 1); `DbSourceOffsetColumn` (Task 9).
- Produces: `format: "source"` honored on `get_block_content` and `update_block_logic` for `GlobalDB`.

- [ ] **Step 1: Add the format parameter to `BlockExporter.Export`**

Change the signature from `Export(Project project, string blockPath)` to `Export(Project project, string blockPath, string format)`. When `format` is `xml` — the default for blocks — the existing body runs **completely unchanged**, so every current caller keeps its exact behavior. This is a hard requirement, not a preference: `get_block_content` without `format` must return byte-identical output to today.

When `format` is `source`, branch to a new `ExportSource` that:

1. Rejects any block that is not a `GlobalDB`, with an error naming the actual block type and the formats valid for it.
2. Calls `plcSoftware.ExternalSourceGroup.GenerateSource(new List<IGenerateSource> { dataBlock }, new FileInfo(path))` — the same call `PlcTypeExporter` makes.
3. Returns `SourceTextEncoding.ForTransport(File.ReadAllText(path))`, raw and unbundled.

- [ ] **Step 2: Add the source route to the import coordinator**

In `BlockImportCoordinator`, add a route taken when `format` is `source`. It mirrors `PlcTypeImporter` exactly: reject non-`GlobalDB`; read the declared name via `PlcTypeSourcePreflight.TryReadDeclaredName` (which already handles `DATA_BLOCK`); refuse on mismatch or missing target; then open an `ExternalSourceScope` and call `GenerateBlocksFromSource` against the `PlcBlockUserGroup`, disposing in a `finally`.

Before writing to a non-optimized DB, call `DbSourceOffsetColumn.HasOffsetColumn` and attach a warning that offsets are valid only for the member layout they were generated from. Live test L2.4 decides whether this stays a warning or becomes a hard error — implement the warning now.

- [ ] **Step 3: Forward the format from the dispatcher**

In `Program.cs`, pass `request.Format` into both `BlockExporter.Export` and the import coordinator. The host already normalizes it (Task 6), so the worker never sees an unrecognized value.

- [ ] **Step 4: Verify both builds**

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: both succeed, 0 errors.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS — all tests. The existing `get_block_content` bundle-format tests are the regression guard for Step 1's "unchanged when xml" requirement; if any of them fail, the default path was altered and must be restored.

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/
git commit -m "feat: support format=source for global data blocks"
```

---

### Task 11: Phase 2 live gate — `scripts/live-test-db.ps1`

**Files:**
- Create: `scripts/live-test-db.ps1`

**Prerequisite — needs the user.** Requires a project open in TIA Portal V21 containing **both** an optimized and a non-optimized global DB. Ask the user to confirm both exist and supply both paths.

- [ ] **Step 1: Write the harness**

Create `scripts/live-test-db.ps1` by adapting `scripts/live-test-udt.ps1`: same `Invoke-Worker` / `Assert-Check` helpers, same structure, but calling `get_block_content` / `update_block_logic` with `blockPath` and `format` instead of `typePath`. Parameters: `-ProjectPath`, `-OptimizedDbPath`, `-NonOptimizedDbPath`, `-InstanceDbPath`, `-FunctionBlockPath`.

Checks:

| ID | Assertion |
|---|---|
| L2.1 | `get_block_content` **without** `format` returns a payload containing `--- FILE:` and `<Document` — i.e. today's bundle, unchanged. **Blocking.** |
| L2.2 | Optimized DB: `format=source` exports, re-imports unchanged, and re-exports byte-identically. **Blocking.** |
| L2.3 | Non-optimized DB: same round trip. *(Amended — the offset-column half of this assertion was removed; see below.)* |
| ~~L2.4~~ | **RETIRED — do not look for an L2.4 result; one will never exist.** See the amendment below. |
| L2.5 | `format=source` on the instance DB and on the FB are each rejected with an error naming the block type. |
| L2.6 | No residual `_tiamcp_` external source node (same check as L1.4). |
| L2.7 | Original content restored; project compiles clean. |

- [ ] **Step 2: Ask the user to run it**

```powershell
pwsh scripts/live-test-db.ps1 `
    -ProjectPath "C:\path\to\Project.ap21" `
    -OptimizedDbPath "PLC_1/Blocks/InputValues_DB" `
    -NonOptimizedDbPath "PLC_1/Blocks/Legacy_DB" `
    -InstanceDbPath "PLC_1/Blocks/Inputs_FB_DB" `
    -FunctionBlockPath "PLC_1/Blocks/Inputs_FB"
```

- [x] ~~**Step 3: Apply the L2.4 contingency**~~ — **RETIRED, along with Task 9 and check L2.4.**

> **Amendment (2026-07-27), by user decision.** This plan assumed non-optimized DBs export a
> per-variable byte-offset column. The user, who owns the TIA Portal V21 install, established
> that they do not: **a non-optimized DB's external-source export is identical in shape to an
> optimized one, with no `Offset` column.** The only difference is the
> `S7_Optimized_Access := 'FALSE'` header attribute.
>
> Consequently: **Task 9 (`DbSourceOffsetColumn`) was never built**, Task 10 ships no offset
> detection and no optimized/non-optimized branching (one identical code path), check **L2.4 is
> retired permanently**, and L2.3 keeps only its byte-identical round-trip assertion. The `L2.4`
> ID is **not** reused — a result labelled L2.4 would be ambiguous between this retired check and
> anything later given the same number.
>
> The delivered harness also carries checks this table does not list, added after review found
> the gate could pass vacuously: **L2.2c** (a mutated initial value survives the round trip —
> without it every write in the gate was a no-op and a total write failure was undetectable),
> **L2.5e/L2.5f** (name-mismatch and nonexistent-path refusals), **L2.8** (software-unit-scoped
> DB, optional via `-UnitScopedDbPath`), and **L2.9** (a post-run sweep that fails the gate on
> stray-write or residual-node warnings). See `.superpowers/sdd/progress.md` for the full record.

- [ ] **Step 4: Handle a failed L2.3**

If the non-optimized round trip does not survive, ship **optimized-DB-only** support: reject `format=source` for non-optimized DBs with an explicit error, and document the limitation in the roadmap. Do not ship a lossy round trip.

- [ ] **Step 5: Commit**

```bash
git add scripts/live-test-db.ps1 TiaMcpServer.OpennessWorker/ TiaMcpServer.Tests/
git commit -m "test: add Phase 2 live round-trip harness for global data blocks"
```

---

### Task 12: Documentation

**Files:**
- Modify: `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Apply the two roadmap corrections**

In `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md`, in the "Sample export analysis" bullet beginning "**UDT and DB samples confirm the same shape found earlier**", replace the claim that `.udt`/`.scl` are "informal/decoded names for the same declaration syntax" with the corrected finding:

> `.udt` and `.s7dcl` are different formats produced by different Openness pipelines, not two names for one syntax. The `.udt` (external source, `GenerateSource`) opens `TYPE "AnalogInputSettings"` with a `VERSION` line and keeps comments inline as `//`. The `.s7dcl` (`ExportAsDocuments`) opens a bare `TYPE` with the name on the STRUCT line, encodes attributes as `{ S7_MLC := "MLC_aC" }`, and externalizes every comment to a companion `.s7res`. Their byte counts are similar; their syntaxes are unrelated. `.udt` is the better client format: one file, comments in place, no ID indirection.

In the phasing table, change Phase 2's "Depends on" cell from "Phase 1 (shared struct-parsing groundwork), Phase 0" to "Phase 1 (`ExternalSourceScope` and the declared-name preflight), Phase 0", and add a sentence to the Phase 2 bullet in the prose above it:

> With the native external-source pipeline confirmed in Phase 0, neither phase parses a struct. What Phase 2 reuses from Phase 1 is the temp-file and `PlcExternalSource` lifecycle helper plus the declared-name preflight.

- [ ] **Step 2: Record the Phase 4 evidence Phase 1 produced**

Add to the Phase 0 section, since Task 8 answers part of what Phase 0 left open:

> **Phase 0 partially closed (see `docs/superpowers/plans/2026-07-26-udt-db-external-source.md`).** The `GenerateSource → CreateFromFile → GenerateBlocksFromSource` round trip is proven live for UDTs by `scripts/live-test-udt.ps1` and for global DBs by `scripts/live-test-db.ps1`. It remains unproven for SCL blocks; Phase 3 must close that itself.

- [ ] **Step 3: Update the project overview**

In `CLAUDE.md`, the "Project overview" says the server "Exposes 10 tools". That number is still correct — the new operations are batch catalog entries, not tools. Instead, add to the "Write safety model" section:

> - **Type writes** (`update_type_content`): batch data writes like any other, but strict — the type must already exist and the declared name in `sourceContent` must match the target. Openness would otherwise create a new type from an unrecognized name.

- [ ] **Step 4: Commit**

```bash
git add docs/EXPORT_IMPORT_FORMAT_ROADMAP.md CLAUDE.md
git commit -m "docs: correct .udt vs .s7dcl finding and record Phase 1-2 live evidence"
```

---

## Deferred, deliberately

Not in this plan; each needs its own decision.

- **`create_type` / `delete_type` / type groups.** Phase 1 is read + strict update only.
- **The `browse_project_tree` → Types dead-end.** `ProjectTreeWalker` prints `PLC_1/Types/<name>` paths that `BlockAddress.Parse` rejects. Closing it means teaching `get_block_content` to sniff a Types path and return an error naming `get_type_content`. Standalone item.
- **Flipping any default.** Roadmap Phase 5 owns that for every block language at once.
- **SCL and LAD.** Roadmap Phases 3 and 4.
