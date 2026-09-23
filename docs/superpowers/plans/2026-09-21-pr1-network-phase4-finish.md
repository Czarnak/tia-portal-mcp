# PR 1 Network Phase 4 Finish and Current-Revision Live Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the five verified Phase 4 subnet-lifecycle gaps, restore a guarded public MCP live
procedure from the user's saved copy, and produce current-revision TIA Portal V21 acceptance
evidence without saving, compiling, or deleting devices.

**Architecture:** Keep `network_write` on the existing canonical structured-batch and safety-token
path. Tighten host request/result validation, repair the net48 worker's delete resolution and
identity capture inside one transaction, then verify the final implementation through the restored
public stdio MCP harness. The harness remains temporary and is restored by the user; this plan never
recreates or substitutes it.

**Tech Stack:** C# 14, .NET 10 host/tests/FakeWorker, .NET Framework 4.8 Openness worker, Siemens TIA
Portal V21 Openness Base/HW API, xUnit, MCP stdio JSON-RPC, PowerShell 7.

**Spec:** [`docs/superpowers/specs/2026-09-21-network-phases-4-6-delivery-design.md`](../specs/2026-09-21-network-phases-4-6-delivery-design.md)

## Global Constraints

- This plan implements PR 1 only. Do not implement or scaffold Phase 5 or Phase 6.
- Start from the latest merged `main` on a dedicated PR 1 branch. Preserve the approved uncommitted
  spec, plan, and index changes when selecting the execution checkout.
- The user restores `scripts/live-test-network-phase4-subnets.ps1` from the saved external copy.
  The implementing agent must not recreate, substitute, or recover a different script as the live
  procedure.
- Before changing the restored harness, record its SHA-256 and Git blob ID, plus the user-supplied
  saved-copy source locator and original commit/blob when those origin facts are available. A blob
  ID proves content identity, not origin. If no versioned origin exists, label it explicitly as an
  unversioned user-provided artifact. Change the script only after a static contract test fails for
  the specific current incompatibility. If every new contract test is already green, keep it
  byte-for-byte unchanged.
- Use strict red-green-refactor for every executable behavior change. A live TIA result is never
  the RED step.
- Keep Siemens API calls in `TiaMcpServer.OpennessWorker` (`net48`). The .NET 10 host must not load
  Siemens assemblies.
- Reuse `StructuredToolResult`, `StructuredOperationBatch`, `CanonicalJson`, and
  `CanonicalWriteSafety`; do not add a parallel response, batching, token, audit, or retry path.
- Preserve unchanged ordered operations, a fresh apply-time state read, single-use tokens, the
  pinned project-binding lease, sequential non-atomic batches, audit ordering, and inspect-before-
  retry guidance.
- A subnet lifecycle success result has exactly four required members: `subnetId`, `name`,
  `networkDeviceCount`, and `networkDeviceCountUnchanged`.
- Update/delete require the exact ordinal selector `{ kind: "subnet", subnetId: "..." }`.
- Delete resolves one exact subnet inside one transaction, validates Ethernet/PROFIBUS support,
  captures a nonblank name from that same object, deletes only the subnet, and commits afterward.
- Do not enumerate dependency graphs or delete devices. Connected-subnet deletion remains allowed;
  the root `project.Devices.Count` must remain unchanged.
- PR 1 does not save or compile the project. It does not download, commission, select an online
  path, control PLC mode, or claim plant acceptance.
- Do not start or attach to TIA Portal, execute any restored harness mode, or mutate any project
  without a separate explicit authorization naming the exact disposable `.ap21` target. Stop for
  that authorization after all offline gates are green.
- Suggested commit commands below are sequencing/rollback markers, not standing authorization.
  Commit, push, and pull-request creation occur only when the user authorizes those actions. Never
  merge or publish a release from this plan.

## Review Focus

1. **Omitted discriminator:** update/delete with `target.subnetId` but no `target.kind` must fail
   validation rather than inherit an implicit subnet kind. Task 1 adds the owning theory.
2. **Omitted value-type member:** a worker success without `networkDeviceCount` must become
   `protocol_error`, not a fabricated zero. Task 2 tests all three lifecycle operations.
3. **Late selector drift:** zero or multiple worker matches after host safety resolution must surface
   as `postcondition_failed`, while unsupported subnet type remains `target_kind_unsupported`.
   Task 3 pins the worker source contract.
4. **Delete race:** the object used for type validation, name capture, and deletion must be the one
   exact subnet resolved inside the transaction; unreadable or blank name must fail before delete.
   Task 4 pins call count and ordering.
5. **Harness safety regression:** Inventory must remain the non-mutating default, Preview must never
   confirm, and Apply must require both mutation switch and exact phrase before the host starts.
   Task 5 adds static contract tests and never executes the script.

## File Map

- Modify `TiaMcpServer/Network/NetworkOperationCatalog.cs`: require the explicit subnet kind.
- Modify `TiaMcpServer/Network/NetworkOperationRequest.cs`: describe the Phase 4 kind requirement.
- Modify `TiaMcpServer/Network/NetworkWriteTools.cs`: advertise the exact update/delete selector.
- Modify `TiaMcpServer/Network/NetworkPayloadContract.cs`: require all four raw result members.
- Modify `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs`: normalize late target
  drift and repair delete's transaction-local resolution/type/name sequence.
- Modify `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleRequestContractTests.cs`: missing-kind
  RED/GREEN coverage.
- Modify `TiaMcpServer.Tests/Batch/BatchToolMetadataTests.cs`: public description coverage.
- Modify `TiaMcpServer.Tests/Network/NetworkSubnetLifecyclePayloadContractTests.cs`: omitted-count
  RED/GREEN coverage and no-result/no-echo assertions.
- Modify `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs`: late
  target category and delete-order source-contract coverage.
- User restores `scripts/live-test-network-phase4-subnets.ps1`: guarded public live procedure.
- Create `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleLiveHarnessScriptTests.cs`: static
  PowerShell safety/protocol/content-identity contract; it never runs the harness.
- Modify `docs/ARCHITECTURE.md`: record the explicit selector, required raw result shape,
  transaction-local delete identity, and late-drift failure category at the Phase 4 seam.
- Modify `docs/roadmap/network-operations.md`: distinguish repaired static status, historical
  evidence, and current-revision live status.
- Modify `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`: align the locked request,
  result, worker, and evidence contracts.
- Modify `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`: summarize the final Phase 4
  boundary.
- Modify `docs/IMPROVEMENT_LOG.md`: record PR 1 completion and its evidence boundary.
- Create `docs/superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md`
  only after the live run supplies every required fact.
- Modify `docs/superpowers/README.md` and `docs/README.md` when the acceptance report is created.

---

### Task 1: Require explicit `target.kind: "subnet"`

**Files:**

- Modify: `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleRequestContractTests.cs:343-382`
- Modify: `TiaMcpServer.Tests/Batch/BatchToolMetadataTests.cs:184-198`
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs:864-894`
- Modify: `TiaMcpServer/Network/NetworkOperationRequest.cs:104-105`
- Modify: `TiaMcpServer/Network/NetworkWriteTools.cs:33`

**Interfaces:**

- Consumes: update/delete `NetworkOperationRequest.Target`.
- Produces: validation failure unless `Target.Kind` is exactly ordinal `"subnet"`, plus matching
  model-facing descriptions.

- [ ] **Step 1: Write the missing-kind request test**

  Add beside `ValidateWrite_RejectsWrongKindTarget`:

  ```csharp
  [Theory]
  [InlineData("update_subnet")]
  [InlineData("delete_subnet")]
  public void ValidateWrite_RejectsMissingKindTarget(string operationName)
  {
      var result = Validate(TargetedSubnetOp(
          operationName,
          target: new NetworkObjectTarget { SubnetId = "subnet-eth-1" }));

      Assert.False(result.IsValid);
      Assert.Contains("'target.kind'", result.Error);
      Assert.Contains("'subnet'", result.Error);
  }
  ```

- [ ] **Step 2: Write the model-facing description test**

  Add to `BatchToolMetadataTests`:

  ```csharp
  [Fact]
  public void NetworkWriteDescription_RequiresExplicitSubnetKindForLifecycleTargets()
  {
      var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");

      Assert.Contains("target.kind set exactly to 'subnet'", description);
      Assert.Contains("target.subnetId", description);
  }
  ```

- [ ] **Step 3: Restore once, then run the focused tests and observe RED**

  Run:

  ```powershell
  dotnet restore TiaMcpServer.sln --nologo
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter "FullyQualifiedName~NetworkSubnetLifecycleRequestContractTests|FullyQualifiedName~BatchToolMetadataTests"
  ```

  Expected: `ValidateWrite_RejectsMissingKindTarget` fails because the current catalog accepts a
  null kind, and the description test fails because the exact requirement is absent. This is the
  single serial restore for a fresh checkout; later test/build commands deliberately use
  `--no-restore`.

- [ ] **Step 4: Make kind validation exact and ordinal**

  Replace the nullable-kind condition in `ValidateSubnetTargetSelector` with:

  ```csharp
  if (!string.Equals(target.Kind, NetworkObjectKinds.Subnet, StringComparison.Ordinal))
  {
      errors.Add(
          $"{prefix} 'target.kind' must be '{NetworkObjectKinds.Subnet}' for {operation.Operation} "
          + $"(received '{target.Kind}').");
  }
  ```

  Update that method's XML summary from “kind absent or exactly subnet” to “kind exactly subnet.”

- [ ] **Step 5: Align public descriptions**

  Extend `NetworkObjectTarget.Kind`'s description with:

  ```text
  For update_subnet and delete_subnet, kind is required and must be exactly 'subnet'.
  ```

  In `NetworkWriteTools.NetworkWrite`, replace the update/delete selector sentence with:

  ```text
  update_subnet and delete_subnet name an existing subnet exactly by target.kind set exactly to 'subnet' and target.subnetId; update_subnet requests at least one change in subnetChanges.
  ```

- [ ] **Step 6: Rerun the focused tests and observe GREEN**

  Run the command from Step 3.

  Expected: both test classes pass; existing wrong-kind and blank-ID cases remain green.

- [ ] **Step 7: Review and checkpoint**

  Confirm no conditional schema was invented: the runtime catalog owns the per-operation rule
  because `NetworkObjectTarget` is shared with operations whose kind semantics differ.

  When commit authorization exists:

  ```powershell
  git add TiaMcpServer/Network/NetworkOperationCatalog.cs `
    TiaMcpServer/Network/NetworkOperationRequest.cs `
    TiaMcpServer/Network/NetworkWriteTools.cs `
    TiaMcpServer.Tests/Network/NetworkSubnetLifecycleRequestContractTests.cs `
    TiaMcpServer.Tests/Batch/BatchToolMetadataTests.cs
  git commit -m "fix(network): require explicit subnet target kind"
  ```

### Task 2: Reject an omitted `networkDeviceCount`

**Files:**

- Modify: `TiaMcpServer.Tests/Network/NetworkSubnetLifecyclePayloadContractTests.cs:57-193`
- Modify: `TiaMcpServer/Network/NetworkPayloadContract.cs:115-130`
- Modify: `TiaMcpServer/Network/NetworkPayloadContract.cs:401-422`
- Reuse: `TiaMcpServer/Network/NetworkPayloadContract.cs:1100-1112`

**Interfaces:**

- Consumes: raw worker JSON for `create_subnet`, `update_subnet`, and `delete_subnet`.
- Produces: `protocol_error`, `result: null`, and no raw payload echo when any of the four required
  members is absent.

- [ ] **Step 1: Write the omitted-count theory**

  Add to `NetworkSubnetLifecyclePayloadContractTests`:

  ```csharp
  [Theory]
  [InlineData("create_subnet")]
  [InlineData("update_subnet")]
  [InlineData("delete_subnet")]
  public void Project_RejectsPayload_WhenNetworkDeviceCountMissing(string operation)
  {
      var payload =
          """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCountUnchanged":true}""";
      var item = Project(operation, payload);

      Assert.Equal(OperationBatchStatus.Failed, item.Status);
      Assert.Null(item.Result);
      Assert.NotNull(item.Failure);
      Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
      Assert.DoesNotContain("subnet-1", item.Failure.Message);
  }
  ```

- [ ] **Step 2: Run the focused test and observe RED**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter FullyQualifiedName~NetworkSubnetLifecyclePayloadContractTests
  ```

  Expected: the three new cases fail because the missing integer currently normalizes to `0` and
  is accepted as a successful result.

- [ ] **Step 3: Add a raw-member decoder before CLR normalization**

  Route the three switch arms through one dedicated method:

  ```csharp
  "create_subnet" => DecodeSubnetLifecycleResult("create_subnet", payload),
  "update_subnet" => DecodeSubnetLifecycleResult("update_subnet", payload),
  "delete_subnet" => DecodeSubnetLifecycleResult("delete_subnet", payload),
  ```

  Add:

  ```csharp
  private static JsonElement DecodeSubnetLifecycleResult(string operation, string payload)
  {
      using var document = JsonDocument.Parse(payload);
      if (document.RootElement.ValueKind == JsonValueKind.Object)
      {
          RequireJsonMembers(
              document.RootElement,
              operation,
              "subnetId",
              "name",
              "networkDeviceCount",
              "networkDeviceCountUnchanged");
      }

      return Decode<SubnetLifecycleResultInfo>(payload, ValidateSubnetLifecycleResult);
  }
  ```

  Keep `ValidateSubnetLifecycleResult`'s semantic checks for nonblank identity, nonnegative count,
  and `networkDeviceCountUnchanged == true`. The raw check supplements rather than replaces typed
  validation.

- [ ] **Step 4: Rerun the payload tests and observe GREEN**

  Run the command from Step 2.

  Expected: all lifecycle payload cases pass, including missing/blank/negative/false/extra/root-
  array cases, and the rejected raw payload is not returned.

- [ ] **Step 5: Check the broader typed contract**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter "FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~StructuredOperationBatchPayloadBudgetTests"
  ```

  Expected: all tests pass; valid four-member results still survive normal payload budgets.

- [ ] **Step 6: Review and checkpoint**

  When commit authorization exists:

  ```powershell
  git add TiaMcpServer/Network/NetworkPayloadContract.cs `
    TiaMcpServer.Tests/Network/NetworkSubnetLifecyclePayloadContractTests.cs
  git commit -m "fix(network): require complete subnet lifecycle payloads"
  ```

### Task 3: Normalize late worker target drift to `postcondition_failed`

**Files:**

- Modify: `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs:64-80`
- Modify: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs:73-96`
- Modify: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs:136-171`
- Modify: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs:333-352`

**Interfaces:**

- Consumes: a Phase 4 target already resolved by the host safety read.
- Produces: worker `postcondition_failed` when that exact ID has zero or multiple matches at the
  mutation boundary; preserves `target_kind_unsupported` for an existing unsupported type.

- [ ] **Step 1: Change the source-contract expectation first**

  Replace `Service_UsesOrdinalExactOneSubnetIdLookupWithNoFallback` with:

  ```csharp
  [Fact]
  public void Service_LateZeroOrMultipleSubnetMatchesFailAsPostconditionDriftWithoutFallback()
  {
      var source = ServiceSource;

      Assert.Contains("StringComparison.Ordinal", source, StringComparison.Ordinal);
      Assert.DoesNotContain("WorkerFailureCategories.TargetNotFound", source, StringComparison.Ordinal);
      Assert.DoesNotContain("WorkerFailureCategories.TargetAmbiguous", source, StringComparison.Ordinal);
      Assert.Matches(new Regex(@"throw\s+PostconditionFailed\(\s*operationName"), source);
      Assert.Contains("ResolveExactSubnetOrThrow(project, subnetId, \"update_subnet\")", source, StringComparison.Ordinal);
      Assert.Contains("ResolveExactSubnetOrThrow(project, subnetId, \"delete_subnet\")", source, StringComparison.Ordinal);
      Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
      Assert.DoesNotContain("First()", source, StringComparison.Ordinal);
      Assert.Matches(new Regex(@"\.Count == 0"), source);
      Assert.Matches(new Regex(@"\.Count > 1"), source);
  }
  ```

- [ ] **Step 2: Run the worker source-contract tests and observe RED**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter FullyQualifiedName~NetworkSubnetLifecycleWorkerServiceContractTests
  ```

  Expected: the new expectation fails because the helper still emits `target_not_found` and
  `target_ambiguous` and has no operation name.

- [ ] **Step 3: Pass the operation name into exact resolution**

  Change every update call to:

  ```csharp
  ResolveExactSubnetOrThrow(project, subnetId, "update_subnet")
  ```

  Change every delete call to:

  ```csharp
  ResolveExactSubnetOrThrow(project, subnetId, "delete_subnet")
  ```

  Replace the helper with:

  ```csharp
  private static Subnet ResolveExactSubnetOrThrow(
      Project project,
      string subnetId,
      string operationName)
  {
      var matches = FindMatches(project, subnetId);

      if (matches.Count == 0)
      {
          throw PostconditionFailed(
              operationName,
              $"The previously resolved subnet '{subnetId}' no longer exists. Inspect the project before retrying.");
      }

      if (matches.Count > 1)
      {
          throw PostconditionFailed(
              operationName,
              $"The previously unique SubnetId '{subnetId}' now matches multiple subnets. Inspect the project before retrying.");
      }

      return matches[0];
  }
  ```

- [ ] **Step 4: Rerun the focused tests and observe GREEN**

  Run the command from Step 2.

  Expected: all source-contract tests pass; no first-match fallback or retry was introduced.

- [ ] **Step 5: Review and checkpoint**

  Verify this is a worker-owned classification, not a host-side rewrite of arbitrary worker
  failures. `NetworkPayloadContract` must continue preserving unrelated worker failure categories.

  When commit authorization exists:

  ```powershell
  git add TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs `
    TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs
  git commit -m "fix(network): classify late subnet drift as postcondition failure"
  ```

### Task 4: Make delete resolution, type validation, and name capture transaction-local

**Files:**

- Modify: `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs:82-263`
- Modify: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs:136-205`
- Modify: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs:304-331`

**Interfaces:**

- Consumes: exact `subnetId` and the Task 3 operation-aware resolver.
- Produces: one transaction-local `Subnet` instance used for supported-type validation, nonblank
  name capture, and `Delete()`, followed by post-commit absence/device-count verification.

- [ ] **Step 1: Add the delete ordering contract**

  Add to `NetworkSubnetLifecycleWorkerServiceContractTests`:

  ```csharp
  [Fact]
  public void Service_DeleteResolvesValidatesAndCapturesNameInsideTransactionBeforeDelete()
  {
      var deleteBody = ExtractPublicMethodBody(ServiceSource, "Delete");

      Assert.DoesNotContain("var existing =", deleteBody, StringComparison.Ordinal);
      Assert.Equal(1, CountOccurrences(deleteBody, "ResolveExactSubnetOrThrow("));

      var transactionIndex = deleteBody.IndexOf(
          "exclusiveAccess.Transaction(project, \"delete_subnet\")",
          StringComparison.Ordinal);
      var resolveIndex = deleteBody.IndexOf(
          "ResolveExactSubnetOrThrow(project, subnetId, \"delete_subnet\")",
          StringComparison.Ordinal);
      var typeIndex = deleteBody.IndexOf(
          "ResolveCurrentTypeIdentifierOrThrow(subnet, subnetId)",
          StringComparison.Ordinal);
      var nameIndex = deleteBody.IndexOf(
          "ReadRequiredSubnetNameOrThrow(subnet, subnetId)",
          StringComparison.Ordinal);
      var deleteIndex = deleteBody.IndexOf("subnet.Delete();", StringComparison.Ordinal);
      var commitIndex = deleteBody.IndexOf("transaction.CommitOnDispose();", StringComparison.Ordinal);

      Assert.True(transactionIndex >= 0);
      Assert.True(transactionIndex < resolveIndex);
      Assert.True(resolveIndex < typeIndex);
      Assert.True(typeIndex < nameIndex);
      Assert.True(nameIndex < deleteIndex);
      Assert.True(deleteIndex < commitIndex);
  }
  ```

- [ ] **Step 2: Tighten the unreadable/blank-name contract**

  Replace `Service_DeleteNeverFallsBackToAnEmptyNameWhenTheSubnetsOwnNameIsUnreadable` with:

  ```csharp
  [Fact]
  public void Service_DeleteRejectsUnreadableOrBlankNameBeforeDelete()
  {
      var source = ServiceSource;
      var deleteBody = ExtractPublicMethodBody(source, "Delete");
      var helperIndex = source.IndexOf(
          "private static string ReadRequiredSubnetNameOrThrow(",
          StringComparison.Ordinal);

      Assert.True(helperIndex >= 0);
      Assert.Contains("catch (EngineeringException)", source[helperIndex..], StringComparison.Ordinal);
      Assert.Contains("string.IsNullOrWhiteSpace(name)", source[helperIndex..], StringComparison.Ordinal);
      Assert.Contains("throw PostconditionFailed(", source[helperIndex..], StringComparison.Ordinal);
      Assert.DoesNotContain("capturedName = string.Empty;", deleteBody, StringComparison.Ordinal);
  }
  ```

- [ ] **Step 3: Run the worker source-contract tests and observe RED**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter FullyQualifiedName~NetworkSubnetLifecycleWorkerServiceContractTests
  ```

  Expected: the new tests fail because delete resolves/captures before exclusive access, resolves a
  second time inside the transaction, omits supported-type validation, and has no required-name
  helper.

- [ ] **Step 4: Move the complete target sequence into the transaction**

  Replace delete's pre-transaction resolution/name block and transaction body with:

  ```csharp
  var deviceCountBefore = project.Devices.Count;
  string capturedName;

  using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: delete_subnet"))
  using (var transaction = exclusiveAccess.Transaction(project, "delete_subnet"))
  {
      var subnet = ResolveExactSubnetOrThrow(project, subnetId, "delete_subnet");
      _ = ResolveCurrentTypeIdentifierOrThrow(subnet, subnetId);
      capturedName = ReadRequiredSubnetNameOrThrow(subnet, subnetId);
      subnet.Delete();
      transaction.CommitOnDispose();
  }
  ```

  Add beside `ResolveCurrentTypeIdentifierOrThrow`:

  ```csharp
  private static string ReadRequiredSubnetNameOrThrow(Subnet subnet, string subnetId)
  {
      string? name;
      try
      {
          name = subnet.Name;
      }
      catch (EngineeringException)
      {
          name = null;
      }

      if (string.IsNullOrWhiteSpace(name))
      {
          throw PostconditionFailed(
              "delete_subnet",
              $"Subnet '{subnetId}' did not expose a nonblank Name immediately before deletion. No delete was committed.");
      }

      return name;
  }
  ```

  Remove `capturedName is null` from the post-commit guard because the helper proves a nonblank
  value before `Delete()` and before `CommitOnDispose()`.

- [ ] **Step 5: Rerun the focused tests and observe GREEN**

  Run the command from Step 3.

  Expected: all worker source-contract tests pass, including one exclusive access/transaction,
  delete-before-commit, no save/compile/device delete, no dependency traversal, and no retry.

- [ ] **Step 6: Compile both worker reference modes**

  Run serially:

  ```powershell
  dotnet build TiaMcpServer.sln -c Release --no-restore -m:1 `
    /p:UseTiaPortalReferenceStubs=true --nologo
  dotnet build TiaMcpServer.sln -c Release --no-restore -m:1 `
    /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" `
    --nologo
  ```

  Expected: both builds succeed. The second proves only compile-time compatibility with installed
  V21 assemblies; it is not live TIA evidence.

- [ ] **Step 7: Review and checkpoint**

  When commit authorization exists:

  ```powershell
  git add TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs `
    TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs
  git commit -m "fix(network): make subnet delete target transaction-local"
  ```

### Task 5: Restore, characterize, and contract-test the user-owned live harness

**Files:**

- User restores: `scripts/live-test-network-phase4-subnets.ps1`
- Create: `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleLiveHarnessScriptTests.cs`

**Interfaces:**

- Consumes: the exact external script restored by the user.
- Produces: a statically guarded PowerShell 7 procedure whose default and Preview paths cannot
  mutate, whose Apply path is double-gated, and whose traffic stays on the public MCP surface.

- [ ] **Step 1: Stop until the user restores the script**

  Do not obtain the script from Git history or write a replacement. After the user confirms the
  saved copy is at the exact path, verify only that file's status:

  ```powershell
  git status --short -- scripts/live-test-network-phase4-subnets.ps1
  Get-Item -LiteralPath scripts/live-test-network-phase4-subnets.ps1 | `
    Select-Object FullName, Length, LastWriteTimeUtc
  ```

  Expected: the exact path exists as a user-restored worktree file.

- [ ] **Step 2: Record the unadapted content identity and origin before any edit**

  Ask the user to identify the saved-copy source they restored (a source path/location description
  is sufficient) and, if known, the original repository commit and blob that contained those bytes.
  Then run:

  ```powershell
  Get-FileHash -LiteralPath scripts/live-test-network-phase4-subnets.ps1 -Algorithm SHA256
  git hash-object -- scripts/live-test-network-phase4-subnets.ps1
  ```

  Preserve the SHA-256, computed blob ID, and user-supplied source information in the PR
  implementation notes and later acceptance report. Treat the computed blob as content identity,
  not proof of origin. If the user cannot provide an original commit/blob, record the origin as
  `unversioned user-provided saved copy`; do not invent commit provenance. When commit authorization
  exists, checkpoint the byte-exact restored copy before adaptation (this creates a new repository
  checkpoint but does not retroactively prove the copy's external origin):

  ```powershell
  git add scripts/live-test-network-phase4-subnets.ps1
  git commit -m "test(network): restore phase 4 live harness"
  ```

- [ ] **Step 3: Add the static harness contract test class**

  Create the class with source-only helpers:

  ```csharp
  using System.Diagnostics;
  using System.Text;
  using System.Text.RegularExpressions;
  using Xunit;

  namespace TiaMcpServer.Tests.Network;

  public sealed class NetworkSubnetLifecycleLiveHarnessScriptTests
  {
      private static string HarnessSource => File.ReadAllText(FindRepositoryFile(
          "scripts", "live-test-network-phase4-subnets.ps1"));

      [Fact]
      public void Harness_DefaultsToInventoryAndUsesStrictPowerShell7()
      {
          var source = HarnessSource;
          Assert.Contains("#Requires -Version 7", source, StringComparison.OrdinalIgnoreCase);
          Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
          Assert.Contains("$ErrorActionPreference = 'Stop'", source, StringComparison.Ordinal);
          Assert.Matches(
              new Regex(@"\[ValidateSet\('Inventory',\s*'Preview',\s*'Apply'\)\][\s\S]*?\$Mode\s*=\s*'Inventory'"),
              source);
      }

      [Fact]
      public void Harness_ApplyIsDoubleGatedBeforeTheHostStarts()
      {
          var source = HarnessSource;
          var applyGate = source.IndexOf("if ($Mode -eq 'Apply')", StringComparison.Ordinal);
          var hostDefinition = source.IndexOf("function Start-McpHost", StringComparison.Ordinal);

          Assert.True(applyGate >= 0 && hostDefinition > applyGate);
          Assert.Contains("if (-not $AllowMutation)", source, StringComparison.Ordinal);
          Assert.Contains("$Acknowledgement -cne $script:RequiredAcknowledgement", source, StringComparison.Ordinal);
          Assert.Contains("DELETE SUBNETS AND KEEP DEVICES", source, StringComparison.Ordinal);
      }

      [Fact]
      public void Harness_ConfirmedNetworkWriteIsReachableOnlyThroughDoubleGatedApply()
      {
          var result = RunStaticAstAssertion("""
              function Get-OwningFunction([System.Management.Automation.Language.Ast] $Node) {
                  for ($cursor = $Node.Parent; $null -ne $cursor; $cursor = $cursor.Parent) {
                      if ($cursor -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
                          return $cursor
                      }
                  }
                  return $null
              }

              function Get-Calls([string] $Name) {
                  @($ast.FindAll({
                      param($node)
                      $node -is [System.Management.Automation.Language.CommandAst] -and
                          $node.GetCommandName() -ceq $Name
                  }, $true))
              }

              function Assert-OnlyCalledFrom([string] $Callee, [string] $Caller) {
                  $calls = @(Get-Calls $Callee)
                  if ($calls.Count -eq 0) { throw "Expected at least one call to $Callee." }
                  foreach ($call in $calls) {
                      $owner = Get-OwningFunction $call
                      if ($null -eq $owner -or $owner.Name -cne $Caller) {
                          throw "$Callee is callable outside $Caller."
                      }
                  }
              }

              $networkWriteCalls = @($ast.FindAll({
                  param($node)
                  if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
                      return $false
                  }
                  if ($node.GetCommandName() -notin @(
                      'Invoke-McpToolCall',
                      'Invoke-McpToolCallExpectingError')) {
                      return $false
                  }
                  $node.Extent.Text -match '(?i)-Name\s+[''"]network_write[''"]'
              }, $true))
              if ($networkWriteCalls.Count -eq 0) { throw 'No public network_write calls found.' }

              $confirmedCalls = @()
              foreach ($call in $networkWriteCalls) {
                  $confirm = [regex]::Matches(
                      $call.Extent.Text,
                      '(?i)\bconfirm\s*=\s*\$(true|false)\b')
                  if ($confirm.Count -ne 1) {
                      throw 'Every network_write call must use one literal confirm boolean.'
                  }
                  if ($confirm[0].Groups[1].Value -ieq 'true') { $confirmedCalls += $call }
              }
              if ($confirmedCalls.Count -ne 1) {
                  throw "Expected exactly one confirmed network_write; found $($confirmedCalls.Count)."
              }
              $confirmedOwner = Get-OwningFunction $confirmedCalls[0]
              if ($null -eq $confirmedOwner -or $confirmedOwner.Name -cne 'Invoke-NetworkWriteApply') {
                  throw 'The confirmed network_write is outside Invoke-NetworkWriteApply.'
              }

              Assert-OnlyCalledFrom 'Invoke-NetworkWriteApply' 'Invoke-LifecycleGroupAndVerify'
              Assert-OnlyCalledFrom 'Invoke-LifecycleGroupAndVerify' 'Invoke-Apply'

              $applyCalls = @(Get-Calls 'Invoke-Apply')
              if ($applyCalls.Count -ne 1 -or $null -ne (Get-OwningFunction $applyCalls[0])) {
                  throw 'Invoke-Apply must have exactly one top-level call site.'
              }
              $switch = $applyCalls[0].Parent
              while ($null -ne $switch -and
                     $switch -isnot [System.Management.Automation.Language.SwitchStatementAst]) {
                  $switch = $switch.Parent
              }
              if ($null -eq $switch -or $switch.Condition.Extent.Text.Trim() -cne '$Mode') {
                  throw 'Invoke-Apply is not dispatched by switch ($Mode).'
              }
              $owningClauses = @($switch.Clauses | Where-Object {
                  $applyCalls[0].Extent.StartOffset -ge $_.Item2.Extent.StartOffset -and
                  $applyCalls[0].Extent.EndOffset -le $_.Item2.Extent.EndOffset
              })
              if ($owningClauses.Count -ne 1 -or
                  $owningClauses[0].Item1.Extent.Text -cne "'Apply'") {
                  throw 'Invoke-Apply is reachable from a non-Apply switch clause.'
              }

              $applyGates = @($ast.FindAll({
                  param($node)
                  $node -is [System.Management.Automation.Language.IfStatementAst] -and
                      $node.Clauses.Count -gt 0 -and
                      $node.Clauses[0].Item1.Extent.Text -match
                          '^\s*\$Mode\s+-eq\s+[''"]Apply[''"]\s*$'
              }, $true) | Where-Object { $null -eq (Get-OwningFunction $_) })
              if ($applyGates.Count -ne 1) { throw 'Expected one top-level Apply gate.' }
              $gateText = $applyGates[0].Clauses[0].Item2.Extent.Text
              if ($gateText -notmatch 'if\s*\(\s*-not\s+\$AllowMutation\s*\)' -or
                  $gateText -notmatch '\$Acknowledgement\s+-cne\s+\$script:RequiredAcknowledgement') {
                  throw 'Apply gate does not enforce both mutation acknowledgements.'
              }

              $hostStarts = @(Get-Calls 'Connect-McpHost' |
                  Where-Object { $null -eq (Get-OwningFunction $_) })
              if ($hostStarts.Count -ne 1 -or
                  $applyGates[0].Extent.EndOffset -ge $hostStarts[0].Extent.StartOffset) {
                  throw 'Apply gates do not dominate the top-level host start.'
              }

              $candidateChecks = @(Get-Calls 'Assert-FrozenCandidate' |
                  Where-Object { $null -eq (Get-OwningFunction $_) })
              if ($candidateChecks.Count -ne 1 -or
                  $candidateChecks[0].Extent.EndOffset -ge $hostStarts[0].Extent.StartOffset) {
                  throw 'Frozen-candidate validation does not dominate the top-level host start.'
              }

              'apply-path-static-contract-ok'
              """);

          Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
          Assert.Equal("apply-path-static-contract-ok", result.StandardOutput.Trim());
      }

      [Fact]
      public void Harness_FrozenCandidateGuardBindsCommitTreeHarnessAndCleanPaths()
      {
          var source = HarnessSource;
          Assert.Contains("rev-parse HEAD", source, StringComparison.Ordinal);
          Assert.Contains("HEAD^{tree}", source, StringComparison.Ordinal);
          Assert.Contains("Get-FileHash -LiteralPath $PSCommandPath", source, StringComparison.Ordinal);
          Assert.Contains("$ExpectedCommit", source, StringComparison.Ordinal);
          Assert.Contains("$ExpectedTree", source, StringComparison.Ordinal);
          Assert.Contains("$ExpectedHarnessSha256", source, StringComparison.Ordinal);
          Assert.Matches(new Regex(@"\$testedCommit\s+-cne\s+\$ExpectedCommit"), source);
          Assert.Matches(new Regex(@"\$testedTree\s+-cne\s+\$ExpectedTree"), source);
          Assert.Matches(
              new Regex(@"\$testedHarnessSha256\s+-ine\s+\$ExpectedHarnessSha256"),
              source);
          Assert.Contains("status --porcelain=v1", source, StringComparison.Ordinal);
          foreach (var path in new[]
                   {
                       "TiaMcpServer", "TiaMcpServer.Contracts", "TiaMcpServer.OpennessWorker",
                       "TiaMcpServer.FakeWorker", "TiaMcpServer.Tests",
                       "scripts/live-test-network-phase4-subnets.ps1",
                   })
          {
              Assert.Contains(path, source, StringComparison.Ordinal);
          }
      }

      [Fact]
      public void Harness_UsesOnlyThePublicMcpRoute()
      {
          var source = HarnessSource;
          foreach (var method in new[]
                   {
                       "initialize", "notifications/initialized", "tools/list",
                       "get_project_status", "network_read", "network_write",
                   })
          {
              Assert.Contains(method, source, StringComparison.Ordinal);
          }

          Assert.DoesNotContain("probe_subnet_lifecycle_mutations", source, StringComparison.Ordinal);
          Assert.DoesNotMatch(new Regex(@"name\s*=\s*['""]save_project['""]"), source);
          Assert.DoesNotMatch(new Regex(@"name\s*=\s*['""]compile_check['""]"), source);
      }

      [Fact]
      public void Harness_RequiresAnAp21PathAndExactConnectedSubnetIds()
      {
          var source = HarnessSource;
          Assert.Contains(".ap21", source, StringComparison.OrdinalIgnoreCase);
          Assert.Contains("ConnectedEthernetSubnetId", source, StringComparison.Ordinal);
          Assert.Contains("ConnectedProfibusSubnetId", source, StringComparison.Ordinal);
          Assert.Contains("subnetId", source, StringComparison.Ordinal);
          Assert.Contains("A name is never accepted", source, StringComparison.Ordinal);
      }

      [Fact]
      public void Harness_RedactsTokensWritesOneTimestampedArtifactAndAlwaysStopsTheHost()
      {
          var source = HarnessSource;
          Assert.Contains("artifacts/live-network-phase4", source, StringComparison.Ordinal);
          Assert.Contains("[REDACTED]", source, StringComparison.Ordinal);
          Assert.Contains("finally", source, StringComparison.Ordinal);
          Assert.Contains("Stop-McpHost", source, StringComparison.Ordinal);
          Assert.Contains("rev-parse HEAD", source, StringComparison.Ordinal);
          Assert.Contains("HEAD^{tree}", source, StringComparison.Ordinal);
          Assert.Contains("testedCommit", source, StringComparison.Ordinal);
          Assert.Contains("testedTree", source, StringComparison.Ordinal);
          Assert.Contains("ExpectedCommit", source, StringComparison.Ordinal);
          Assert.Contains("ExpectedTree", source, StringComparison.Ordinal);
          Assert.Contains("ExpectedHarnessSha256", source, StringComparison.Ordinal);
          Assert.Contains("Assert-FrozenCandidate", source, StringComparison.Ordinal);
          Assert.Contains("TIA", source, StringComparison.Ordinal);
      }

      [Fact]
      public void Harness_RecordsTheUnchangedRootDeviceCountContract()
      {
          var source = HarnessSource;
          Assert.Contains("networkDeviceCount", source, StringComparison.Ordinal);
          Assert.Contains("networkDeviceCountUnchanged", source, StringComparison.Ordinal);
          Assert.Contains("ConnectedNodeNames", source, StringComparison.Ordinal);
          Assert.DoesNotMatch(new Regex(@"device[^\r\n]*\.Delete\("), source);
      }

      private static string FindRepositoryFile(params string[] segments)
      {
          for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
               directory is not null;
               directory = directory.Parent)
          {
              if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
              {
                  return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
              }
          }

          throw new InvalidOperationException("Could not locate the repository root.");
      }

      private static ScriptResult RunStaticAstAssertion(string assertionBody)
      {
          var harnessPath = FindRepositoryFile("scripts", "live-test-network-phase4-subnets.ps1");
          var syntheticSource = $$"""
              Set-StrictMode -Version Latest
              $ErrorActionPreference = 'Stop'
              $tokens = $null
              $parseErrors = $null
              $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                  {{PowerShellLiteral(harnessPath)}},
                  [ref] $tokens,
                  [ref] $parseErrors)
              if ($parseErrors.Count -ne 0) {
                  throw ($parseErrors | ForEach-Object Message | Out-String)
              }

              {{assertionBody}}
              """;
          var syntheticPath = Path.Combine(
              Path.GetTempPath(),
              $"phase4-harness-static-{Guid.NewGuid():N}.ps1");
          File.WriteAllText(
              syntheticPath,
              syntheticSource,
              new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

          try
          {
              var startInfo = new ProcessStartInfo
              {
                  FileName = "pwsh",
                  RedirectStandardOutput = true,
                  RedirectStandardError = true,
                  UseShellExecute = false,
                  CreateNoWindow = true,
              };
              startInfo.ArgumentList.Add("-NoProfile");
              startInfo.ArgumentList.Add("-File");
              startInfo.ArgumentList.Add(syntheticPath);

              using var process = Process.Start(startInfo)
                  ?? throw new InvalidOperationException("Failed to start pwsh.");
              var standardOutput = process.StandardOutput.ReadToEndAsync();
              var standardError = process.StandardError.ReadToEndAsync();
              if (!process.WaitForExit(30_000))
              {
                  process.Kill(entireProcessTree: true);
                  process.WaitForExit(5_000);
                  throw new TimeoutException("Static harness parser timed out.");
              }

              return new ScriptResult(
                  process.ExitCode,
                  standardOutput.GetAwaiter().GetResult(),
                  standardError.GetAwaiter().GetResult());
          }
          finally
          {
              File.Delete(syntheticPath);
          }
      }

      private static string PowerShellLiteral(string value)
          => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

      private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
  }
  ```

  The tests may launch `pwsh` only to parse and inspect the target file's AST through a separate
  temporary script. They never dot-source, invoke, or extract/execute a function from the live
  harness, never start the MCP host, and never attach to TIA Portal.

- [ ] **Step 4: Run the contract tests before adapting the script**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo `
    --filter FullyQualifiedName~NetworkSubnetLifecycleLiveHarnessScriptTests
  ```

  Outcome rule:

  - If a test fails, that exact failure is the RED authorizing the smallest corresponding script
    repair.
  - If every test passes, do not edit the restored script and record that no adaptation was needed.
  - Never weaken a test merely to fit the external copy.

- [ ] **Step 5: Apply only demonstrated compatibility repairs**

  The known historical copy may still describe a net8.0 host. If the restored file contains that
  stale text and the contract review confirms the current host is .NET 10, replace only the stale
  runtime wording with `net10.0`; keep the worker described as `net48`.

  Add mandatory frozen-candidate parameters (`ExpectedCommit`, `ExpectedTree`, and
  `ExpectedHarnessSha256`) and a pre-host validation function. The function must compute the
  current values, compare all three ordinally (hash case may be normalized), and reject any dirty
  production/test/harness path. Its core is:

  ```powershell
  function Assert-FrozenCandidate {
      param(
          [Parameter(Mandatory)] [string] $ExpectedCommit,
          [Parameter(Mandatory)] [string] $ExpectedTree,
          [Parameter(Mandatory)] [string] $ExpectedHarnessSha256
      )

      $testedCommit = (& git -C $script:RepositoryRoot rev-parse HEAD).Trim()
      $testedTree = (& git -C $script:RepositoryRoot rev-parse 'HEAD^{tree}').Trim()
      $testedHarnessSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
      if ($LASTEXITCODE -ne 0 -or
          [string]::IsNullOrWhiteSpace($testedCommit) -or
          [string]::IsNullOrWhiteSpace($testedTree)) {
          throw 'Could not resolve the current Git commit and tree.'
      }
      if ($testedCommit -cne $ExpectedCommit -or $testedTree -cne $ExpectedTree -or
          $testedHarnessSha256 -ine $ExpectedHarnessSha256) {
          throw 'Current source or harness does not match the frozen Task 7 candidate.'
      }

      $candidatePaths = @(
          'TiaMcpServer',
          'TiaMcpServer.Contracts',
          'TiaMcpServer.OpennessWorker',
          'TiaMcpServer.FakeWorker',
          'TiaMcpServer.Tests',
          'scripts/live-test-network-phase4-subnets.ps1'
      )
      $dirty = @(& git -C $script:RepositoryRoot status --porcelain=v1 `
          --untracked-files=all -- @candidatePaths)
      if ($LASTEXITCODE -ne 0) { throw 'Could not verify the frozen candidate worktree.' }
      if ($dirty.Count -ne 0) {
          throw "Frozen candidate paths are dirty:`n$($dirty -join "`n")"
      }

      [ordered]@{
          testedCommit       = $testedCommit
          testedTree         = $testedTree
          testedHarnessSha256 = $testedHarnessSha256.ToLowerInvariant()
      }
  }
  ```

  Call `Assert-FrozenCandidate` once at top level after argument-only gates but before
  `Connect-McpHost`; carry its returned values into every artifact. This comparison must occur on
  every Inventory, Preview, and Apply invocation, not only when the report is written. Continue
  deriving the TIA version from a public status/read response; do not query the worker directly.
  Keep the real safety token only in the immediate apply variable and persist `[REDACTED]` instead.

- [ ] **Step 6: Parse the script without executing it**

  Run:

  ```powershell
  $tokens = $parseErrors = $null
  [System.Management.Automation.Language.Parser]::ParseFile(
      (Resolve-Path scripts/live-test-network-phase4-subnets.ps1),
      [ref]$tokens,
      [ref]$parseErrors) | Out-Null
  if ($parseErrors.Count -ne 0) { throw ($parseErrors | Out-String) }
  ```

  Expected: no parser errors. This is not a live run.

- [ ] **Step 7: Rerun the static contract and observe GREEN**

  Run the command from Step 4.

  Expected: all harness tests pass. The AST contract proves every public `network_write` has one
  literal confirmation value, the sole confirmed call is reachable only through the Apply call
  chain, both Apply gates and the frozen-candidate check dominate host startup, and Preview has no
  confirmed call. The script never saves, compiles, calls a worker probe, or deletes a device.

- [ ] **Step 8: Review and checkpoint**

  Compare the adapted script to the byte-exact restoration checkpoint (or, when that checkpoint was
  not authorized, to the recorded restored hashes) and account for every changed line. When commit
  authorization exists:

  ```powershell
  git add scripts/live-test-network-phase4-subnets.ps1 `
    TiaMcpServer.Tests/Network/NetworkSubnetLifecycleLiveHarnessScriptTests.cs
  git commit -m "test(network): guard phase 4 live acceptance"
  ```

### Task 6: Correct Phase 4 documentation before live acceptance

**Files:**

- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/roadmap/network-operations.md`
- Modify: `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`

**Interfaces:**

- Consumes: Tasks 1-5's final static behavior and the known historical Phase 4 run.
- Produces: current documentation that distinguishes repaired static verification, historical live
  evidence tied to its old commit, and the still-pending current-revision public live gate.

- [ ] **Step 1: Update the architecture seam and locked request/result contract**

  In `docs/ARCHITECTURE.md` §7a's Phase 4 subnet lifecycle seam and in the focused Phase 4
  reference, state explicitly:

  - update/delete require `target.kind: "subnet"` and exact ordinal `subnetId`;
  - every successful lifecycle result requires all four raw JSON members;
  - late zero/multiple worker matches are `postcondition_failed`;
  - delete validates type and captures a nonblank name from the same transaction-local object before
    `Delete()`, so the identity used for mutation cannot drift from a pre-transaction object; and
  - the operation does not save or compile.

- [ ] **Step 2: Correct evidence/status claims**

  In the roadmap, focused Phase 4 reference, and summary:

  - remove any “no discrepancy” statement invalidated by the audit;
  - acknowledge the historical Phase 4 run only as evidence for its recorded older commit;
  - state that it is not evidence for the current PR 1 tree; and
  - keep current-revision Inventory/Preview/Apply marked unverified until Task 8 completes.

  In the improvement log, record the four code-contract repairs and user-restored harness guard as
  completed only after their automated gates pass. Keep the live gate open.

- [ ] **Step 3: Validate documentation mechanically**

  Run:

  ```powershell
  rg -n "no discrepancy|has not been performed|net8\.0" `
    docs/ARCHITECTURE.md `
    docs/roadmap/network-operations.md `
    docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md `
    docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md `
    scripts/live-test-network-phase4-subnets.ps1
  git diff --check
  ```

  Expected: no stale absolute claim or net8.0 harness wording remains; any historical-run sentence
  is explicitly qualified by its tested commit. `git diff --check` exits zero.

- [ ] **Step 4: Review and checkpoint**

  When commit authorization exists:

  ```powershell
  git add docs/ARCHITECTURE.md `
    docs/roadmap/network-operations.md `
    docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md `
    docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md `
    docs/IMPROVEMENT_LOG.md
  git commit -m "docs(network): correct phase 4 acceptance boundary"
  ```

### Task 7: Run the complete non-live merge gate

**Files:**

- Verify: all PR 1 production, test, harness, and documentation files.
- Do not create or modify live evidence in this task.

**Interfaces:**

- Consumes: the final PR 1 implementation tree before live testing.
- Produces: one immutable candidate commit/tree eligible for separately authorized live acceptance.

- [ ] **Step 1: Run the focused Phase 4 slice**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 `
    --disable-build-servers --nologo `
    --filter "FullyQualifiedName~NetworkSubnetLifecycle|FullyQualifiedName~BatchToolMetadataTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ReadOnlyModeHardeningTests"
  ```

  Expected: all focused tests pass.

- [ ] **Step 2: Run the full suite serially**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 `
    --disable-build-servers --nologo
  ```

  Expected: the complete suite passes.

- [ ] **Step 3: Run Release stub and real-reference builds serially**

  Run:

  ```powershell
  dotnet build TiaMcpServer.sln -c Release --no-restore -m:1 `
    /p:UseTiaPortalReferenceStubs=true --nologo
  dotnet build TiaMcpServer.sln -c Release --no-restore -m:1 `
    /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" `
    --nologo
  ```

  Expected: both builds succeed. Neither is live TIA evidence.

- [ ] **Step 4: Run scoped coverage and enforce the repository threshold**

  Run after the Release build:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release -m:1 `
    --disable-build-servers --collect:"XPlat Code Coverage" `
    --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults
  $reports = @(Get-ChildItem -Path TestResults -Recurse -Filter coverage.cobertura.xml)
  if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
  ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
  ```

  Expected: the suite passes and line coverage is at least `0.80`.

- [ ] **Step 5: Require a committed executable candidate and persist its identity**

  The live candidate is a commit, not a dirty working tree. First require every production, test, and
  harness path to be clean, then persist the exact Task 7 identity in an ignored local manifest:

  ```powershell
  $candidatePaths = @(
      'TiaMcpServer',
      'TiaMcpServer.Contracts',
      'TiaMcpServer.OpennessWorker',
      'TiaMcpServer.FakeWorker',
      'TiaMcpServer.Tests',
      'scripts/live-test-network-phase4-subnets.ps1'
  )
  $candidateStatus = @(git status --porcelain=v1 --untracked-files=all -- @candidatePaths)
  if ($LASTEXITCODE -ne 0) { throw 'Could not inspect candidate paths.' }
  if ($candidateStatus.Count -ne 0) {
      throw "Commit the executable candidate before live testing:`n$($candidateStatus -join "`n")"
  }

  git diff --check
  git status --short
  $candidateCommit = (git rev-parse HEAD).Trim()
  $candidateTree = (git rev-parse 'HEAD^{tree}').Trim()
  $candidateHarnessSha256 = (
      Get-FileHash -LiteralPath scripts/live-test-network-phase4-subnets.ps1 -Algorithm SHA256
  ).Hash.ToLowerInvariant()
  if ($LASTEXITCODE -ne 0 -or
      [string]::IsNullOrWhiteSpace($candidateCommit) -or
      [string]::IsNullOrWhiteSpace($candidateTree)) {
      throw 'Could not resolve the frozen candidate identity.'
  }

  $candidateRoot = Join-Path (Resolve-Path .) 'artifacts/live-network-phase4'
  [void](New-Item -ItemType Directory -Force -Path $candidateRoot)
  $candidateManifestPath = Join-Path $candidateRoot 'candidate.json'
  [ordered]@{
      schemaVersion = 'network-phase4-live-candidate/v1'
      testedCommit = $candidateCommit
      testedTree = $candidateTree
      harnessSha256 = $candidateHarnessSha256
      frozenAtUtc = (Get-Date).ToUniversalTime().ToString('o')
  } | ConvertTo-Json | Set-Content -LiteralPath $candidateManifestPath -Encoding utf8NoBOM
  Get-FileHash -LiteralPath $candidateManifestPath -Algorithm SHA256
  ```

  Record the manifest path and hash with its commit, tree, and harness SHA-256. Review the entire PR
  diff against its merged-main base. Fixes discovered now use a new focused RED/GREEN cycle and
  repeat this task; do not hand-edit the manifest to bless a new candidate.

- [ ] **Step 6: Freeze the live candidate**

  Stop modifying production code, automated tests, or the harness. A new commit, checkout, rebase,
  harness-byte change, or dirty candidate path invalidates the manifest and requires the complete
  Task 7 gate again before live testing. The harness must reject that drift before starting its MCP
  host; a post-run comparison alone is not sufficient.

### Task 8: Obtain fresh authorization and run public TIA V21 acceptance

**Files:**

- Execute without editing: `scripts/live-test-network-phase4-subnets.ps1`
- Create after factual evidence exists:
  `docs/superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md`
- Modify after report creation: `docs/superpowers/README.md`
- Modify after report creation: `docs/README.md`
- Modify after the final verdict: `docs/roadmap/network-operations.md`
- Modify after the final verdict: `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
- Modify after the final verdict: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify after the final verdict: `docs/IMPROVEMENT_LOG.md`

**Interfaces:**

- Consumes: the immutable Task 7 candidate, an exact disposable `.ap21` project, and exact connected
  Ethernet/PROFIBUS subnet IDs discovered from that project.
- Produces: sanitized public-path Inventory, Preview, and Apply evidence tied to the exact candidate
  commit/tree/harness, followed only by evidence/status documentation.

- [ ] **Step 1: Stop and request fresh live authorization**

  Ask for one authorization that names:

  - the exact absolute disposable `.ap21` project path;
  - permission to attach to its already-running TIA Portal V21 process;
  - permission to run Inventory and Preview;
  - the exact connected Ethernet and PROFIBUS `subnetId` values approved for deletion; and
  - permission for one Apply using `-AllowMutation` and the phrase
    `DELETE SUBNETS AND KEEP DEVICES`.

  Do not infer this authorization from design/plan approval. Do not run any harness mode before the
  user grants it.

- [ ] **Step 2: Load and independently verify the frozen candidate manifest**

  Load the manifest written by Task 7 and fail closed before any harness invocation:

  ```powershell
  $candidateManifestPath = (Resolve-Path `
      artifacts/live-network-phase4/candidate.json).Path
  $candidateManifestSha256 = (
      Get-FileHash -LiteralPath $candidateManifestPath -Algorithm SHA256
  ).Hash.ToLowerInvariant()
  $candidate = Get-Content -LiteralPath $candidateManifestPath -Raw | ConvertFrom-Json
  if ($candidate.schemaVersion -cne 'network-phase4-live-candidate/v1') {
      throw 'Unexpected live-candidate manifest schema.'
  }
  $currentCommit = (git rev-parse HEAD).Trim()
  $currentTree = (git rev-parse 'HEAD^{tree}').Trim()
  $currentHarnessSha256 = (
      Get-FileHash -LiteralPath scripts/live-test-network-phase4-subnets.ps1 -Algorithm SHA256
  ).Hash.ToLowerInvariant()
  if ($currentCommit -cne $candidate.testedCommit -or
      $currentTree -cne $candidate.testedTree -or
      $currentHarnessSha256 -ine $candidate.harnessSha256) {
      throw 'Task 7 candidate identity has drifted; repeat Task 7.'
  }
  ```

  Do not update the manifest to fit a mismatch. Repeat Task 7 after the appropriate TDD cycle.
  Each harness mode independently repeats these checks and rejects dirty executable candidate paths
  before it starts the MCP host.

- [ ] **Step 3: Run Inventory only and review its artifact**

  After authorization, assign the exact approved project value and run:

  ```powershell
  pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -ProjectPath $authorizedProjectPath -Mode Inventory `
    -ExpectedCommit $candidate.testedCommit `
    -ExpectedTree $candidate.testedTree `
    -ExpectedHarnessSha256 $candidate.harnessSha256
  ```

  Review the timestamped artifact before continuing. Confirm exact project/session identity,
  positive Portal process identity, TIA version, root device count, and the two authorized IDs.
  Inventory must contain no `network_write` apply and leave the project unmodified.

- [ ] **Step 4: Run Preview only and review every operation**

  Run:

  ```powershell
  pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -ProjectPath $authorizedProjectPath -Mode Preview `
    -ConnectedEthernetSubnetId $authorizedEthernetSubnetId `
    -ConnectedProfibusSubnetId $authorizedProfibusSubnetId `
    -ExpectedCommit $candidate.testedCommit `
    -ExpectedTree $candidate.testedTree `
    -ExpectedHarnessSha256 $candidate.harnessSha256
  ```

  Confirm previews cover isolated Ethernet/PROFIBUS create, update, delete, and the two exact
  connected deletes; tokens are redacted; negative cases fail before mutation; and no confirm=true
  call ran.

- [ ] **Step 5: Reconfirm the destructive target and run Apply once**

  Immediately before the mutation, restate the exact project path and both connected subnet IDs to
  the user. Only after that final confirmation, run:

  ```powershell
  pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -ProjectPath $authorizedProjectPath -Mode Apply `
    -ConnectedEthernetSubnetId $authorizedEthernetSubnetId `
    -ConnectedProfibusSubnetId $authorizedProfibusSubnetId `
    -ExpectedCommit $candidate.testedCommit `
    -ExpectedTree $candidate.testedTree `
    -ExpectedHarnessSha256 $candidate.harnessSha256 `
    -AllowMutation -Acknowledgement 'DELETE SUBNETS AND KEEP DEVICES'
  ```

  Expected: the harness creates and updates isolated Ethernet/PROFIBUS subnets, deletes those
  created subnets, deletes only the two named connected subnets, reports exact four-member results,
  proves deleted IDs absent, proves the root device count unchanged, and records retained-device
  relationship clearing only as expected project-state evidence. It does not save or compile.

  On timeout, worker crash, Siemens exception, or postcondition failure: stop. Inspect current
  project state before any corrective action. Never replay automatically.

- [ ] **Step 6: Write the factual acceptance report**

  Create the report only now, with actual values for:

  - verdict and UTC start/end;
  - tested commit, tree, final harness SHA-256, and Task 7 manifest SHA-256;
  - user-supplied saved-copy source locator, original commit/blob when known (otherwise the explicit
    `unversioned user-provided saved copy` classification), and computed restored SHA-256/blob ID;
  - host/worker identity and version;
  - TIA Portal and Openness versions;
  - exact disposable fixture identity and discard/restoration policy;
  - exact authorized selectors;
  - Inventory, Preview, negative-case, Apply, post-read, audit, and device-count verdicts;
  - sanitized artifact paths and SHA-256 values;
  - explicit confirmation that no save, compile, download, commissioning, retry, or device delete
    occurred; and
  - untested boundaries.

  Link the report from both documentation indexes and update current Phase 4 status in the roadmap,
  supported-operation references, and improvement log. Keep the older run explicitly historical.

- [ ] **Step 7: Prove the live-tested tree was not changed**

  After evidence documentation edits, run:

  ```powershell
  $candidatePaths = @(
      'TiaMcpServer',
      'TiaMcpServer.Contracts',
      'TiaMcpServer.OpennessWorker',
      'TiaMcpServer.FakeWorker',
      'TiaMcpServer.Tests',
      'scripts/live-test-network-phase4-subnets.ps1'
  )
  $changed = @(git diff --name-only $candidate.testedCommit -- @candidatePaths)
  $dirty = @(git status --porcelain=v1 --untracked-files=all -- @candidatePaths)
  $currentHarnessSha256 = (
      Get-FileHash -LiteralPath scripts/live-test-network-phase4-subnets.ps1 -Algorithm SHA256
  ).Hash.ToLowerInvariant()
  if ($changed.Count -ne 0 -or $dirty.Count -ne 0 -or
      $currentHarnessSha256 -ine $candidate.harnessSha256) {
      throw 'Production, tests, or harness changed after the frozen live candidate.'
  }
  ```

  Expected: no changed/dirty candidate paths and the exact frozen harness hash. Also compare each
  live artifact's `testedCommit`, `testedTree`, and `testedHarnessSha256` to the manifest. If any
  value differs, the live result is stale; rerun Tasks 7 and 8 after the new RED/GREEN cycle.

- [ ] **Step 8: Validate report links and checkpoint evidence**

  Run:

  ```powershell
  git diff --check
  rg -n "2026-09-21-network-phase4-current-revision-live" docs/README.md docs/superpowers/README.md
  ```

  Expected: clean diff checks and links in both indexes.

  When commit authorization exists:

  ```powershell
  git add docs/superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md `
    docs/superpowers/README.md docs/README.md `
    docs/roadmap/network-operations.md `
    docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md `
    docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md `
    docs/IMPROVEMENT_LOG.md
  git commit -m "test(network): record phase 4 live acceptance"
  ```

### Task 9: Final PR 1 review and handoff

**Files:**

- Review: the complete PR 1 diff against its latest merged `main` base.
- Do not modify: production, automated tests, or harness after accepted live evidence.

**Interfaces:**

- Consumes: completed Tasks 1-8 and the durable live report.
- Produces: a merge-ready PR 1 only; it does not start PR 2 or release work.

- [ ] **Step 1: Rerun non-mutating final checks**

  Run:

  ```powershell
  git diff --check
  git status --short
  git log --oneline --decorate -n 12
  ```

  Review the final diff for accidental scope, token leakage, local paths, secrets, stale status
  claims, and any production/test/harness change after the live-tested commit.

- [ ] **Step 2: Confirm the PR boundary**

  The pull request title should be:

  ```text
  fix(network): finish Phase 4 subnet lifecycle
  ```

  Its body must summarize the four behavior fixes, the restored harness's recorded origin
  classification and content hashes, strict TDD evidence, full offline/reference gates, exact
  current-revision live verdict, no-save/no-compile boundary, and no-release boundary. It must not
  claim Phase 5/6 implementation.

- [ ] **Step 3: Request remote-write authorization**

  Do not push or create the pull request until the user explicitly authorizes the remote write. Do
  not merge it. After it merges into `main`, review the final merged diff and acceptance report;
  only then write the detailed PR 2 qualification plan from that exact `main`.
