# Issue #30 PLC Block Header Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName` as optional
`browse_project_tree` v3 block-node details through typed TIA Portal V21 properties.

**Architecture:** Read the four properties only in the net48 worker's shared
`ProjectTreeSnapshotWalker.BuildBlockNode` seam, add non-blank values to the existing string details
dictionary, and let the existing typed worker decoder, flattener, snapshot cache, and canonical page
projector carry them unchanged. Add producer-boundary and FakeWorker protocol coverage without adding
an input flag, DTO, cursor field, contract-version change, or new failure category.

**Tech Stack:** C# 14, .NET 10 host/tests, .NET Framework 4.8 Openness worker, Siemens TIA Portal V21
Openness Step7 API, xUnit, MCP SDK structured results, PowerShell verification scripts.

**Spec:** [`docs/superpowers/specs/2026-09-13-issue-30-plc-block-header-metadata-design.md`](../specs/2026-09-13-issue-30-plc-block-header-metadata-design.md)

## Global constraints

- Preserve the two-process boundary: Siemens API access stays in `TiaMcpServer.OpennessWorker`.
- Use only `PlcBlock.HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName`; never use
  `GetAttribute` for this feature.
- Emit only non-blank values. Preserve non-blank strings exactly, and render the typed version with
  `HeaderVersion?.ToString()`.
- Keep node `name` independent from `details.HeaderName`; neither is a fallback for the other.
- Add details in this deterministic order after `Number` and `ProgrammingLanguage`:
  `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, `HeaderName`.
- Keep the fields default-on. Do not add `includeHeaders`, a header DTO, cursor/query state, or a v3
  `contractVersion` bump.
- Preserve all current snapshot, page, item, timeout, warning, and failure behavior. Do not truncate
  values or silently suppress property-access failures.
- Do not add `PlcType`/UDT header reads, provenance attestation, deeper selector pushdown, or write
  behavior.
- Stop after offline/static verification. Do not start, attach to, read from, save, or mutate a live
  TIA Portal instance without separate explicit authorization.
- Do not implement directly on `main`. Before Task 1, agree the checkout strategy and preserve the
  approved uncommitted spec, plan, and documentation-index edits already present in the current
  checkout.
- Do not commit, push, open a pull request, or otherwise write remotely unless the user explicitly
  authorizes that action. Suggested commit steps below are authority gates, not automatic permission.

## File map

- Modify `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSnapshotWalker.cs`: read and emit the four
  typed header values in shared block-node construction.
- Modify `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`: model the exact V21
  `PlcBlock` header-property CLR shapes for offline producer tests.
- Modify `TiaMcpServer.Tests/Project/ProjectTreeWorkerProducerContractTests.cs`: prove populated and
  blank behavior across worker serialization and strict host decoding.
- Modify `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs`: replace the deliberate
  no-header guard with a typed-access/no-dynamic-attribute guard.
- Modify `TiaMcpServer.FakeWorker/Program.cs`: add representative header details to the existing v3
  project-tree fixture.
- Modify `TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs`: prove cached continuation
  pages preserve the details without another worker observation.
- Modify `TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs`: prove canonical MCP text
  and structured content expose all four values.
- Modify `README.md`: advertise block header metadata in the compact tool description.
- Modify `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`: document keys, omission,
  `HeaderName` identity separation, and provenance semantics.
- Modify `docs/ARCHITECTURE.md`: record the typed worker seam and prohibition on dynamic attributes.
- Modify `docs/IMPROVEMENT_LOG.md`: record offline completion only after every mandatory gate passes.

---

### Task 1: Produce typed block-header details in the worker

**Files:**

- Modify: `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs:118-122`
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeWorkerProducerContractTests.cs:17-143`
- Modify: `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs:76-88`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSnapshotWalker.cs:104-129`

**Interfaces:**

- Consumes: `PlcBlock.HeaderAuthor : string`, `HeaderFamily : string`, `HeaderName : string`, and
  `HeaderVersion : System.Version` from Siemens.Engineering.Step7.
- Produces: optional `Dictionary<string,string>` entries named `HeaderAuthor`, `HeaderVersion`,
  `HeaderFamily`, and `HeaderName` on every block node produced by `BuildBlockNode`.

- [ ] **Step 1: Extend the Siemens test double with the exact typed property surface**

  Add these properties to the existing `PlcBlock` double after `ProgrammingLanguage`:

  ```csharp
  public string? HeaderAuthor { get; set; }
  public string? HeaderFamily { get; set; }
  public string? HeaderName { get; set; }
  public System.Version? HeaderVersion { get; set; }
  ```

  This is test infrastructure only. Do not add these properties to the shared production contracts;
  the public boundary remains the existing details dictionary.

- [ ] **Step 2: Generalize the producer fixture for explicit user and system blocks**

  Replace `ProjectWithLeaves` with this signature and block setup while preserving the existing
  device, software, tag-table, and type setup:

  ```csharp
  private static Siemens.Engineering.Project ProjectWithLeaves(
      bool includeLeaves = true,
      PlcBlock? userBlock = null,
      PlcBlock? systemBlock = null)
  {
      var project = new Siemens.Engineering.Project();
      var device = new Device { Name = "PLC_1" };
      var plc = new PlcSoftware { Name = "PLC" };
      plc.BlockGroup.Name = "Program blocks";
      plc.TagTableGroup.Name = "PLC tags";
      plc.TypeGroup.Name = "PLC data types";
      if (includeLeaves)
      {
          plc.BlockGroup.Blocks.Items.Add(userBlock ?? new FB { Name = "Motor", Number = 1 });
          plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Signals" });
          plc.TypeGroup.Types.Items.Add(new PlcType { Name = "State" });
      }

      if (systemBlock is not null)
      {
          var systemGroup = new PlcSystemBlockGroup { Name = "System blocks" };
          systemGroup.Blocks.Items.Add(systemBlock);
          plc.BlockGroup.SystemBlockGroups.Items.Add(systemGroup);
      }

      device.DeviceItems.Items.Add(new DeviceItem
      {
          Container = new SoftwareContainer { Software = plc }
      });
      project.Devices.Items.Add(device);
      return project;
  }
  ```

- [ ] **Step 3: Write producer-boundary tests for populated, distinct, and blank values**

  Add these tests to `ProjectTreeWorkerProducerContractTests`:

  ```csharp
  [Fact]
  public void BlockHeaders_PopulatedValuesCrossStrictBoundaryWithoutChangingIdentity()
  {
      var project = ProjectWithLeaves(
          userBlock: new FB
          {
              Name = "Motor",
              Number = 1,
              HeaderAuthor = "Example Controls Ltd.",
              HeaderVersion = new Version(2, 3),
              HeaderFamily = "Motion",
              HeaderName = "Reusable motor control"
          },
          systemBlock: new FC
          {
              Name = "SystemCycle",
              Number = 2,
              HeaderAuthor = "Siemens",
              HeaderVersion = new Version(1, 0),
              HeaderFamily = "System",
              HeaderName = "System cycle header"
          });
      var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(project, startSelector: null, depth: null);

      var observation = Decode(snapshot, selector: null, depth: null);
      var userBlock = Descendants(observation.Roots).Single(node => node.Name == "Motor");
      var systemBlock = Descendants(observation.Roots).Single(node => node.Name == "SystemCycle");

      Assert.Equal("Motor", userBlock.Name);
      Assert.Equal(
          new[]
          {
              "Number", "ProgrammingLanguage", "HeaderAuthor", "HeaderVersion",
              "HeaderFamily", "HeaderName"
          },
          userBlock.Details!.Keys);
      Assert.Equal("Example Controls Ltd.", userBlock.Details["HeaderAuthor"]);
      Assert.Equal("2.3", userBlock.Details["HeaderVersion"]);
      Assert.Equal("Motion", userBlock.Details["HeaderFamily"]);
      Assert.Equal("Reusable motor control", userBlock.Details["HeaderName"]);
      Assert.False(userBlock.Details.ContainsKey("IsSystemBlock"));

      Assert.Equal("SystemCycle", systemBlock.Name);
      Assert.Equal("Siemens", systemBlock.Details!["HeaderAuthor"]);
      Assert.Equal("1.0", systemBlock.Details["HeaderVersion"]);
      Assert.Equal("System", systemBlock.Details["HeaderFamily"]);
      Assert.Equal("System cycle header", systemBlock.Details["HeaderName"]);
      Assert.Equal("true", systemBlock.Details["IsSystemBlock"]);
  }

  [Fact]
  public void BlockHeaders_NullEmptyAndWhitespaceValuesAreOmitted()
  {
      var project = ProjectWithLeaves(userBlock: new FB
      {
          Name = "Motor",
          Number = 1,
          HeaderAuthor = null,
          HeaderVersion = null,
          HeaderFamily = string.Empty,
          HeaderName = " \t "
      });
      var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(project, startSelector: null, depth: null);

      var observation = Decode(snapshot, selector: null, depth: null);
      var block = Descendants(observation.Roots).Single(node => node.Name == "Motor");

      Assert.Equal("1", block.Details!["Number"]);
      Assert.Equal("SCL", block.Details["ProgrammingLanguage"]);
      Assert.DoesNotContain("HeaderAuthor", block.Details.Keys);
      Assert.DoesNotContain("HeaderVersion", block.Details.Keys);
      Assert.DoesNotContain("HeaderFamily", block.Details.Keys);
      Assert.DoesNotContain("HeaderName", block.Details.Keys);
  }
  ```

  The first test deliberately makes `Name` and `HeaderName` different. It passes the real worker
  serializer output through `ProjectTreeWorkerPayloadContract.Decode`, so it covers the typed
  worker/host boundary rather than only inspecting an in-memory dictionary.

- [ ] **Step 4: Replace the source-level no-header guard with the approved typed-access guard**

  In `ProjectTreeSnapshotWalker_MarksSystemMembershipWithoutChangingFunctionalBlockTypes`, remove:

  ```csharp
  Assert.DoesNotContain("HeaderAuthor", source, StringComparison.Ordinal);
  ```

  Add:

  ```csharp
  Assert.Contains("block.HeaderAuthor", source, StringComparison.Ordinal);
  Assert.Contains("block.HeaderVersion", source, StringComparison.Ordinal);
  Assert.Contains("block.HeaderFamily", source, StringComparison.Ordinal);
  Assert.Contains("block.HeaderName", source, StringComparison.Ordinal);
  Assert.DoesNotContain("GetAttribute(", source, StringComparison.Ordinal);
  ```

- [ ] **Step 5: Run the focused tests and verify the RED state**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectTreeWorkerProducerContractTests|FullyQualifiedName~ProjectTraversalSourceContractTests"
  ```

  Expected: FAIL because the populated header keys and four typed property reads are absent from
  `ProjectTreeSnapshotWalker`. The blank-value test may already pass; the focused run as a whole must
  be red for the intended production omission.

- [ ] **Step 6: Implement the minimal typed worker mapping**

  In `BuildBlockNode`, immediately after the initial `Number`/`ProgrammingLanguage` dictionary
  initializer, add:

  ```csharp
  AddNonBlankDetail(details, "HeaderAuthor", block.HeaderAuthor);
  AddNonBlankDetail(details, "HeaderVersion", block.HeaderVersion?.ToString());
  AddNonBlankDetail(details, "HeaderFamily", block.HeaderFamily);
  AddNonBlankDetail(details, "HeaderName", block.HeaderName);
  ```

  Add this private helper next to `BuildBlockNode`:

  ```csharp
  private static void AddNonBlankDetail(
      IDictionary<string, string> details,
      string key,
      string? value)
  {
      if (!string.IsNullOrWhiteSpace(value))
      {
          details[key] = value;
      }
  }
  ```

  Keep `SoftwareUnit` and `IsSystemBlock` insertion after the four calls. Do not catch property getter
  exceptions or normalize stored strings.

- [ ] **Step 7: Run the focused tests and verify GREEN**

  Run the Step 5 command again.

  Expected: PASS. Confirm the populated user block has the exact six-key order and the system block
  retains its functional `FC` node type plus `IsSystemBlock: "true"`.

- [ ] **Step 8: Run all project-tree producer, traversal, filtering, and flattening tests**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeWorkerProducerContractTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTraversalSourceContractTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeWorkerPayloadContractTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeFilterTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeFlattenerTests"
  ```

  Expected: PASS with no changes to selector, node-type, children, or detail-copy behavior.

- [ ] **Step 9: Commit the worker producer slice only if commits were explicitly authorized**

  Suggested commands:

  ```powershell
  git add TiaMcpServer.OpennessWorker/Openness/ProjectTreeSnapshotWalker.cs TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs TiaMcpServer.Tests/Project/ProjectTreeWorkerProducerContractTests.cs TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs
  git commit -m "feat: expose PLC block header metadata"
  ```

  If commits were not authorized, do not stage or commit; continue with the verified working-tree
  changes.

### Task 2: Prove IPC, cached continuation, and canonical MCP propagation

**Files:**

- Modify: `TiaMcpServer.FakeWorker/Program.cs:1600-1640`
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs:295-313`
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs:32-60`

**Interfaces:**

- Consumes: the existing `ProjectTreeNode.Details` string dictionary produced by Task 1.
- Produces: end-to-end evidence that header details survive newline-delimited worker IPC, strict
  decoding, flattening, cached continuation, and canonical MCP rendering.

- [ ] **Step 1: Add failing continuation assertions against the existing FakeWorker fixture**

  Extend `ContinuationUsesCachedSnapshotAfterPersistentWorkerIsDisposed` after its sequence assertion:

  ```csharp
  var block = Assert.Single(
      second.Response.Result!.Nodes,
      node => node.NodeType == ProjectTreeNodeTypes.Fb);
  Assert.Equal("Main", block.Name);
  Assert.Equal("Fake Vendor", block.Details["HeaderAuthor"]);
  Assert.Equal("2.3", block.Details["HeaderVersion"]);
  Assert.Equal("Motion", block.Details["HeaderFamily"]);
  Assert.Equal("Reusable main cycle", block.Details["HeaderName"]);
  ```

  This existing scenario rejects a second worker observation and disposes the worker before the
  continuation, so the assertion proves metadata comes from the cached snapshot.

- [ ] **Step 2: Add failing canonical MCP assertions**

  In `BrowseProjectTree_AdvertisesOutputSchemaAndUsesOneCanonicalDocument`, change `pageSize` from
  `2` to `4`, then add:

  ```csharp
  var nodes = structured.GetProperty("result").GetProperty("nodes");
  var block = Assert.Single(
      nodes.EnumerateArray(),
      node => node.GetProperty("nodeType").GetString() == ProjectTreeNodeTypes.Fb);
  Assert.Equal("Main", block.GetProperty("name").GetString());
  var details = block.GetProperty("details");
  Assert.Equal("Fake Vendor", details.GetProperty("HeaderAuthor").GetString());
  Assert.Equal("2.3", details.GetProperty("HeaderVersion").GetString());
  Assert.Equal("Motion", details.GetProperty("HeaderFamily").GetString());
  Assert.Equal("Reusable main cycle", details.GetProperty("HeaderName").GetString());
  ```

  Keep the existing `AssertOneCanonicalDocument` call; it is the proof that text and structured
  content are the same serialized document.

- [ ] **Step 3: Run both tests and verify the RED state**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectTreeBrowseCoordinatorTests.ContinuationUsesCachedSnapshotAfterPersistentWorkerIsDisposed|FullyQualifiedName~ProjectTreeStructuredProtocolTests.BrowseProjectTree_AdvertisesOutputSchemaAndUsesOneCanonicalDocument"
  ```

  Expected: FAIL because the FakeWorker block has no header details.

- [ ] **Step 4: Add representative headers to the FakeWorker v3 block node**

  In `ProjectTreeV3Fixture`, replace the `Main` FB node's `Details = null` with:

  ```csharp
  Details = new Dictionary<string, string>
  {
      ["Number"] = "42",
      ["ProgrammingLanguage"] = "SCL",
      ["HeaderAuthor"] = "Fake Vendor",
      ["HeaderVersion"] = "2.3",
      ["HeaderFamily"] = "Motion",
      ["HeaderName"] = "Reusable main cycle",
  },
  ```

  Do not add a new scenario or protocol field. Both `project-tree-v3-small` and
  `project-tree-v3-one-shot` deliberately share this typed fixture.

- [ ] **Step 5: Run the two focused tests and verify GREEN**

  Run the Step 3 command again.

  Expected: PASS. The continuation must still succeed after worker disposal, and the MCP result must
  retain one canonical document.

- [ ] **Step 6: Run the complete coordinator, protocol, and page-budget test groups**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeBrowseCoordinatorTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreeStructuredProtocolTests|FullyQualifiedName~TiaMcpServer.Tests.Project.ProjectTreePageProjectorTests"
  ```

  Expected: PASS, including existing `snapshot_too_large`, `result_item_too_large`, metadata budget,
  exact-page projection, replay, and zero-worker-call continuation coverage.

- [ ] **Step 7: Commit the IPC/protocol slice only if commits were explicitly authorized**

  Suggested commands:

  ```powershell
  git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs
  git commit -m "test: verify block headers across project tree IPC"
  ```

  If commits were not authorized, do not stage or commit.

### Task 3: Update the maintained public contract documentation

**Files:**

- Modify: `README.md:58`
- Modify: `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md:11-13`
- Modify: `docs/ARCHITECTURE.md:123-127`

**Interfaces:**

- Consumes: the verified public behavior from Tasks 1 and 2.
- Produces: current user and maintainer documentation that does not rely on this historical spec as
  the runtime authority.

- [ ] **Step 1: Update the README landing-page description**

  Revise the `browse_project_tree` bullet to state that the paged v3 snapshot includes optional typed
  PLC block header author, version, family, and header-name metadata. Keep the existing absolute GitHub
  link to the project-operations reference; README is the NuGet package readme.

- [ ] **Step 2: Add the exact block-detail contract to the project-operations reference**

  After the opening `browse_project_tree` v3 paragraph, add a table with these rows:

  ```markdown
  | Block detail | Meaning |
  | --- | --- |
  | `HeaderAuthor` | Non-blank `PlcBlock.HeaderAuthor` exactly as reported by TIA Portal. |
  | `HeaderVersion` | Non-blank `PlcBlock.HeaderVersion.ToString()` value. |
  | `HeaderFamily` | Non-blank `PlcBlock.HeaderFamily` exactly as reported by TIA Portal. |
  | `HeaderName` | Non-blank `PlcBlock.HeaderName`; separate from the node's engineering-object `name`. |
  ```

  Follow the table with these explicit rules:

  ```markdown
  Blank header values are omitted rather than emitted as `null` or empty strings. Header metadata is
  descriptive project data, not verified vendor provenance. `HeaderName` never changes selector
  identity, and `IsSystemBlock` continues to describe hierarchy membership only.
  ```

- [ ] **Step 3: Record the worker architecture seam**

  Extend the architecture paragraph about shared block-node construction with:

  ```markdown
  The same seam reads `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName` through typed
  `PlcBlock` properties and omits blank values. It does not use dynamic `GetAttribute` calls. These
  fields report project metadata and do not attest to vendor provenance.
  ```

- [ ] **Step 4: Review documentation terminology against the executable contract**

  Confirm every maintained document uses the exact PascalCase keys, calls the engineering object
  `name` distinct from `HeaderName`, and does not promise verified vendor identity, UDT headers, an
  opt-in flag, or per-page worker reads.

- [ ] **Step 5: Run focused protocol tests and documentation hygiene checks**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectTreeStructuredProtocolTests|FullyQualifiedName~ProjectTraversalSourceContractTests"
  git diff --check
  ```

  Expected: tests PASS and `git diff --check` emits no output.

- [ ] **Step 6: Commit maintained documentation only if commits were explicitly authorized**

  Suggested commands:

  ```powershell
  git add README.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/ARCHITECTURE.md
  git commit -m "docs: document PLC block header metadata"
  ```

  If commits were not authorized, do not stage or commit.

### Task 4: Run the complete offline release gate and record the result

**Files:**

- Modify after all gates pass: `docs/IMPROVEMENT_LOG.md`
- Verify: all files changed by Tasks 1-3 plus the approved spec and this plan

**Interfaces:**

- Consumes: the complete implementation, integration evidence, and maintained documentation.
- Produces: fresh build, test, coverage, package, static real-assembly, diff, and review evidence; no
  live TIA session.

- [ ] **Step 1: Restore and run the serial Release reference-stub build**

  Run:

  ```powershell
  dotnet restore TiaMcpServer.sln
  dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: restore and build exit 0. Record warning counts separately; do not call inherited warnings
  new failures without comparing them to current `main`.

- [ ] **Step 2: Run the full Release suite with scoped coverage and enforce 80%**

  Run:

  ```powershell
  $coverageResults = Join-Path (Get-Location) ("artifacts\issue-30-coverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $coverageResults
  $coverageReports = @(Get-ChildItem -LiteralPath $coverageResults -Recurse -Filter coverage.cobertura.xml)
  if ($coverageReports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($coverageReports.Count)." }
  .\scripts\verify-coverage-threshold.ps1 -CoveragePath $coverageReports[0].FullName -MinimumLineRate 0.80
  ```

  Expected: all tests PASS and the threshold script exits 0 at an inclusive minimum line rate of
  `0.80`.

- [ ] **Step 3: Pack a local verification artifact and inspect package contents**

  Run:

  ```powershell
  $packageVersion = "3.0.0-issue30-local"
  $packageDirectory = Join-Path (Get-Location) ("artifacts\issue-30-package-" + [guid]::NewGuid().ToString('N'))
  dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release --no-restore -o $packageDirectory /p:Version=$packageVersion /p:PackageVersion=$packageVersion /p:InformationalVersion=$packageVersion /p:IncludeSourceRevisionInInformationalVersion=false /p:UseTiaPortalReferenceStubs=true
  $packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter *.nupkg)
  if ($packages.Count -ne 1) { throw "Expected exactly one NuGet package; found $($packages.Count)." }
  .\scripts\verify-doctor-package.ps1 -PackagePath $packages[0].FullName
  ```

  Expected: pack and verification exit 0, the package readme includes the updated tool description,
  and no Siemens DLL is present. This prerelease identifier is only for local verification and does
  not decide the eventual release version.

- [ ] **Step 4: Compile against installed TIA Portal V21 assemblies without launching TIA**

  When the documented V21 PublicAPI directory exists, run:

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  ```

  Expected: build exits 0 and confirms the four production property references match the installed
  Step7 assembly. This is a static assembly build; do not start or attach to TIA Portal. If the
  directory is absent, record that the static real-assembly build was unavailable and retain the
  passing reference-stub build as the mandatory offline evidence.

- [ ] **Step 5: Perform the scoped whole-change review**

  Inspect the complete diff and verify each item explicitly:

  - only the four approved keys were added;
  - insertion and output order is `Author`, `Version`, `Family`, `Name`;
  - no string trimming, fallback, dynamic attribute access, exception suppression, or new input exists;
  - user and system block node types and `IsSystemBlock` semantics remain unchanged;
  - continuations still use the cached snapshot after worker disposal;
  - docs distinguish observed metadata from verified provenance; and
  - no generated artifact, fixture path, Siemens DLL, or unrelated user change entered the diff.

- [ ] **Step 6: Add the completed improvement-log entry after Steps 1-5 pass**

  Append this section at the end of `docs/IMPROVEMENT_LOG.md`:

  ```markdown
  ## Issue #30 PLC block header metadata — offline implementation completed (2026-09-13)

  - `browse_project_tree` block nodes now expose non-blank `HeaderAuthor`, `HeaderVersion`,
    `HeaderFamily`, and `HeaderName` values through typed `PlcBlock` properties.
  - Producer, strict-decoder, FakeWorker IPC, cached continuation, canonical MCP, budget, Release
    build/test/coverage, and package-content gates passed. Maintained docs distinguish reported
    metadata from verified provenance.
  - Live TIA Portal V21 acceptance remains a separate read-only authorization gate.
  ```

  If any mandatory gate failed or was not run, do not add this completion entry. Report the actual
  boundary instead.

- [ ] **Step 7: Run final documentation, whitespace, and repository-state checks**

  Run:

  ```powershell
  git diff --check
  git status --short --branch
  ```

  Expected: `git diff --check` emits no output. `git status` shows only the scoped implementation,
  tests, maintained documentation, spec, plan, and documentation-index files, plus ignored local
  verification artifacts.

  Verify every relative Markdown link in changed documentation resolves from its containing file,
  and verify README cross-document links remain absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/...`
  URLs for NuGet compatibility.

- [ ] **Step 8: Commit the verification record only if commits were explicitly authorized**

  Suggested commands:

  ```powershell
  git add docs/IMPROVEMENT_LOG.md docs/README.md docs/superpowers/README.md docs/superpowers/specs/2026-09-13-issue-30-plc-block-header-metadata-design.md docs/superpowers/plans/2026-09-13-issue-30-plc-block-header-metadata.md
  git commit -m "docs: record issue 30 design and verification"
  ```

  If commits were not authorized, do not stage or commit. Stop with the verified scoped diff ready for
  user review.

## Deferred live acceptance gate — do not execute without new authorization

Tasks 1-4 stop before any live TIA Portal action. A later, separately authorized read-only acceptance
run uses an explicitly approved disposable project and must not save or mutate it. The acceptance run
must verify all four values against TIA block headers, include a case where engineering `Name` differs
from `HeaderName`, cover blank omission and representative user/system/software-unit block paths,
page through the complete snapshot without metadata loss, and record three-run initial-snapshot timing
and size deltas against current `main`.

Correct values, unchanged tree identity, successful bounded paging, and no new timeout are semantic
requirements. Timing and size evidence is reported with spread for user acceptance rather than judged
against an invented threshold.
