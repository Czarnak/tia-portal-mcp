# Hardware Configuration Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded, resumable pagination to `network_read/read_hardware_config` while preserving the existing unpaged response and keeping Siemens Openness traversal and host-side canonical JSON assembly on their correct sides of the process boundary.

**Architecture:** The existing unpaged call continues through `read_hardware_config`. A paged call goes through a host `HardwarePaginationCoordinator`, an internal worker method named `read_hardware_page_candidates`, a strictly typed candidate payload, and a host projector that returns the largest complete canonical page at or below 60,000 characters. A versioned HMAC-SHA256 cursor binds the query, project/session identity, host binding snapshot, stable descriptor snapshot, and next offset. The internal worker method is access-policy registered but is not added to the public Network operation catalog.

**Tech Stack:** C#; .NET 8 host; .NET Framework 4.8 worker; `netstandard2.0` shared contracts; `System.Text.Json`; `System.Security.Cryptography`; xUnit; the existing newline-delimited worker IPC, canonical JSON, structured-operation batch, FakeWorker, and PowerShell live-harness patterns.

**Spec:** [`docs/superpowers/specs/2026-08-28-issue-31-project-completeness-pagination-design.md`](../specs/2026-08-28-issue-31-project-completeness-pagination-design.md)

## Global Constraints

- Preserve the public behavior and serialization of an unpaged `read_hardware_config` request. Only `pageSize != null` or `cursor != null` selects the new seam.
- Count `pageSize` across the combined stable sequence of matching devices followed by matching subnets. Keep devices and subnets in their existing separate public arrays.
- Keep Siemens objects and traversal inside the net48 worker. Only shared DTOs, structural locator evidence, hashes, counts, diagnostics, and materialized public candidates cross IPC.
- Treat `WorkerResponse.SessionIdentity` / `WorkerCallResult.SessionIdentity` as the sole observed worker-session identity. Do not duplicate it inside the candidate payload.
- Authenticate cursors with a process-local random HMAC-SHA256 key. Cursors are opaque and intentionally invalid after a host restart; they are authenticated, not encrypted.
- Bind cursors to normalized `deviceName`, exact `plcName`, `includeIoDetails`, and `includeTagMatches`. `pageSize` is not query identity and may change between pages within the valid range.
- A cursor continuation may omit `projectPath`; the host then supplies the cursor's resolved path. If the caller repeats `projectPath`, it must resolve to the same path.
- Support explicit-project pagination while the host remains unbound. Do not create or rotate a host binding merely to page a project.
- Use the existing expected-session field to pin continuations. A continuation worker call must receive the cursor identity as `ExpectedSessionIdentity`.
- Keep the failure taxonomy exact:
  - `validation_error`: invalid `pageSize` or dependency validation.
  - `invalid_cursor`: malformed encoding, unsupported version, invalid schema, or bad signature.
  - `cursor_filter_mismatch`: cursor/query mismatch.
  - `cursor_binding_mismatch`: host binding revision/path or worker-session mismatch during a continuation.
  - `cursor_snapshot_mismatch`: stable candidate set/order changed.
  - `cursor_out_of_range`: offset is outside the current candidate set.
  - `protocol_error`: missing response identity, malformed typed candidate data, or incoherent counts/offsets.
- Preserve page-level diagnostics, then include per-candidate diagnostics only for candidates actually returned. Do not leak diagnostics for trimmed candidates onto an earlier page.
- Measure each prospective public operation item with `CanonicalJson.Serialize`. Never split an entity or diagnostic string. Return the largest complete prefix at or below 60,000 characters, then let the existing 180,000-character structured-batch budget run normally.
- If page-level diagnostics alone exceed 60,000 characters, omit the operation with reason `hardwarePageDiagnosticsExceededItemCharLimit`, no subject, and no offset advance.
- If the first candidate that must be emitted cannot fit, omit the operation with a machine-readable `subject { kind, name, identifier }`, no internal locator, and no offset advance. A device identifier is absent/null; a subnet identifier is its `SubnetId`.
- Omission guidance must say: retry the unchanged request at the same cursor, or start a new sequence with narrower filters or fewer detail options. Never suggest changing detail flags on the same cursor.
- Do not add transactions, a deep project hash, server-side cursor cache, inner device pagination, a new public MCP tool, or a migration of `NetworkObjectCursorCodec`.
- Follow behavioral TDD. Observe the named focused test fail before adding each production behavior. Never use an unrelated compile failure as RED evidence.
- Do not run the live-TIA harness without separate explicit authorization. Stub/offline/FakeWorker evidence is not live-TIA acceptance.
- Do not commit merely because a task reaches its commit boundary. Every commit remains subject to explicit user authorization.

---

## Task 1: Define the Public and IPC Contracts

**Files:**

- Create: `TiaMcpServer.Contracts/HardwarePaginationInfo.cs`
- Create: `TiaMcpServer.Contracts/HardwarePageCandidateInfo.cs`
- Create: `TiaMcpServer.Contracts/HardwarePageEvidence.cs`
- Modify: `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.Contracts/WorkerFailureCategories.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationRequest.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Network/HardwarePageContractTests.cs`
- Modify: `TiaMcpServer.Tests/Network/HardwareConfigInfoTests.cs`
- Modify: `TiaMcpServer.Tests/Network/NetworkOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/Network/NetworkOperationCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatches/StructuredOperationBatchTests.cs`
- Modify: `TiaMcpServer.Tests/Worker/WorkerResponseJsonTests.cs`

- [ ] **Step 1: Write failing serialization and validation tests**

  Add tests that prove:

  - unpaged `HardwareConfigInfo` still omits `pagination`;
  - paged metadata serializes `totalDevices`, `totalSubnets`, `returnedDevices`, `returnedSubnets`, and nullable `nextCursor`;
  - `NetworkOperationRequest` accepts nullable `pageSize` and `cursor`;
  - request JSON still rejects unknown fields under the existing strict input contract;
  - only `read_hardware_config` accepts those fields;
  - `pageSize` must be in `1..200` when supplied;
  - cursor-only validation is accepted and defers the default page size to orchestration;
  - omission `subject` is optional and serializes only `kind`, `name`, and optional `identifier`;
  - `cursor_binding_mismatch` is a known worker failure category;
  - candidate payload JSON has no session-identity member.

  Use a representative public contract shape:

  ```csharp
  public sealed record HardwarePaginationInfo(
      int TotalDevices,
      int TotalSubnets,
      int ReturnedDevices,
      int ReturnedSubnets,
      string? NextCursor);

  public sealed record StructuredOperationOmissionSubject(
      string Kind,
      string Name,
      string? Identifier);
  ```

- [ ] **Step 2: Run the focused tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePageContractTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests|FullyQualifiedName~StructuredOperationBatchTests|FullyQualifiedName~WorkerResponseJsonTests"
  ```

  Expected RED: the new fields/types/category do not exist, or the catalog rejects/ignores the new dependency rules.

- [ ] **Step 3: Add the smallest shared contract implementation**

  Define these DTO responsibilities:

  ```csharp
  public sealed record HardwarePageContinuationInfo(
      int OrderingVersion,
      string QueryHash,
      string SnapshotHash,
      int Offset);

  public sealed record HardwarePageCandidateResultInfo(
      int OrderingVersion,
      string QueryHash,
      string SnapshotHash,
      int StartOffset,
      int TotalDevices,
      int TotalSubnets,
      IReadOnlyList<string> Messages,
      IReadOnlyList<HardwareDevicePageCandidateInfo> DeviceCandidates,
      IReadOnlyList<HardwareSubnetPageCandidateInfo> SubnetCandidates);
  ```

  Candidate DTOs carry `Offset`, the existing public `DeviceInfo` or `SubnetInfo`, and candidate-scoped `Messages`. Add `HardwarePageSize` and `HardwarePageContinuation` to `WorkerRequest`; do not reuse the network-object cursor fields.

  Add a shared, pure query/evidence layer that:

  - normalizes a missing boolean to `false` before hashing;
  - normalizes `deviceName` consistently with its ordinal-ignore-case matching;
  - preserves exact `plcName` semantics;
  - excludes `pageSize` from the query hash;
  - emits deterministic SHA-256 hashes using a fixed canonical field order and UTF-8.

  Extend `HardwareConfigInfo` with a nullable, null-omitted `Pagination` property. Extend `StructuredOperationOmission` with a nullable, null-omitted `Subject` property so existing omitted items remain unchanged.

- [ ] **Step 4: Implement operation validation without activating pagination yet**

  Update the request description and catalog dependency rules. The catalog should reject `pageSize`/`cursor` on every operation other than `read_hardware_config`, reject an out-of-range explicit `pageSize`, and allow cursor-only requests. Do not change `NetworkWorkerInvoker` in this task.

- [ ] **Step 5: Run focused tests and confirm GREEN**

  Run the Step 2 command again. Also run:

  ```powershell
  dotnet build TiaMcpServer.Contracts/TiaMcpServer.Contracts.csproj --no-restore -m:1 --disable-build-servers
  ```

- [ ] **Step 6: Review the contract diff**

  Verify that unpaged JSON is unchanged, candidate JSON contains no duplicated session identity, and no public operation has been added.

- [ ] **Step 7: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.Contracts TiaMcpServer/Network/NetworkOperationRequest.cs TiaMcpServer/Network/NetworkOperationCatalog.cs TiaMcpServer/OperationBatches/StructuredOperationBatch.cs TiaMcpServer.Tests
  git commit -m "feat(network): define hardware pagination contracts"
  ```

---

## Task 2: Authenticate and Validate Host Cursors

**Files:**

- Create: `TiaMcpServer/Network/HardwarePageCursorState.cs`
- Create: `TiaMcpServer/Network/HardwarePageCursorCodec.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Network/HardwarePageCursorCodecTests.cs`

- [ ] **Step 1: Write failing cursor tests**

  Cover round-trip and each rejection boundary:

  - deterministic round-trip with an injected 32-byte test key;
  - any payload or signature byte change gives `invalid_cursor`;
  - malformed base64url, missing/extra envelope parts, unsupported version, missing member, duplicate member, wrong JSON type, and trailing JSON give `invalid_cursor`;
  - query mismatch gives `cursor_filter_mismatch` before any worker call;
  - explicit path mismatch and host-binding snapshot mismatch give `cursor_binding_mismatch` before any worker call;
  - page-size changes do not invalidate a cursor;
  - a codec with a different process key rejects the cursor.

  Model the internal state explicitly:

  ```csharp
  internal sealed record HardwarePageCursorState(
      int Version,
      string ResolvedProjectPath,
      WorkerSessionIdentity SessionIdentity,
      ProjectBindingCursorState HostBinding,
      string QueryHash,
      int OrderingVersion,
      string SnapshotHash,
      int Offset);
  ```

  `ProjectBindingCursorState` must distinguish an unbound host from a bound host and, when bound, carry the binding ID, revision, and normalized project path needed for equality checks.

- [ ] **Step 2: Run the cursor tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePageCursorCodecTests"
  ```

- [ ] **Step 3: Implement the strict codec**

  Use the format:

  ```text
  base64url(canonical-json-payload).base64url(hmac-sha256(payload-bytes))
  ```

  Provide a testable constructor that accepts a key and a production factory such as `CreateProcessScoped()` that fills a private 32-byte key using `RandomNumberGenerator`. Compare signatures with `CryptographicOperations.FixedTimeEquals`. Parse exact known JSON members and reject unsupported formats rather than attempting compatibility guesses. Do not log or return decoded cursor state.

- [ ] **Step 4: Keep semantic validation separate from authentication**

  The codec authenticates and decodes. A small validator compares the decoded state with the normalized incoming query, supplied project path, and current `ProjectBindingSnapshot`, returning the exact failure category. This keeps malformed/authentication errors distinct from filter and binding mismatches.

- [ ] **Step 5: Run focused tests and confirm GREEN**

  Run the Step 2 command, then:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~WorkerResponseJsonTests"
  ```

- [ ] **Step 6: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/Network/HardwarePageCursorState.cs TiaMcpServer/Network/HardwarePageCursorCodec.cs TiaMcpServer.Tests/Network/HardwarePageCursorCodecTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "feat(network): authenticate hardware page cursors"
  ```

---

## Task 3: Build Stable Worker Descriptor Evidence

**Files:**

- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectDeviceEnumerator.cs`
- Create: `TiaMcpServer.OpennessWorker/HardwarePageDescriptorSet.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Network/HardwarePageDescriptorSetTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs`

- [ ] **Step 1: Write failing pure descriptor tests**

  Link `HardwarePageDescriptorSet.cs` into the net8 test project so ordering and snapshot behavior can be tested without Siemens assemblies. Cover:

  - devices are followed by subnets in the combined page sequence;
  - descriptors sort deterministically by entity kind, public identity, and stable source traversal order;
  - duplicate public names remain distinct because structural locator is part of descriptor identity and snapshot evidence, while stable source traversal order resolves equal public sort keys;
  - descriptor locators use collection indices such as `devices/0`, `deviceGroups/0/groups/2/devices/1`, and `subnets/3`;
  - any regroup/reorder/add/remove changes the snapshot hash;
  - detail flags do not alter descriptor ordering, but do alter the shared query hash;
  - device and PLC filters produce deterministic matching descriptors;
  - page offsets slice the combined descriptor sequence without duplication or gaps.

- [ ] **Step 2: Run pure tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePageDescriptorSetTests|FullyQualifiedName~ProjectTraversalSourceContractTests"
  ```

- [ ] **Step 3: Add location-aware traversal without changing existing enumeration**

  Add a location-bearing internal result and a new API such as:

  ```csharp
  internal sealed record LocatedProjectDevice(Device Device, string StructuralLocator, int SourceOrder);

  internal static IReadOnlyList<LocatedProjectDevice> EnumerateWithLocations(Project project);
  ```

  Keep `ProjectDeviceEnumerator.Enumerate(Project)` as a projection over `EnumerateWithLocations` so every PR1 caller retains the same direct-first/depth-first device order and flat public shape.

- [ ] **Step 4: Implement pure descriptor ordering and hashing**

  `HardwarePageDescriptorSet` receives already extracted evidence; it does not reference Siemens types. Descriptor evidence contains only entity kind, structural locator, sortable public identity, and source order. It orders devices by ordinal device name and subnets by ordinal `SubnetId`, with stable source traversal order resolving equal public keys. The structural locator participates in descriptor identity and snapshot hashing; do not lexically sort locator text because `devices/10` must not precede `devices/2`. The set computes totals, query-filtered stable order, snapshot hash, range validation, and the requested descriptor window.

  Do not expose structural locators in `HardwareConfigInfo`, pagination metadata, omission subjects, or diagnostic text.

- [ ] **Step 5: Run tests and confirm GREEN**

  Run the Step 2 command. Also run the PR1 traversal regression tests selected by `ProjectTraversalSourceContractTests` to confirm the old enumeration path is unchanged.

- [ ] **Step 6: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.OpennessWorker/Openness/ProjectDeviceEnumerator.cs TiaMcpServer.OpennessWorker/HardwarePageDescriptorSet.cs TiaMcpServer.Tests/Network/HardwarePageDescriptorSetTests.cs TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "feat(worker): enumerate hardware page descriptors"
  ```

---

## Task 4: Add the Internal Worker Candidate Reader

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/HardwarePageCandidateReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Network/HardwarePaginationWorkerSourceContractTests.cs`
- Create: `TiaMcpServer.Tests/Network/HardwarePageCandidateReaderTests.cs`

- [ ] **Step 1: Write failing worker seam tests**

  Add source-contract tests for the Siemens-dependent wiring and pure tests for candidate validation. Prove that:

  - `read_hardware_page_candidates` is dispatched by the worker;
  - the method is registered as an observe/read policy but is absent from `NetworkOperationCatalog`;
  - the worker calls `ValidateExpectedSessionIdentity` before materializing a continuation;
  - the worker enumerates/hash-checks the full matching descriptor set before materializing the requested window;
  - an ordering-version or snapshot change returns `cursor_snapshot_mismatch`;
  - an offset below zero or above the current count returns `cursor_out_of_range`;
  - exactly the selected descriptor window is materialized;
  - candidate-scoped messages remain attached to their candidate;
  - page-level messages remain separate;
  - the response identity stays in `WorkerResponse.SessionIdentity`, not the payload.

- [ ] **Step 2: Run focused tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationWorkerSourceContractTests|FullyQualifiedName~HardwarePageCandidateReaderTests"
  ```

- [ ] **Step 3: Extract narrow materializers from `HardwareConfigReader`**

  Reuse the existing public-shape construction and tag-index helpers. Expose only the smallest internal methods needed to materialize one located device or subnet plus its scoped messages. Leave `HardwareConfigReader.Read(...)` and its unpaged ordering/output behavior unchanged.

- [ ] **Step 4: Implement `HardwarePageCandidateReader`**

  Its sequence is:

  1. Resolve the exact project selected by the existing worker/session logic.
  2. Enumerate all matching lightweight descriptors and page-level messages.
  3. Compute ordering version, query hash, snapshot hash, and totals.
  4. Validate continuation evidence, if present.
  5. Select at most `HardwarePageSize` combined descriptors.
  6. Materialize only those selected candidates.
  7. Return the typed payload; let the worker envelope add the observed session identity.

  Use a default of 50 only when the host sends a cursor-only public request; normally the host should send an explicit effective size.

- [ ] **Step 5: Wire dispatch and access policy**

  Add the worker switch case for `read_hardware_page_candidates` and register it in `OperationPolicyCatalog` as observe/read-only. Do not register it as a generic batch Network operation and do not add it to public tool descriptions.

- [ ] **Step 6: Run focused tests and worker stub build**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationWorkerSourceContractTests|FullyQualifiedName~HardwarePageCandidateReaderTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~ProjectTraversalSourceContractTests"
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj --no-restore -m:1 --disable-build-servers /p:UseTiaPortalReferenceStubs=true
  ```

- [ ] **Step 7: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.OpennessWorker TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.Tests/Network/HardwarePaginationWorkerSourceContractTests.cs TiaMcpServer.Tests/Network/HardwarePageCandidateReaderTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "feat(worker): add hardware page candidate seam"
  ```

---

## Task 5: Pin Continuations Across the Host-to-Worker Call

**Files:**

- Create: `TiaMcpServer/Worker/HardwarePageWorkerCallResult.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Contracts/ProjectSessionBinding.cs` only if a focused snapshot comparison helper is needed by both cursor validation and the client
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Modify: `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`
- Create: `TiaMcpServer.Tests/Worker/HardwarePageWorkerClientTests.cs`

- [ ] **Step 1: Write failing client/binding tests**

  Cover both bound and unbound host states:

  - first page returns the `ProjectBindingSnapshot` captured inside the serialized binding operation and the worker result;
  - continuation checks a required host snapshot inside that same serialized operation before sending;
  - a changed bound binding ID, revision, or path fails locally with `cursor_binding_mismatch` and sends no worker request;
  - a cursor created while unbound continues only while the host is still unbound;
  - an unbound explicit-project continuation may omit its public `projectPath`; the worker request receives the cursor path;
  - a repeated equivalent explicit path is accepted; a different path fails locally;
  - the cursor session identity is put into `WorkerRequest.ExpectedSessionIdentity`;
  - a continuation response with missing identity is a `protocol_error`;
  - a response identity mismatch, or worker `binding_conflict` caused by continuation identity validation, becomes `cursor_binding_mismatch`;
  - first-page failures retain their original non-cursor failure category.

- [ ] **Step 2: Run focused tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePageWorkerClientTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  ```

- [ ] **Step 3: Add one dedicated serialized client entry point**

  Implement an API with equivalent responsibilities to:

  ```csharp
  public Task<HardwarePageWorkerCallResult> ReadHardwarePageCandidatesAsync(
      string? projectPath,
      string? deviceName,
      string? plcName,
      bool includeIoDetails,
      bool includeTagMatches,
      int pageSize,
      HardwarePageContinuationInfo? continuation,
      ProjectBindingSnapshot? requiredHostBinding,
      WorkerSessionIdentity? expectedSessionIdentity);
  ```

  The method must enter the same binding serialization/lease mechanism used by bound project requests, capture and validate the host snapshot there, construct `WorkerRequest`, await the worker, and return both `WorkerCallResult` and the in-lease snapshot. Do not perform an authoritative binding check only before entering the serialized section.

- [ ] **Step 4: Enforce the sole identity authority**

  Require a complete observed `WorkerCallResult.SessionIdentity` on success. On a continuation, compare it with the expected cursor identity after the response. Do not accept an identity value from the payload even if a malformed worker supplies one.

- [ ] **Step 5: Run focused tests and confirm GREEN**

  Run the Step 2 command, plus existing binding tests:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~FakeWorkerIdentityEnforcementTests"
  ```

- [ ] **Step 6: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/Worker TiaMcpServer.Contracts/ProjectSessionBinding.cs TiaMcpServer.Tests/Worker TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "feat(worker): pin hardware page continuations"
  ```

---

## Task 6: Assemble Canonical Pages on the Host

**Files:**

- Create: `TiaMcpServer/Network/HardwarePagePayloadContract.cs`
- Create: `TiaMcpServer/Network/HardwarePageProjector.cs`
- Create: `TiaMcpServer/Network/HardwarePaginationCoordinator.cs`
- Create: `TiaMcpServer/Network/NetworkReadOperationExecutor.cs`
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs`
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer/Program.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Network/HardwarePagePayloadContractTests.cs`
- Create: `TiaMcpServer.Tests/Network/HardwarePageProjectorTests.cs`
- Create: `TiaMcpServer.Tests/Network/HardwarePaginationCoordinatorTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatches/StructuredOperationBatchPayloadBudgetTests.cs`

- [ ] **Step 1: Write failing strict-payload tests**

  Require `HardwarePagePayloadContract` to reject with `protocol_error`:

  - invalid JSON or a payload of the wrong root type;
  - missing required fields or candidate arrays;
  - duplicate/out-of-order/noncontiguous candidate offsets;
  - a `StartOffset` different from the requested continuation offset;
  - negative counts, returned counts larger than totals, or totals inconsistent with candidates;
  - the wrong ordering/query/snapshot evidence for the request;
  - a candidate whose kind/payload array does not match;
  - a payload that attempts to add session identity.

  Assert that rejected worker payload text is not echoed into the public error document.

- [ ] **Step 2: Write failing projector budget tests**

  Cover exact boundaries using `CanonicalJson.Serialize`:

  - all candidates fit under 60,000 characters;
  - trailing complete candidates are trimmed until the item fits;
  - offset advances by the number actually returned, not the number materialized;
  - `nextCursor` is null exactly at the end of the combined descriptor set;
  - candidate messages appear only with returned candidates;
  - page messages remain first and stable;
  - page messages alone over budget produce `hardwarePageDiagnosticsExceededItemCharLimit`, no subject, and no advance;
  - the first required device over budget produces an omitted item with `{kind:"device", name, identifier:null}`;
  - the first required subnet over budget produces `{kind:"subnet", name, identifier:subnetId}`;
  - omission JSON never contains a locator, query hash, snapshot hash, or session identity;
  - the existing 180,000-character batch limiter still runs after page projection.

- [ ] **Step 3: Write failing coordinator/routing tests**

  Prove that:

  - no pagination fields uses exactly the existing `NetworkWorkerInvoker` plus `NetworkPayloadContract.Project` path;
  - either pagination field selects the coordinator and internal worker method;
  - cursor validation failures happen before a worker call;
  - cursor-only uses effective page size 50;
  - successful first page records resolved path, in-lease host snapshot, observed identity, query hash, snapshot hash, ordering version, and next offset in the cursor;
  - a successful continuation injects cursor path/identity and preserves the host's bound/unbound state;
  - query, binding, snapshot, offset, and protocol failures retain the approved categories.

- [ ] **Step 4: Run the new tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePagePayloadContractTests|FullyQualifiedName~HardwarePageProjectorTests|FullyQualifiedName~HardwarePaginationCoordinatorTests|FullyQualifiedName~StructuredOperationBatchPayloadBudgetTests"
  ```

- [ ] **Step 5: Add a direct-item read-engine overload**

  Add an overload equivalent to:

  ```csharp
  public Task<StructuredOperationBatchResponse> ExecuteReadsAsync<T>(
      IReadOnlyList<T> operations,
      Func<T, Task<StructuredOperationItem>> execute);
  ```

  Reuse the current order preservation, per-operation failure isolation, canonical response construction, and 180,000-character budget logic. Do not encode host omissions as fake worker payloads.

- [ ] **Step 6: Implement strict decoding and canonical projection**

  `HardwarePagePayloadContract` produces a validated typed candidate result. `HardwarePageProjector` repeatedly builds the prospective public `HardwareConfigInfo` and measures the whole `StructuredOperationItem` using the same canonical serializer used for the final text and `structuredContent`.

  The projector must create a next cursor only after it knows the actual returned prefix. When no candidate fits, emit the approved omission and leave the incoming offset unchanged. The omission's retry guidance must not claim that changing query flags can reuse the cursor.

- [ ] **Step 7: Implement coordinator and route selection**

  `NetworkReadOperationExecutor` owns the one branch:

  ```csharp
  if (operation.Name == "read_hardware_config"
      && (operation.PageSize is not null || operation.Cursor is not null))
  {
      return await paginationCoordinator.ExecuteAsync(operation);
  }

  var worker = await NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation);
  return NetworkPayloadContract.Project(operation, worker);
  ```

  `NetworkReadTools` passes this executor to the new direct-item engine overload. Register one process-scoped cursor codec and the coordinator/executor dependencies as host singletons in `TiaMcpServer/Program.cs`.

- [ ] **Step 8: Run focused and regression tests**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePagePayloadContractTests|FullyQualifiedName~HardwarePageProjectorTests|FullyQualifiedName~HardwarePaginationCoordinatorTests|FullyQualifiedName~StructuredOperationBatch|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkStructuredProtocolTests"
  ```

  Then run focused regressions that protect the lightweight hardware snapshot used by Network write safety and the existing access-mode/public-tool surface:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~NetworkIntrospectionSafetySnapshotTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~NetworkOperationCatalogTests"
  ```

- [ ] **Step 9: Review the seam**

  Confirm there is one canonical serialization per public document, the old unpaged invoker remains selected without pagination fields, the cursor key is not exposed through DI/logging, and `read_hardware_page_candidates` is still internal.

- [ ] **Step 10: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/Network TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs TiaMcpServer/Program.cs TiaMcpServer.Tests/Network TiaMcpServer.Tests/OperationBatches TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "feat(network): assemble budgeted hardware pages"
  ```

---

## Task 7: Prove End-to-End Reconstruction with FakeWorker

**Files:**

- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/Network/HardwarePaginationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

- [ ] **Step 1: Add failing FakeWorker integration tests**

  Add one deterministic fixture with duplicate device names, nested-group locators, subnets, page-level diagnostics, per-candidate diagnostics, and at least one candidate large enough to force canonical trimming. Test:

  - pages reconstruct all matching devices and subnets exactly once, in the stable public order;
  - totals remain constant and returned counts match each page;
  - continuation can omit `projectPath` after an explicit-project first page while the host remains unbound;
  - a smaller/larger valid page size can be used on continuation;
  - a changed filter/detail flag fails before FakeWorker observes another request;
  - changing the FakeWorker descriptor snapshot produces `cursor_snapshot_mismatch`;
  - a stale/out-of-range cursor produces `cursor_out_of_range`;
  - changing host binding or response identity produces `cursor_binding_mismatch`;
  - missing identity, malformed candidate offsets, incoherent totals, and wrong payload type produce `protocol_error` without payload echo;
  - a restart/new process cursor key produces `invalid_cursor`;
  - unpaged FakeWorker results remain byte-for-byte equivalent at the canonical document level.

- [ ] **Step 2: Run the integration tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationFakeWorkerTests"
  ```

- [ ] **Step 3: Add the deterministic FakeWorker scenario**

  Parse `HardwarePageSize`, `HardwarePageContinuation`, filters, detail flags, and `ExpectedSessionIdentity`. Return the same typed candidate DTO as the real worker and the existing envelope identity. Add explicit scenario switches for snapshot drift, missing/mismatched identity, malformed offsets, incoherent counts, and wrong payload shape. Keep all existing FakeWorker scenarios unchanged.

- [ ] **Step 4: Run pagination and existing Network FakeWorker suites**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationFakeWorkerTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkIoMapFakeWorkerTests|FullyQualifiedName~NetworkSubnetLifecycleFakeWorkerTests"
  ```

- [ ] **Step 5: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Network/HardwarePaginationFakeWorkerTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "test(network): verify hardware page reconstruction"
  ```

---

## Task 8: Document, Add the Read-Only Live Harness, and Verify PR2

**Files:**

- Create: `scripts/live-test-hardware-pagination.ps1`
- Create: `TiaMcpServer.Tests/Network/HardwarePaginationLiveHarnessContractTests.cs`
- Modify: `README.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/guides/troubleshooting.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

- [ ] **Step 1: Write failing live-harness contract tests**

  Require the script to:

  - be read-only and call only `network_read/read_hardware_config`;
  - accept project path, filters/detail flags, and page size;
  - follow `nextCursor` until null without changing cursor-bound query fields;
  - assert every canonical operation item is at most 60,000 characters;
  - assert totals remain stable and offsets reconstruct devices/subnets exactly once;
  - record page timing separately from correctness evidence;
  - stop with a clear artifact when a cursor category or omission occurs;
  - require an explicit invocation and never run from ordinary tests or CI.

- [ ] **Step 2: Run the harness contract test and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationLiveHarnessContractTests"
  ```

- [ ] **Step 3: Implement the read-only PowerShell harness**

  Follow the existing Network live-harness process startup, NDJSON request, artifact-directory, and transcript conventions. Do not add write/apply modes. Make the output state clearly that success proves only the exact live project/filter/detail combination tested.

- [ ] **Step 4: Update current documentation**

  Document:

  - `pageSize` range `1..200`, cursor-only default 50, combined device/subnet count, nullable `nextCursor`, and unchanged unpaged behavior;
  - process-local cursor invalidation after host restart;
  - query/path/binding/snapshot/offset failure meanings and correct recovery;
  - the 60,000-character item limit, trimming behavior, omission reasons/subjects, and unchanged 180,000-character batch limit;
  - the worker/host seam and sole session-identity authority in `docs/ARCHITECTURE.md`;
  - no live claim until the separately authorized harness has run.

  Keep `README.md` concise and use absolute GitHub URLs for every cross-document link. Use relative links everywhere under `docs/`.

- [ ] **Step 5: Keep historical indexes reachable**

  Ensure this plan appears in both `docs/README.md` and `docs/superpowers/README.md`. The design spec should remain the approved architecture record; do not rewrite it into a second current-user guide.

- [ ] **Step 6: Run focused documentation/harness tests**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~HardwarePaginationLiveHarnessContractTests|FullyQualifiedName~NetworkLiveHarnessContractTests"
  ```

- [ ] **Step 7: Run full offline verification**

  Run serially:

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
  git diff --check
  git status --short
  ```

  Record exact build/test totals. Inspect the complete diff for accidental public-tool exposure, duplicated identity, locator leakage, cursor material in errors/logs, unpaged output drift, and unrelated changes.

- [ ] **Step 8: Keep live acceptance separate**

  Do not run `scripts/live-test-hardware-pagination.ps1` without separate explicit authorization and a suitable live TIA Portal V21 project. If authorized later, retain its artifact path and report live correctness and timing separately from offline/stub/FakeWorker evidence.

- [ ] **Step 9: Stop at the final commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add scripts/live-test-hardware-pagination.ps1 TiaMcpServer.Tests/Network/HardwarePaginationLiveHarnessContractTests.cs README.md docs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "docs(network): document hardware pagination"
  ```

---

## Completion Gate

PR2 is implementation-complete only when all of the following are true:

- [ ] Every task's focused RED and GREEN evidence is recorded.
- [ ] The full stub solution build passes.
- [ ] The full offline/stub test suite passes with exact totals recorded.
- [ ] Unpaged `read_hardware_config` contract tests prove no regression.
- [ ] FakeWorker pages reconstruct every matching device/subnet exactly once.
- [ ] Every approved cursor and protocol failure category has a regression test.
- [ ] The public canonical item never exceeds 60,000 characters and the outer batch still observes 180,000 characters.
- [ ] The internal worker method is not publicly advertised or catalogued.
- [ ] No internal structural locator, cursor state, HMAC key, or rejected payload is exposed.
- [ ] `git diff --check` passes and the final status contains only intended PR2 changes.
- [ ] Live TIA validation is reported as unverified unless separately authorized and actually run.
- [ ] Any commits were made only after explicit commit authorization.
