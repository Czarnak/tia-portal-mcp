# PR #27 Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the six accepted PR #27 review findings without widening the structured I/O-map feature or fabricating live-TIA evidence.

**Architecture:** Keep the existing host/worker contract unchanged. Add degradation boundaries at the two optional worker-enrichment seams, make logical-address formatting validate against the parser's representable interval, make the FakeWorker model the request selectors it is meant to verify, and downgrade the historical live report to the evidence the repository actually contains.

**Tech Stack:** C# 12, .NET 8 host/tests, .NET Framework 4.8 Openness worker, xUnit, System.Text.Json, PowerShell 7 documentation checks.

## Global Constraints

- Work on the checked-out PR branch; do not create or switch branches or worktrees.
- Use TDD for every production behavior change: add one regression, run it and observe the expected failure, then implement the smallest fix and rerun it.
- Catch only `EngineeringException` at Siemens Openness degradation boundaries; unexpected programming failures must remain visible.
- `includeTagMatches` and `includeIoDetails` are optional enrichment. Failure to enumerate their Siemens data must not remove otherwise readable base hardware data.
- `FormatLogicalAddress` may return a string only when `TryParse` recovers the exact same area, start bit, and width.
- FakeWorker's `network-io-map` scenario must honor `deviceName`, `plcName`, `includeIoDetails`, and `includeTagMatches` using production-equivalent comparison rules.
- Do not invent or reconstruct the deleted live-TIA JSON artifact. The report must say exactly what is and is not repository-auditable.
- Run .NET build/test commands serially with `--no-restore -m:1 --disable-build-servers` and `-p:UseTiaPortalReferenceStubs=true` for the solution build.

---

### Task 1: Preserve base hardware data across optional Siemens-enumeration failures

**Files:**
- Modify: `TiaMcpServer.Tests/Network/HardwareDeviceSelectionTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareIoMapReader.cs`

**Interfaces:**
- Consumes: `HardwareTagIndexResolver.Resolve(Project, string?, List<string>)`, `DeviceItem.Addresses`, and `DeviceItem.Channels`.
- Produces: the existing `HardwareConfigInfo` and `DeviceItemIoDetailsInfo` shapes, with degradation messages instead of a failed read or dropped `DeviceItemInfo`.

- [ ] **Step 1: Add source-contract regressions for both degradation boundaries**

Add tests to `HardwareDeviceSelectionTests` that normalize line endings through `ReadRepositorySource` and require:

```csharp
[Fact]
public void HardwareConfigReader_TagIndexFailureIsNonFatalOptionalEnrichment()
{
    var source = ReadRepositorySource(
        "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs");

    Assert.Contains("private static IoTagIndex? ResolveTagIndex(", source, StringComparison.Ordinal);
    Assert.Contains("catch (EngineeringException exception)", source, StringComparison.Ordinal);
    Assert.Contains("no tag matches are reported", source, StringComparison.Ordinal);
    Assert.Contains("tagIndex = ResolveTagIndex(project, plcName, result.Messages);", source, StringComparison.Ordinal);
}

[Fact]
public void HardwareIoMapReader_EnumerationFailuresStayInsideOptionalIoDetails()
{
    var source = ReadRepositorySource(
        "TiaMcpServer.OpennessWorker", "Openness", "HardwareIoMapReader.cs");

    Assert.Contains("try\n        {\n            foreach (Address address in item.Addresses)", source, StringComparison.Ordinal);
    Assert.Contains("Could not enumerate addresses while reading device item", source, StringComparison.Ordinal);
    Assert.Contains("try\n        {\n            foreach (Channel channel in item.Channels)", source, StringComparison.Ordinal);
    Assert.Contains("Could not enumerate channels while reading device item", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~HardwareDeviceSelectionTests"
```

Expected: the two new tests fail because `ResolveTagIndex` and the outer enumeration guards do not exist.

- [ ] **Step 3: Add the minimal worker degradation boundaries**

In `HardwareConfigReader`, route optional tag-index construction through a helper with this behavior:

```csharp
private static IoTagIndex? ResolveTagIndex(Project project, string? plcName, List<string> messages)
{
    try
    {
        return HardwareTagIndexResolver.Resolve(project, plcName, messages);
    }
    catch (EngineeringException exception)
    {
        messages.Add($"Could not build the PLC tag index: {exception.Message}; no tag matches are reported.");
        return null;
    }
}
```

In `HardwareIoMapReader`, put each complete `foreach` over `item.Addresses` and `item.Channels` inside an outer `try/catch (EngineeringException)`. Keep the existing per-element guards inside. On an outer failure append the exact messages required by Step 1 and return the partial/empty details so `ReadDeviceItem` continues.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the Step 2 command. Expected: all `HardwareDeviceSelectionTests` pass.

- [ ] **Step 5: Commit the task**

```powershell
git add TiaMcpServer.Tests/Network/HardwareDeviceSelectionTests.cs TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs TiaMcpServer.OpennessWorker/Openness/HardwareIoMapReader.cs
git commit -m "fix(network): degrade optional I/O enrichment failures"
```

### Task 2: Make logical-address intervals overflow-safe and formatter output round-trip

**Files:**
- Modify: `TiaMcpServer.Tests/Network/IoLogicalAddressFormatterTests.cs`
- Modify: `TiaMcpServer.Contracts/IoLogicalAddressFormatter.cs`

**Interfaces:**
- Consumes: `IoLogicalAddressFormatter.TryParse` and the existing `IoAbsoluteBitInterval` record struct.
- Produces: a non-overflowing `long EndBitExclusive` and `FormatLogicalAddress` output that parses back to the exact requested interval.

- [ ] **Step 1: Add boundary regressions**

Add:

```csharp
[Theory]
[InlineData("Input", 2147483640, 8u)]
[InlineData("Input", 2147483632, 16u)]
[InlineData("Input", 2147483616, 32u)]
public void FormatLogicalAddress_RejectsAlignedIntervalsTheParserCannotRepresent(
    string ioType,
    int startBit,
    uint widthBits)
{
    Assert.Null(IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
}

[Fact]
public void EndBitExclusive_UsesNonOverflowingArithmeticAtTheMaximumParsedBit()
{
    Assert.True(IoLogicalAddressFormatter.TryParse("%I268435455.7", out var address));
    Assert.Equal(2147483648L, address!.Value.Interval.EndBitExclusive);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~IoLogicalAddressFormatterTests"
```

Expected: the formatter returns strings for at least one non-round-trippable interval and `EndBitExclusive` wraps negative.

- [ ] **Step 3: Implement exact round-trip validation and long arithmetic**

Change the derived endpoint to:

```csharp
public long EndBitExclusive => (long)StartBit + BitCount;
```

Have `FormatLogicalAddress` build the aligned candidate exactly as today, then return it only when `TryParse(candidate, out parsed)` succeeds and `parsed` equals `new IoAbsoluteIoAddress(area, new IoAbsoluteBitInterval(startBit.Value, widthBits.Value))`; otherwise return null.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all formatter tests pass.

- [ ] **Step 5: Commit the task**

```powershell
git add TiaMcpServer.Tests/Network/IoLogicalAddressFormatterTests.cs TiaMcpServer.Contracts/IoLogicalAddressFormatter.cs
git commit -m "fix(network): enforce logical address round trips"
```

### Task 3: Make the I/O-map FakeWorker exercise the request fields it claims to verify

**Files:**
- Modify: `TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`

**Interfaces:**
- Consumes: serialized `WorkerRequest` fields `deviceName`, `plcName`, `includeIoDetails`, and `includeTagMatches`.
- Produces: the existing `HardwareConfigInfo` fixture, filtered like production and with tag matches populated only when explicitly enabled for the selected PLC.

- [ ] **Step 1: Add integration regressions through the real host/FakeWorker transport**

Add tests that call `NetworkReadTools.NetworkRead` and assert:

```csharp
[Fact]
public async Task NetworkRead_IncludeIoDetailsWithoutTagMatchesReturnsEmptyTagMatchCollections()
```

Every returned channel has `tagMatches: []` when `includeIoDetails: true` and `includeTagMatches: false`.

```csharp
[Fact]
public async Task NetworkRead_DeviceNameFilterExcludesTheNonMatchingFixtureDevice()
```

Sending `deviceName: "OTHER_PLC"` returns a succeeded operation with `devices: []`.

```csharp
[Fact]
public async Task NetworkRead_PlcNameMismatchSuppressesTagMatches()
```

Sending `includeIoDetails: true`, `includeTagMatches: true`, and `plcName: "plc_1"` returns channels with empty tag matches and a `messages` entry explaining that no exact PLC match was found.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~NetworkIoMapFakeWorkerTests"
```

Expected: the new tests fail because the fixture always returns `PLC_1` and always fills tag matches when I/O details are present.

- [ ] **Step 3: Parse and honor all four request fields in the scenario**

Use the existing `ReadField` and `ReadBoolField` helpers. Apply `deviceName` with `StringComparison.OrdinalIgnoreCase`, and apply `plcName` only when tag matches are requested, with `StringComparison.Ordinal` like `HardwareTagIndexResolver`. Pass a separate `includeTagMatches` flag into `IoMapDeviceItem`; always serialize `tagMatches` as an empty list when not enabled. Preserve the fixture's current data and deterministic ordering.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all FakeWorker I/O-map tests pass.

- [ ] **Step 5: Commit the task**

```powershell
git add TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs TiaMcpServer.FakeWorker/Program.cs
git commit -m "test(network): make I/O map fixture honor selectors"
```

### Task 4: Make the historical live report match the committed evidence

**Files:**
- Modify: `docs/superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md`

**Interfaces:**
- Consumes: the historical human-readable observations already in the report and the repository fact that the referenced JSON artifact is absent.
- Produces: an audit-honest historical report that distinguishes a reported live run from repository-verifiable evidence.

- [ ] **Step 1: Correct the evidence claim**

Keep the historical observations, but replace the claim that raw JSON is committed with an explicit statement that the referenced artifact is absent from this branch and the table is a human-readable summary only. Change the final verdict from an auditable `PASS` to: the historical run was reported as passing, but live acceptance is not repository-auditable and requires a fresh authorized rerun if merge acceptance depends on it.

- [ ] **Step 2: Verify documentation links**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-doc-links.ps1
```

Expected: all documentation links pass; the report no longer points at a missing raw evidence file.

- [ ] **Step 3: Commit the task**

```powershell
git add docs/superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md
git commit -m "docs(network): clarify I/O map live evidence limits"
```

### Task 5: Integrated verification and PR publication

**Files:**
- Verify all files changed by Tasks 1-4 plus this plan and `docs/superpowers/README.md`.

**Interfaces:**
- Consumes: the four task commits.
- Produces: fresh build/test/doc evidence and a pushed update on the existing PR branch.

- [ ] **Step 1: Run the serial stub solution build**

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
```

- [ ] **Step 2: Run the complete non-live test suite**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --no-build -m:1 --disable-build-servers --nologo --verbosity minimal
```

- [ ] **Step 3: Run repository hygiene checks**

```powershell
pwsh -NoProfile -File scripts/verify-doc-links.ps1
git diff --check HEAD~4..HEAD
git status --short
```

- [ ] **Step 4: Obtain an independent whole-change review and fix any validated blocking findings**

Review the exact range added by this plan for correctness, contract drift, test validity, and documentation honesty. Any fix must receive a focused regression and rerun before publication.

- [ ] **Step 5: Push the current branch to its configured upstream**

```powershell
git push
```

Confirm PR #27 resolves to the pushed head SHA.
