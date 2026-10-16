# Codebase review 2026-10-17 — verified findings appendix

Generated from workflow wf_792f4a32-514 (8 reviewers + 8 adversarial verifiers, static review at main f0c79f8). Refuted findings are listed but struck. This appendix is the raw review evidence. Where it disagrees with the [plan](2026-10-17-codebase-simplification-review.md), the plan's decisions win. For example, the TOOLS-1 `plc_read` projectPath example is superseded by decision Q6: content tools take no `projectPath`.

## TOOLS

The 19 tools mostly share the right pieces: the guarded write pipeline, StructuredOperationBatch, CanonicalJson and the payload budget. The main problem is copy-paste between sibling tools that has started to drift. Strictness for root-level arguments differs a lot: 12 of the 19 tools neither close their schema nor reject unknown or legacy root arguments at runtime. The batch-shape and same-project rules are written six times, and vci_read differs from the others. Read tools, read envelopes, error records and validation-result shapes are duplicated in four or more variants. The network_read wiring and VCI attempted-item tracking differ from their siblings. Most fixes are small to medium consolidations into helpers that already exist (StrictArgumentsTool, WriteValidation, StructuredOperationFailure, WriteProjectPaths, WriteCall). The schema and rejection changes alter the contract and need test updates.

### TOOLS-1 — Root-argument strictness differs across the 19 tools; 12 accept unknown or legacy root keys silently

- **Verdict:** confirmed · **Severity:** high · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Tools/McpToolRegistration.cs:17 WithAccessModeTools (WithTools<NetworkReadTools>/<PlcReadTools>/<HmiReadTools>/<CrossReferenceReadTools>)`
  - `TiaMcpServer/Tools/McpToolRegistration.cs:19 WithTools<ProjectEngineeringTools> (compile_check)`
  - `TiaMcpServer/Tools/McpToolRegistration.cs:23 WithTools<ProjectWriteTools> (six lifecycle tools)`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:12-95 (vci_read, vci_write, hmi_write, plc_write: closed schema + runtime rejection)`
  - `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs:30-47 (network_write)`
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:163-212 (bind_project)`
  - `TiaMcpServer/Tools/ProjectReadTools.cs:57-93 (get_project_status: closed schema only, no runtime check; browse_project_tree: wrapper)`
- **Evidence:** A DisallowAdditionalProperties search finds it only in the StrictArgumentsTool registrations, NetworkWriteArgumentValidatingTool, ProjectBindingTools and ProjectReadTools. plc_read, network_read, hmi_read, read_cross_references, compile_check and open/create/save/save_as/archive/close_project use plain WithTools<> with no closed schema and no wrapper. Tests check runtime rejection of a legacy `confirm` only for the content writes (PlcGuardedWriteMcpTests.cs:18, NetworkGuardedWriteMcpTests.cs:49, HmiGuardedWriteMcpTests.cs:79, VciGuardedWriteMcpTests.cs:58). Lifecycle tests only check that `confirm` is absent from the schema (McpToolSchemaTests.cs:228/239, ProjectLifecycleToolTests.cs:41). Example: plc_read({operations:[...], projectPath:'C:\\other.ap21'}) silently drops the root projectPath and reads the current project. save_project({confirm:true}) or plc_read({dryRun:true}) is accepted silently, while network_write rejects the same legacy key.
- **Proposal:** Register every tool through one helper that builds a closed-schema McpServerTool and wraps it in StrictArgumentsTool. Derive allowedKeys and booleanKeys from the inner tool's ProtocolTool.InputSchema properties instead of hand-written sets, so tools cannot drift. Replace the WithTools<> calls at McpToolRegistration.cs:17/19/23 with it.
- **Risk:** Contract change: callers that send stray root keys now get an error. Schema snapshots change (additionalProperties:false appears on 12 tools). Update McpToolSchemaTests and the protocol tests, and run a client smoke test in each mode. Lifecycle elicitation is not affected.
- **Verifier notes:** McpToolRegistration.cs:17,19,23 registers plc_read, network_read, hmi_read, read_cross_references, compile_check and the six lifecycle tools with plain WithTools<>. DisallowAdditionalProperties appears only in StrictArgumentsTool.cs:23/42/61/90, NetworkWriteArgumentValidatingTool.cs:22, ProjectBindingTools.cs:97 and ProjectReadTools.cs:81 (get_project_status gets a closed schema there but no runtime check). No other DelegatingMcpServerTool or Params.Arguments check exists in TiaMcpServer, so the SDK binder silently drops unknown root keys on those 12 tools. McpToolSchemaTests.cs:128-139/226-239 checks only schema property names. AGENTS.md documents unknown-root rejection only for network_write. One caveat on the proposal: for the structured read tools, a plain-text StrictArgumentsTool rejection departs from the target contract (json-contract.md:104-115, where error is non-null when isError is true) and from the conformance rejection probes. The read wrappers should return the tool's canonical error envelope instead. bind_project and browse_project_tree keep their own validators.

### TOOLS-2 — Five argument-validator wrappers and copy-pasted registration blocks; rejection shape differs (plain text vs canonical envelope)

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs:10-47 NetworkWriteToolRegistration/NetworkWriteArgumentValidatingTool`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:12-95 VciRead/VciWrite/HmiWrite/PlcWriteToolRegistration`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:117-152 StrictArgumentsTool`
  - `TiaMcpServer/Tools/ProjectReadTools.cs:129-230 ProjectTreeArgumentValidatingTool`
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:193-212 BindProjectArgumentValidatingTool`
- **Corrected locations:**
  - `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs:10-47`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:12-152`
  - `TiaMcpServer/Tools/ProjectReadTools.cs:57-93,129-230`
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:86-134`
  - `docs/roadmap/json-contract.md:296`
- **Evidence:** NetworkWriteArgumentValidatingTool.InvokeAsync (lines 36-37) does exactly what StrictArgumentsTool("network_write", {operations,dryRun}, {dryRun}, {}) does, but with its own message text. The block `McpServerTool.Create(... SchemaCreateOptions{TransformOptions{DisallowAdditionalProperties=true}})` is pasted 6 times. StrictArgumentsTool and the network wrapper return isError:true with a text block and no structuredContent. ProjectTreeArgumentValidatingTool.ValidationFailure and BindProjectArgumentValidatingTool return the canonical envelope with error/failure {category,message}. docs/roadmap/json-contract.md:104-115 says `error` is non-null exactly when isError is true. (AGENTS.md calls the network pre-entry rejection a 'normal MCP error', so the plain-text shape is deliberate for writes, but the two styles are not documented as a rule.)
- **Proposal:** Delete NetworkWriteArgumentValidatingTool and register network_write through StrictArgumentsTool. Extract one `CreateClosedTool(Type, string method, IServiceProvider, Func<…>? transformSchemaNode)` helper that all 7 registrations use. Decide on one rejection shape and record it in AGENTS.md. Preferably the canonical envelope with validation_error and no audit, so structured clients always get structuredContent; otherwise document plain text as the rule for pre-entry rejection.
- **Verifier-corrected proposal:** Delete NetworkWriteArgumentValidatingTool and register network_write through StrictArgumentsTool (allowed {operations,dryRun}, boolean {dryRun}). Extract one CreateClosedTool(MethodInfo, IServiceProvider, Func<AIJsonSchemaCreateContext,JsonNode,JsonNode>? transform) helper for the 7 registrations. Keep plain-text pre-entry rejection for write tools as documented (json-contract.md:296). Only record in AGENTS.md that pre-entry rejection of writes is plain text, while bind_project and browse_project_tree return canonical envelopes. Effort S. Small risk: network_write rejection text changes, and no test pins it.
- **Risk:** The network_write rejection message text changes (NetworkGuardedWriteMcpTests may assert it). Moving to the canonical envelope adds structuredContent, which changes the contract. Choosing plain text everywhere would change the bind_project and browse_project_tree tests instead.
- **Verifier notes:** The duplication is real. NetworkWriteArgumentValidatingTool.cs:36-37 is functionally StrictArgumentsTool("network_write", {operations,dryRun}, {dryRun}, {}), and no test asserts either message text (search for 'accepts only' finds only the two source files). The closed-schema McpServerTool.Create block appears 7 times, not 6: StrictArgumentsTool.cs x4, NetworkWriteArgumentValidatingTool.cs:15-24, ProjectBindingTools.cs:90-99, ProjectReadTools.cs:70-84. The bind_project locations are wrong: the registration is at ProjectBindingTools.cs:86-111 and the validator at :114-134. The rejection-shape split is documented and deliberate for writes, not drift: json-contract.md:296 says pre-entry legacy/unknown-root/nonboolean rejection is 'a normal MCP error with no audit', AGENTS.md says the same, and README.md:116 says SDK argument rejection has no write audit. Switching writes to the canonical envelope would be a contract change with no defect behind it.

### TOOLS-3 — Batch-shape and single-project rules are implemented six times with drift; plc_read, network_read and hmi_read do not enforce one project but vci_read does

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:15-16 MaxBatchSize/MaxOperationIdLength, :144-195 Validate/ValidateItem, :365-373 same-project`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:33-34, :176-203, :307-314`
  - `TiaMcpServer/Hmi/HmiOperationCatalog.cs:13-14, :138-158, :196-198`
  - `TiaMcpServer/Hmi/HmiWriteCatalog.cs:59, :67-68, :129-135 (literals 50/256)`
  - `TiaMcpServer/Vci/VciReadCatalog.cs:19, :27-28, :59-61 (literals; ProjectPath!.Trim())`
  - `TiaMcpServer/Vci/VciWriteCatalog.cs:26, :35-36, :69-70`
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:172-193 ValidateInput (repeats empty/blank/duplicate-id checks for every write)`
- **Corrected locations:**
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:15-16,146-154,365-373`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:33-34,167-203,307-314`
  - `TiaMcpServer/Hmi/HmiOperationCatalog.cs:13-14,138-158`
  - `TiaMcpServer/Hmi/HmiWriteCatalog.cs:59,67,129-135`
  - `TiaMcpServer/Vci/VciReadCatalog.cs:19,27,59-61`
  - `TiaMcpServer/Vci/VciWriteCatalog.cs:26,35,68-70`
- **Evidence:** PLC, Network and HMI each declare `public const int MaxBatchSize = 50; MaxOperationIdLength = 256;`. HmiWriteCatalog, VciReadCatalog and VciWriteCatalog hardcode `> 50` and `> 256`. The messages differ: 'Batch exceeds the maximum of 50 operations (received N).' vs 'Batch must contain 1 to 50 operations.', and 'Duplicate operationId' vs 'operationId must be unique, nonblank and at most 256 characters.'. The same-project check is written 5 times. Four use WriteProjectPaths.Normalize; VciReadCatalog.cs:60 uses `.Trim()` only, so a relative path and its absolute form count as different projects there. vci_read rejects mixed projectPath values, but plc_read, network_read and hmi_read have no such check (PlcOperationCatalog runs ValidateWriteCall only when write:true, line 163).
- **Proposal:** Add `OperationBatchRules` in TiaMcpServer/OperationBatches with MaxItems=50, MaxOperationIdChars=256, `ValidateShape(IReadOnlyList<IOperationBatchItem?>, List<string> errors)` and `ValidateSingleProject(...)` (using WriteProjectPaths.Normalize). Call them from all six catalogs and drop the duplicate checks in WriteRun.ValidateInput, or have WriteRun call the shared helper and remove them from the write catalogs. Decide explicitly whether multi-project read batches are allowed, and apply that decision to all four read tools.
- **Verifier-corrected proposal:** Add one OperationBatchRules (MaxItems=50, MaxOperationIdChars=256, ValidateSingleProject using WriteProjectPaths.Normalize) in OperationBatches and use it from the PLC/Network/HMI/HmiWrite/Vci catalogs in place of the literals and the 4 copied same-project blocks. Keep WriteRun.ValidateInput as the pipeline's defensive guard. Fix VciReadCatalog to use WriteProjectPaths.Normalize. Either drop vci_read's batch-level mixed-path rejection, matching the documented per-item read assertion, or document it. Do not add new single-project enforcement to plc/network/hmi reads. Effort S-M. Catalog message tests change.
- **Risk:** Error message text changes (catalog tests assert messages). Enforcing single-project on plc/network/hmi reads changes behavior for callers that mix paths today. Low runtime risk with no Siemens calls involved, so no live verification is needed.
- **Verifier notes:** Confirmed facts. PlcOperationCatalog.cs:146-154 and the HMI/Network catalogs use MaxBatchSize constants. HmiWriteCatalog.cs:59/67, VciReadCatalog.cs:19/27 and VciWriteCatalog.cs:26/35 hardcode 50/256, with different messages. The same-project checks are at PlcOperationCatalog.cs:365-373, HmiWriteCatalog.cs:129-135, VciWriteCatalog.cs:68-70 and VciReadCatalog.cs:59-61; the vci_read one uses ProjectPath!.Trim() instead of WriteProjectPaths.Normalize. PLC runs the single-project check only when write is true (:163-166). Documented read semantics are per-item assertions where 'read items run independently' (VCI_OPERATIONS_SUMMARY.md:9), so vci_read's whole-batch mixed-path rejection is the outlier, not plc/network/hmi_read. The proposal goes too far in one place. WriteRun.ValidateInput (WriteRun.cs:172-193) is the shared pipeline entry guard and also runs for LifecycleWriteDomain (ProjectWriteTools.cs:104), so it is not a duplicate that can be dropped.

### TOOLS-4 — Four different validation-result shapes; three read tools discard the failure category

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:20-24 NetworkValidationResult(bool IsValid, string Error)`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:116-120 (bool IsValid, string Error)`
  - `TiaMcpServer/Hmi/HmiOperationCatalog.cs:138 (bool IsValid, string Error)`
  - `TiaMcpServer/Vci/VciReadCatalog.cs:17 (bool IsValid, string Error, string Category)`
  - `TiaMcpServer/Plc/PlcReadTools.cs:32, Network/NetworkReadTools.cs:38, Hmi/HmiReadTools.cs:33 (hardcode ValidationError)`
  - `TiaMcpServer/Vci/VciReadTools.cs:32 (passes validation.Category)`
- **Corrected locations:**
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:20-24`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:116-120`
  - `TiaMcpServer/Hmi/HmiOperationCatalog.cs:138`
  - `TiaMcpServer/Vci/VciReadCatalog.cs:17`
  - `TiaMcpServer/Vci/VciWriteCatalog.cs:24`
  - `TiaMcpServer/Safety/Pipeline/WriteRecords.cs:22-26`
- **Evidence:** vci_read reports invalid_selector for a malformed scope (VciReadCatalog.cs:53-56, scopeCategory). plc_read, network_read and hmi_read always report validation_error because their result type has no category. The write pipeline already has WriteValidation(Invalid(category, message)), which the domains build from these tuples (PlcWriteDomain.cs:32, NetworkWriteDomain.cs:29, HmiWriteDomain.cs:32, VciWriteDomain.cs:25).
- **Proposal:** Use one result type everywhere. Reuse Safety/Pipeline WriteValidation (or move it to OperationBatches as OperationValidation) for the read and write catalogs. Delete NetworkValidationResult and the tuples. Read tools pass the category through as VciReadTools does.
- **Verifier-corrected proposal:** Optional low-value cleanup. Replace NetworkValidationResult and the PLC/HMI/VCI tuples with one OperationValidation(bool IsValid, string Category, string Message) record in OperationBatches, and let WriteValidation wrap it or be built from it. No wire change. Do it only together with TOOLS-3/TOOLS-5. Effort S.
- **Risk:** Signature changes ripple into catalog tests. No wire change except where a catalog chooses to report a more specific category.
- **Verifier notes:** The shape divergence is real. Validation results come in NetworkValidationResult (NetworkOperationCatalog.cs:20-24), a (bool,string) tuple in PLC (PlcOperationCatalog.cs:116-120) and HMI (HmiOperationCatalog.cs:138), a (bool,string,string) tuple in VCI (VciReadCatalog.cs:17, VciWriteCatalog.cs:24), and WriteValidation (Safety/Pipeline/WriteRecords.cs:22-26). The claim that the read tools 'discard the failure category' is wrong. The PLC/Network/HMI catalogs never compute a category to discard. HMI cursor failures (invalid_cursor) are raised per item in HmiPageCursorCodec.cs:56/78 through the invoker, not in the catalog. So no wire category is lost today, and this is a cosmetic unification.

### TOOLS-5 — The four batch read tools are one skeleton pasted four times, with identical response and error records per domain

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcReadTools.cs:25-86`
  - `TiaMcpServer/Network/NetworkReadTools.cs:31-62, :88-102, :130-134`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:25-81`
  - `TiaMcpServer/Vci/VciReadTools.cs:25-77`
  - `TiaMcpServer/Plc/PlcToolResponses.cs:7,23 PlcToolError/PlcReadResponse`
  - `TiaMcpServer/Network/NetworkToolResponses.cs:8,22`
  - `TiaMcpServer/Hmi/HmiToolResponses.cs:6,12`
  - `TiaMcpServer/Vci/VciToolResponses.cs:5,7`
  - `TiaMcpServer/Safety/Pipeline/WriteRecords.cs:6 WriteToolError`
  - `TiaMcpServer/ProjectTree/ProjectTreeToolResponses.cs BrowseProjectTreeFailure`
- **Evidence:** Each read tool has the same steps: ValidateRead → ValidateAccessMode(AccessPolicy?.Mode ?? ReadWrite) → StructuredOperationBatchExecutionEngine.ExecuteReadsAsync → ApplyBudget (same 6-argument call) → Compose(ToolName, XContractVersion.Current, IsFullySuccessful, null, [], batch) → Error(category, message). The four XReadResponse records have identical members (Tool, ContractVersion, Success, Error, Warnings, Batch). PlcToolError, NetworkToolError, HmiToolError, VciToolError, WriteToolError, StructuredOperationFailure and BrowseProjectTreeFailure are all `(string Category, string Message)`.
- **Proposal:** Add `BatchReadTool.RunAsync<TItem>(toolName, contractVersion, validate, accessCheck, invoke, project, retryGuidance)` in OperationBatches. Each tool then keeps only its description, invoke lambda and RetryGuidance. Replace the four XReadResponse records with one `OperationBatchReadResponse`, and replace the domain error records with StructuredOperationFailure (WriteToolError can alias it).
- **Risk:** Output-schema $defs/type names change, so ToolOutputContractConformanceTests and the schema snapshot tests need updating. Wire JSON members stay the same. No live verification needed beyond one read per domain.
- **Verifier notes:** PlcReadTools.cs:25-86 and VciReadTools.cs:25-78 are line-for-line the same skeleton (validate → AccessPolicy?.Mode → ExecuteReadsAsync(invoke, XPayloadContract.Project) → ApplyBudget → Compose → Error). NetworkReadTools.cs:31-62/88-134 differs only in using the executor-based ExecuteReadsAsync overload. PlcReadResponse, NetworkReadResponse, HmiReadResponse and VciReadResponse have identical members (Tool, ContractVersion, Success, Error, Warnings, Batch), and Plc/Network/Hmi/VciToolError are all (Category, Message). The helper must accept either an invoke+project pair or an item executor, because Network goes through StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(ops, execute) at :17-21.

### TOOLS-6 — Dead null-fallback on the non-nullable AccessPolicy reads as fail-open to ReadWrite

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:64,82,87 (_accessPolicy non-null, defaulted in ctor)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2338 `if (_accessPolicy is not null)``
  - `TiaMcpServer/Plc/PlcReadTools.cs:35`
  - `TiaMcpServer/Network/NetworkReadTools.cs:41`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:36`
  - `TiaMcpServer/Vci/VciReadTools.cs:35`
  - `TiaMcpServer/Safety/Pipeline/IWriteBindingGate.cs:57`
- **Evidence:** `public Safety.OperationAccessPolicy AccessPolicy => _accessPolicy;` is non-nullable and always assigned (constructor line 82). Five call sites still write `workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite`. compile_check (ProjectEngineeringTools.cs:22) and LifecycleBindingStrategy.cs:16 call `AccessPolicy.Authorize` directly. The ReadWrite fallback is unreachable, but it suggests that a missing policy grants writes.
- **Proposal:** Replace these with `workerClient.AccessPolicy.Mode` and delete the `is not null` guard at OpennessWorkerClient.cs:2338. The shared read helper from TOOLS-5 would leave only one call site.
- **Risk:** None. Nullable warnings may show up in tests that construct the client with a null policy (the ctor already defaults it).
- **Verifier notes:** OpennessWorkerClient.cs:64 declares a readonly non-nullable _accessPolicy. Line 82 assigns `accessPolicy ?? new OperationAccessPolicy(McpAccessMode.ReadWrite)`, and line 87 exposes it as non-nullable. The `?.Mode ?? ReadWrite` fallbacks at PlcReadTools.cs:35, NetworkReadTools.cs:41, HmiReadTools.cs:36, VciReadTools.cs:35 and IWriteBindingGate.cs:57, and the `is not null` check at OpennessWorkerClient.cs:2338, are unreachable. Line 1913 already uses _accessPolicy directly. No GetUninitializedObject use exists in tests. One note: the actual ReadWrite default for a null policy is the ctor at :82. The dead fallbacks only repeat it.

### TOOLS-7 — network_read resolves its executor through a static ConditionalWeakTable side registry, while hmi_read and browse_project_tree use DI parameters

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/NetworkReadTools.cs:18-21 CompatibilityExecutors/CompatibilityCursorCodec`
  - `TiaMcpServer/Network/NetworkReadTools.cs:48-50, :64-82 RegisterExecutor/CreateCompatibilityExecutor`
  - `TiaMcpServer/Program.cs:125-127, :135-137`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:27 (HmiPageCursorCodec injected)`
  - `TiaMcpServer/Tools/ProjectReadTools.cs:44 (ProjectTreeBrowseCoordinator injected)`
- **Evidence:** Program.cs registers NetworkReadOperationExecutor in DI and then, after Build(), pushes it into a static ConditionalWeakTable through NetworkReadTools.RegisterExecutor (the only production caller). Any OpennessWorkerClient that was not registered gets a 'compatibility' executor whose HardwarePageCursorCodec has its own process-scoped AuthenticatedCursorProtector. That is a second key, so its cursors cannot be validated by the DI codec. The sibling paged tools take their DI singleton as a tool-method parameter, which the MCP SDK resolves from services.
- **Proposal:** Add a `NetworkReadOperationExecutor operationExecutor` parameter to NetworkRead, as HmiRead does with HmiPageCursorCodec. Delete CompatibilityExecutors, CompatibilityCursorCodec, CreateCompatibilityExecutor, RegisterExecutor and Program.cs:135-137. Tests that call NetworkRead directly then pass an executor built from their fake client.
- **Risk:** Test churn wherever tests call NetworkReadTools.NetworkRead(workerClient, ops) without DI. No wire change. Run one hardware pagination test to confirm cursors still round-trip.
- **Verifier notes:** NetworkReadTools.cs:18-21 holds a static ConditionalWeakTable plus CompatibilityCursorCodec. Lines 48-50 do the GetValue lookup, and :64-82 contain RegisterExecutor/CreateCompatibilityExecutor. RegisterExecutor's only caller is Program.cs:135-137, after Build(), and the DI singleton is registered at Program.cs:125-127. HmiReadTools injects HmiPageCursorCodec, and ProjectReadTools.cs:44 injects ProjectTreeBrowseCoordinator. In production the single client is always registered, so the second-key path is effectively test-only: about 10 tests call NetworkReadTools.NetworkRead(client, ops) directly (NetworkIoMapFakeWorkerTests.cs:59+, HardwarePaginationFakeWorkerTests.cs:233, NetworkConnectionEvidenceTests.cs:194). The proposal is still right, and those tests need an executor argument.

### TOOLS-8 — StructuredStandaloneResult dispatches on tool-name strings and typeof checks; four identical standalone response records

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Tools/StructuredStandaloneResult.cs:27-33 guidance switch, :45-64 Compose switch + throw`
  - `TiaMcpServer/Tools/StandaloneToolResponses.cs:16-54 GetProjectStatusResponse/CompileCheckResponse/ReadCrossReferencesResponse/BindProjectResponse`
- **Corrected locations:**
  - `TiaMcpServer/Tools/StructuredStandaloneResult.cs:27-33,45-64`
  - `TiaMcpServer/Tools/StandaloneToolResponses.cs:16-54`
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:54`
- **Evidence:** Adding a standalone tool means editing two string switches and adding a record. A mismatched tool/payload pair only fails at runtime ('Unknown standalone tool/payload pair.'). The four records differ only in the payload type argument and a hardcoded `Tool` property. bind_project already passes omissionGuidance explicitly (ProjectBindingTools.cs:131-133), so the guidance switch is half-used. StructuredStandaloneResult.Version is one shared "1.0" for four tools, so one tool cannot bump its contract alone.
- **Proposal:** Make Create take the guidance string and a `Func<bool, StructuredOperationFailure?, IReadOnlyList<string>, StandaloneToolOutcome<T>?, object> compose`, or use one generic `StandaloneToolResponse<TPayload>(string Tool, string ContractVersion, ...)`. Callers pass the tool name and version, and both switches go away.
- **Verifier-corrected proposal:** Pass guidance from each caller and delete the guidance switch. Optionally replace the Compose switch with a caller-supplied Func<bool, StructuredOperationFailure?, IReadOnlyList<string>, StandaloneToolOutcome<T>?, object> so a mismatch fails to compile, and keep the four named records, which preserves output-schema type names. Effort S. No schema change.
- **Risk:** Output-schema type names change if the generic record is used, so the schema and conformance tests need updating. Wire members stay the same.
- **Verifier notes:** StructuredStandaloneResult.cs:27-33 (guidance switch) and :48-63 (Compose switch with runtime throw at :62) dispatch on tool strings and typeof checks. The four records in StandaloneToolResponses.cs (16/26/36/46, each with a hardcoded Tool at 23/33/43/53) differ only in payload type. The cited explicit omissionGuidance location is wrong: it is ProjectBindingTools.cs:54, for inspection actions, not :131-133. The 'cannot bump one contract alone' point is speculative (YAGNI). The value is low.

### TOOLS-9 — vci_write decides which items were attempted from a hardcoded failure-category string list; siblings track _attempted in MutateAsync

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:11-12 NeverRan, :17-21, :27`
  - `TiaMcpServer/Vci/VciWriteDomain.cs:55-74 MutateAsync/VerifyAsync (whole batch passed)`
  - `TiaMcpServer/Plc/PlcWriteDomain.cs:20, :71, :79-80`
  - `TiaMcpServer/Hmi/HmiWriteDomain.cs:20, :71, :78-80`
  - `TiaMcpServer/Network/NetworkWriteDomain.cs:22, :91, :99-101`
- **Corrected locations:**
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:11-12,17-21,27-30`
  - `TiaMcpServer/Vci/VciWriteDomain.cs:73-74`
- **Evidence:** PLC, HMI and Network add the operationId to `_attempted` when MutateAsync runs, then verify only `batch.Operations.Where(o => _attempted.Contains(o.OperationId))`. VCI passes the full batch and guesses attempted-ness from `{"validation_error","target_not_found","target_ambiguous","state_changed","guard_blocked","binding_conflict","access_denied"}` plus `"portal_lost" or "worker_timeout" or "worker_crashed"`. These are string literals rather than WorkerFailureCategories constants. If a pre-mutation failure category is added later and not listed here, the item is treated as attempted and gets a verification read.
- **Proposal:** Add `_attempted` to VciWriteDomain.MutateAsync and filter the batch before VciWriteVerifier, as the siblings do. Keep only the fatal-category check, and write it with WorkerFailureCategories constants. Remove the NeverRan set, or reduce it to the categories that need special 'notVerified' wording.
- **Verifier-corrected proposal:** Keep VCI's whole-batch verification and NeverRan semantics. Replace the literals at VciWriteVerifier.cs:11-12 and :17 with WorkerFailureCategories constants, and consider moving the fatal-category set into WorkerFailureCategories next to its siblings. Optionally add a comment explaining why VCI verifies the whole batch. Effort S. No behavior change.
- **Risk:** Verification output for never-run items must still read 'notVerified'/'superseded' as today. Covered by VCI verifier unit tests. No live run needed if the unit tests pin the wording.
- **Verifier notes:** VciWriteVerifier.cs:11-12 (NeverRan) and :17 use string literals, although WorkerFailureCategories constants exist (WorkerFailureCategories.cs:19,22,28,46,92). VciWriteDomain.cs:73-74 passes the whole batch while PLC/HMI/Network filter by _attempted (PlcWriteDomain.cs:20/71/80, HmiWriteDomain.cs:20/71/80, NetworkWriteDomain.cs:22/91/101). The proposal overreaches. NeverRan is not equivalent to _attempted. Items that did reach MutateAsync can still come back from the worker with pre-mutation categories such as state_changed or guard_blocked (VCI does fresh owner checks and an adjacent sync recheck). VCI deliberately reports them, and skipped items, as per-item 'notVerified' (:27-28), and its 'superseded' logic (:29) depends on that filtered attempted list. Swapping to _attempted would send verification reads for those items and drop the per-item notVerified records.

### TOOLS-10 — Write domains and write tool entry points repeat the same boilerplate; PlcWriteDomain and HmiWriteDomain are line-for-line twins

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcWriteTools.cs:21-24`
  - `TiaMcpServer/Network/NetworkWriteTools.cs:21-24`
  - `TiaMcpServer/Hmi/HmiWriteTools.cs:19-21`
  - `TiaMcpServer/Vci/VciWriteTools.cs:19-21`
  - `TiaMcpServer/Plc/PlcWriteDomain.cs:29-38, :40-67`
  - `TiaMcpServer/Hmi/HmiWriteDomain.cs:29-38, :40-67`
  - `TiaMcpServer/Network/NetworkWriteDomain.cs:26-35`
  - `TiaMcpServer/Vci/VciWriteDomain.cs:22-31`
  - `TiaMcpServer/Safety/Pipeline/WriteRecords.cs:15 WriteCall`
- **Evidence:** All four tools build `new WriteCall<T>(operations?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item?.ProjectPath))?.ProjectPath, operations ?? [], dryRun)`, and IOperationBatchItem already exposes ProjectPath. All four Validate methods follow the same pattern: catalog validation → `MeasureProtectedCore(items) > MaxDocumentChars` with the same message except for the tool name → ValidateAccessMode → AccessDenied. The PLC and HMI domains have identical PlanAsync, EvaluateGuards and ReplanAsync bodies (guard cache, DependsOn carry-over).
- **Proposal:** Add a `WriteCall<T>.From(IReadOnlyList<T>? items, bool dryRun)` factory and use it in the four tools. Extract `WriteValidation.Combine(toolName, catalogResult, protectedCoreChars, accessErrors)`. Optionally move the PLC/HMI guard-cache logic into a small shared helper class, but only if a third domain needs it (YAGNI otherwise).
- **Risk:** Pure refactor with no wire change. Existing domain tests cover it.
- **Verifier notes:** WriteCall construction from the first nonblank item ProjectPath is repeated in PlcWriteTools.cs:22, NetworkWriteTools.cs:22, HmiWriteTools.cs:19 and VciWriteTools.cs:19. Lifecycle (ProjectWriteTools.cs:104) differs, so the factory covers the four content writes only. A diff of PlcWriteDomain.cs:20-70 against HmiWriteDomain.cs:20-70 differs only in type names, tool name and catalog names. The Validate blocks (PlcWriteDomain.cs:32-37, HmiWriteDomain.cs:32-37, NetworkWriteDomain.cs:29-34, VciWriteDomain.cs:25-30) differ only in tool name and the category source. Two twin domains already justify a shared helper, so the YAGNI caveat does not apply.

### TOOLS-11 — Paging vocabulary and defaults differ across the four paged tools

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** L · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Tools/ProjectReadTools.cs:48-49 browse_project_tree pageSize 1-200 default 100 + cursor`
  - `TiaMcpServer/Network/NetworkReadTools.cs:30 read_hardware_config pageSize 1..200, cursor alone defaults to 50`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:24 offset + limit 1-2000 default 100 + cursor/nextCursor + nextOffset`
  - `TiaMcpServer/Vci/VciReadTools.cs:24 list_mappings offset + limit 1-200 default 100, no cursor/snapshot`
- **Evidence:** There are three page-size names and ranges (pageSize 1-200, limit 1-2000, limit 1-200), two continuation models (an opaque cursor in tree, network and HMI; plain offset in VCI, plus HMI's mixed offset/cursor), and two defaults (100 vs network's cursor-only 50). The tool descriptions document each variant separately, but no document gives the cross-tool rule.
- **Proposal:** Short term: add a one-table paging matrix to docs/SupportedOperations/README.md and make network's cursor-only default 100 to match. At the next major contract bump, align the names (pageSize/cursor everywhere, with offset only where there is no snapshot).
- **Risk:** Renaming parameters breaks the contract and must go with a contractVersion major bump. The default change from 50 to 100 is a behavior change for cursor-only callers.
- **Verifier notes:** browse_project_tree has pageSize 1-200, default 100, plus a cursor (ProjectReadTools.cs:48-49). The network_read description says 'pageSize (1..200) or cursor; cursor alone defaults to 50' (NetworkReadTools.cs:30). hmi_read uses limit 1-2000, default 100, with offset/cursor (AGENTS.md, README.md:32). vci_read uses limit 1-200, default 100, with offset and no snapshot (VciReadTools.cs:24, VciReadCatalog.cs:49). Each is documented on its own, and no cross-tool rule exists. The renames rightly wait for major contract bumps. The 50→100 default change is a behavior change.

### TOOLS-12 — Read tools ignore MCP cancellation; invoker CancellationToken parameters are effectively dead

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcReadTools.cs:44 CancellationToken.None`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:45 CancellationToken.None`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:9-14 InvokeReadAsync`
  - `TiaMcpServer/Hmi/HmiWorkerInvoker.cs:9-15 InvokeReadAsync`
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:106 (CancellationToken in the middle of the parameter list)`
  - `TiaMcpServer/Plc/PlcWriteTools.cs:20 and siblings (writes accept CancellationToken)`
- **Corrected locations:**
  - `TiaMcpServer/Plc/PlcReadTools.cs:44`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:45`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:9-14`
  - `TiaMcpServer/Hmi/HmiWorkerInvoker.cs:9-15`
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs:17-21,43-46`
- **Evidence:** The write tools and bind_project take the request CancellationToken. plc_read, network_read, hmi_read, vci_read, get_project_status, browse_project_tree, read_cross_references and compile_check do not. The PLC and HMI invokers accept a token that production always passes as CancellationToken.None, and they use it only for ThrowIfCancellationRequested. A cancelled 50-item read batch keeps running every item.
- **Proposal:** Either remove the dead CT parameters from PlcWorkerInvoker and HmiWorkerInvoker, or (preferred, small) add a CancellationToken parameter to the read tools and check it between items in StructuredOperationBatchExecutionEngine.ExecuteReadsAsync. Move bind_project's CT to the end for consistency.
- **Verifier-corrected proposal:** Preferred: add a CancellationToken to the four batch read tools and to ExecuteReadsAsync, check it between items, and pass it to the PLC/HMI invokers. Alternatively, delete the dead token parameters from PlcWorkerInvoker/HmiWorkerInvoker.InvokeReadAsync. Leave bind_project's parameter order alone. Effort S.
- **Risk:** Cancelling between items is safe for reads. Worker calls themselves stay uncancellable, which the worker timeout already covers.
- **Verifier notes:** PlcReadTools.cs:44 and HmiReadTools.cs:45 pass CancellationToken.None. PlcWorkerInvoker.cs:9-14 and HmiWorkerInvoker.cs:9-15 use the token only for ThrowIfCancellationRequested. Outside those call sites the only test use is PlcGuardedWriteFixture.cs:75, with the default token. StructuredOperationBatchExecutionEngine.ExecuteReadsAsync (:17-21, :43-46) takes no token. The bind_project location is wrong: the parameter is at ProjectBindingTools.cs:27, mid-list. Moving it there is churn with no defect, since the MCP binder is name-based and direct test callers depend on positional order.

### TOOLS-13 — Nullable-boolean handling differs: bind_project accepts forceRebind:null, other boolean arguments reject or do not handle null

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:182-190 AllowNullForceRebind, :204-209`
  - `TiaMcpServer/Tools/ProjectWriteTools.cs:168 open_project forceRebind`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:129-130 (dryRun:null rejected)`
  - `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs:36-37`
- **Corrected locations:**
  - `TiaMcpServer/Tools/ProjectBindingTools.cs:103-111,125-130`
  - `TiaMcpServer/Tools/ProjectWriteTools.cs:24`
  - `TiaMcpServer/Tools/StrictArgumentsTool.cs:129-130`
  - `docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md:33`
- **Evidence:** bind_project advertises forceRebind as [boolean,null] and strips a JSON null before binding. The same-named forceRebind on open_project is plain boolean. dryRun:null is rejected by StrictArgumentsTool and the network wrapper. Lifecycle dryRun, saveBeforeClose and saveBeforeArchive null are left to SDK binding behavior.
- **Proposal:** Pick one rule (recommended: JSON null on an optional boolean means omitted) and implement it once in the shared StrictArgumentsTool from TOOLS-1/TOOLS-2. Then delete the bind_project-specific schema transform and argument rewrite.
- **Verifier-corrected proposal:** Leave bind_project's null and key-presence handling as is. It is documented. Separately, decide whether optional lifecycle booleans (dryRun, saveBeforeClose, saveBeforeArchive, forceRebind) accept JSON null, and apply that rule in the strict wrapper added for TOOLS-1. Low priority. Effort S.
- **Risk:** Small contract change for clients that send null today. Schema snapshot tests need updating.
- **Verifier notes:** Factual difference: bind_project marks forceRebind as nullable (ProjectBindingTools.cs:103-111) and strips a JSON null (:125-130), while open_project's forceRebind is a plain bool (ProjectWriteTools.cs:24, not :168), and StrictArgumentsTool.cs:129-130 rejects dryRun:null. bind_project's handling is tied to its documented exact raw-key validation: MULTIUSER_OPERATIONS_SUMMARY.md:33 says inspection actions reject supplied projectPath/forceRebind keys 'even when null/false', and ARCHITECTURE.md:300 covers default/null/explicit bind. Folding it into a generic StrictArgumentsTool rule and deleting the bind_project transform would break that key-presence contract.

### TOOLS-14 — Docs drift: json-contract roadmap and README still describe the 16-tool / PLC+Network-only surface

- **Verdict:** confirmed · **Severity:** low · **Category:** docs-drift · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `docs/roadmap/json-contract.md:35 'All sixteen registered tools'`
  - `docs/roadmap/json-contract.md:40-48 scope table (no hmi_read, hmi_write, vci_read, vci_write)`
  - `docs/roadmap/json-contract.md:64 'Standalone/lifecycle/Network/PLC tools use the target envelope'`
  - `README.md:130 'plc_write and network_write plan and re-plan ... under one pinned verified project binding'`
- **Evidence:** README.md:19 and AGENTS.md give 8/19/19 tools. The roadmap still says sixteen and leaves the four HMI/VCI tools out of its scope table, even though AGENTS.md lists them on the canonical contract. AGENTS.md says planning, mutation, verification and audit run under one pinned lease for all guarded writes, but README.md:130 names only PLC and Network.
- **Proposal:** Update json-contract.md:35 to nineteen, add rows for hmi_read (1.1), hmi_write (1.0), vci_read and vci_write to the scope table, and widen line 64. Change README.md:130 to name all four content write tools, keeping absolute URLs per AGENTS.md.
- **Risk:** Docs only. Per the docs-after-live-verification convention, make these edits in the docs task, not mid-feature.
- **Verifier notes:** json-contract.md:35 says 'All sixteen registered tools', and the scope table at :40-48 has no hmi_read, hmi_write, vci_read or vci_write rows. Line 90 lists 'Lifecycle, Network, PLC and HMI writes' without VCI. README.md:19 and AGENTS.md give 8/19/19. README.md:130 names only plc_write and network_write for pinned-lease planning, while AGENTS.md states the lease rule for all guarded writes.

### ~~TOOLS-15 — browse_project_tree envelope still diverges (status/failure, no tool/success/error)~~ (REFUTED)

- **Verdict:** refuted · **Severity:** low · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/ProjectTree/ProjectTreeToolResponses.cs:53 BrowseProjectTreeResponse(ContractVersion, Status, Result, Failure, Warnings)`
  - `TiaMcpServer/Tools/ProjectReadTools.cs:216-229 ValidationFailure`
  - `docs/roadmap/json-contract.md:43, :144-147`
- **Evidence:** All other 18 tools put tool, contractVersion, success, error and warnings at the root. browse_project_tree uses status (succeeded/failed) and failure, and has no tool member. This is tracked as roadmap Phase 4 'align envelope members', so it is known, not a regression.
- **Proposal:** No new action beyond the roadmap's Phase 4. When it is done, reuse StructuredOperationFailure for the failure record (see TOOLS-5) and bump the project-tree contract major.
- **Risk:** Breaking change for project-tree callers. Needs a major contractVersion bump and a migration note.
- **Verifier notes:** The divergence is documented and deliberately deferred: json-contract.md:43 (scope row 'Phase 4: align envelope members'), :142-151 (deviation table, plus 'For the project tree it is a breaking change, so it waits for the tree's next major contract version'), and :297. The finding itself proposes no new action, so there is nothing new to fix.

## HOSTDOMAINS

The domains all sit on the shared Safety/Pipeline and OperationBatches seams. Around those seams, though, each domain re-implements the same machinery by copy-paste: write payload budgets, batch-envelope and field-spec validators, access-mode filters, read-tool flow, write-pipeline records, PlcWriteDomain/HmiWriteDomain, payload-projection failure scaffolding and cursor binding checks. Several copies have already drifted apart: messages, the 50/256 limits written as literals, project-path normalization, diagnostic emission, and unbound-binding rules. Dead code is small: a few leftovers from the removed generic batch apply, some test-only members and pass-through wrappers, and unused CancellationToken parameters. The two Network files are over 1,000 lines and encode the selector kind/field matrix twice. Method: jCodemunch outline/search/source plus bounded grep/sed reads; find_dead_code exceeded the response limit, so name-reference scans were used instead (overloaded common names may hide some dead members).

### HOSTDOMAINS-1 — Four guarded-write payload budgets duplicate one algorithm; HMI and VCI copies are verbatim

- **Verdict:** confirmed · **Severity:** high · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Hmi/HmiWritePayloadBudget.cs:20 ApplyCore, :60 MeasureProtectedCore, :86 Compact, :116 HasOmission, :119 Size`
  - `TiaMcpServer/Vci/VciWritePayloadBudget.cs:20 ApplyCore, :58 MeasureProtectedCore, :84 Compact, :107 HasOmission, :110 Size`
  - `TiaMcpServer/Plc/PlcWritePayloadBudget.cs:26 ApplyCore, :116 MeasureProtectedCore, :142 Compact, :175 HasOmission, :183 Size`
  - `TiaMcpServer/Network/NetworkWritePayloadBudget.cs:22 ApplyCore, :325 diagnostic omission, :335 Size`
- **Evidence:** Hmi and Vci ApplyCore/Oversized/Compact/HasOmission/Omission/Size are line-for-line identical apart from type names and the tool name in Guidance. The only real difference is that HMI also compacts HmiWriteOutcomeInfo.CleanupMessage. The PLC version is the same skeleton plus diff and outcome omission kinds. All four repeat the Restore-trusted-status wrapper around StructuredOperationBatchPayloadBudget.Apply, the MeasureProtectedCore worst-case document builder (only the guard multiplier differs: 2N+1, 4N+3, 5N+5), the Size<T> helper and the literal "diagnosticDetailsOmitted" (Plc :18/:171, Hmi :114, Vci :105, Network :325), while ItemLimitReason and DocumentLimitReason already live in StructuredOperationBatchPayloadBudget. Drift: Plc/Hmi/Vci Compact stamp Omission.limit with the static StructuredOperationBatchPayloadBudget.MaxDocumentChars, not the maxDocumentChars passed to Apply. A test or caller override therefore reports the wrong limit.
- **Proposal:** Extract one generic GuardedWriteResponseBudget in Safety/Pipeline (or OperationBatches). It should hold: compaction of Error/Warnings/Guards/Batch failures/verification messages; the ordered omission loop over effects and verification checks; trusted-status Restore; HasOmission; and a parameterized MeasureProtectedCore(items, guardsPerItem, trailingGuards). Domain-specific omittable roots (PLC content diff and import outcome, Network evidence and finalChecks, HMI cleanup message) become hooks. Move "diagnosticDetailsOmitted" next to ItemLimitReason/DocumentLimitReason. Pass the effective maxDocumentChars into Compact. Do HMI and VCI first: they collapse to configuration.
- **Risk:** Delivered-document shape and omission ordering are contract-visible and pinned by budget tests in every domain; keep the per-domain priority order exactly. The Omission.limit change only shows up when limits are overridden. No live verification needed if the budget tests stay green.
- **Verifier notes:** Hmi/HmiWritePayloadBudget.cs:20-55 and Vci/VciWritePayloadBudget.cs:20-55 ApplyCore are identical apart from type names and the retry tool. Oversized, Compact, HasOmission, Omission and Size also match; HMI's Compact additionally rewrites HmiWriteOutcomeInfo.CleanupMessage (:98-103). The guard multipliers are as claimed: Plc :122-123 gives 2N+1, Hmi :66-67 gives 4N+3, Vci :64-65 gives 5N+5. The literal "diagnosticDetailsOmitted" appears at Plc :18, Hmi :114, Vci :105 and Network :325. The limit drift is real: Plc :171, Hmi :114 and Vci :105 stamp the static StructuredOperationBatchPayloadBudget.MaxDocumentChars, not the maxDocumentChars passed to ApplyCore. Network :325 does the same in compact mode. This only shows up when a caller or test overrides the limit. NetworkWritePayloadBudget is meaningfully larger (evidence and finalChecks), which the finding already says.

### HOSTDOMAINS-2 — Batch-envelope and field-spec validation copied across six catalogs, with drifting messages and limits

- **Verdict:** confirmed · **Severity:** high · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:15-16 MaxBatchSize/MaxOperationIdLength, :144 Validate, :176 ValidateItem, :364-372 same-project check`
  - `TiaMcpServer/Hmi/HmiOperationCatalog.cs:13-14 constants, :138 ValidateRead, :182 ValidateItem`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:33-34 constants, :167 Validate, :306-315 same-project check`
  - `TiaMcpServer/Hmi/HmiWriteCatalog.cs:57 ValidateWrite (:59 literal 50, :67 literal 256), :129-135 same-project`
  - `TiaMcpServer/Vci/VciWriteCatalog.cs:24 ValidateWrite (:26 literal 50, :35 literal 256), :68-70 same-project`
  - `TiaMcpServer/Vci/VciReadCatalog.cs:17 ValidateRead (:19 literal 50, :27 literal 256), :59-61 same-project using Trim()`
- **Evidence:** A diff of HmiOperationCatalog.ValidateRead/ValidateItem against PlcOperationCatalog.Validate/ValidateItem differs only in the read/write flag and paging fields. HmiWriteCatalog and the Vci catalogs re-implement the same checks (count, null item, operationId unique/nonblank/<=256, unknown operation, required/optional/nonblank fields, same project) in compact form. They use literals although MaxBatchSize and MaxOperationIdLength constants exist three times. Messages differ for the same rule: "Batch must contain 1 to 50 operations." vs "Batch must contain at least one operation." / "Batch exceeds the maximum of 50"; "operationId must be unique, nonblank..." vs "Duplicate operationId '...'". VciReadCatalog compares ProjectPath.Trim() while every other catalog uses WriteProjectPaths.Normalize.
- **Proposal:** Add one OperationBatches helper, e.g. OperationBatchEnvelope.Validate<T>(items) where T : IOperationBatchItem, returning errors for count, nulls and operationId rules. Use a single MaxBatchSize = 50 / MaxOperationIdLength = 256 pair, a SameProject(items, getPath) check built on WriteProjectPaths.Normalize, and a small FieldSpec.Check(spec, fields, item, prefix) for required/optional/nonblank. Have the six catalogs call it and keep only domain-specific rules (selectors, content hashes, scopes). Delete the duplicated constants.
- **Risk:** validation_error message text changes, and catalog tests assert exact strings. Behavior change for VCI reads: relative vs absolute paths to the same project would now compare equal. No live verification needed.
- **Verifier notes:** Vci/VciReadCatalog.cs:19 and Vci/VciWriteCatalog.cs:26 use a literal 50 and :27/:35 a literal 256. Hmi/HmiWriteCatalog.cs:59 and :67 do the same. Meanwhile MaxBatchSize = 50 and MaxOperationIdLength = 256 are declared three times: Plc :15-16, Hmi :13-14, Network :33-34. The message drift is confirmed. Plc :148/:153, Hmi :142/:147 and Network :173/:179 say "Batch must contain at least one operation." / "Batch exceeds the maximum of 50 ..." and "Duplicate operationId" (Plc :193, Hmi :204, Network :209). Hmi write and both Vci catalogs say "Batch must contain 1 to 50 operations." and "operationId must be unique, nonblank...". VciReadCatalog.cs:59-61 compares ProjectPath!.Trim(), while VciWriteCatalog.cs:68-70 and HmiWriteCatalog.cs:129-135 use WriteProjectPaths.Normalize. One note: the return shapes differ, which a shared helper must bridge. They are (bool,string) for Plc/Hmi, (bool,string,string Category) for Vci and NetworkValidationResult for Network.

### HOSTDOMAINS-3 — Read-tool flow, access-mode filter and read envelope records repeated per domain

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcReadTools.cs:29-49 PlcRead, :52 ApplyBudget, :64 Compose`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:30-81 HmiRead/ApplyBudget/Compose/Error`
  - `TiaMcpServer/Vci/VciReadTools.cs:29-77 VciRead/ApplyBudget/Compose/Error`
  - `TiaMcpServer/Network/NetworkReadTools.cs:35-61, :88, :100, :130`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:122 ValidateAccessMode; Hmi/HmiOperationCatalog.cs:160; Hmi/HmiWriteCatalog.cs:155; Network/NetworkOperationCatalog.cs:145; Vci/VciReadCatalog.cs:66; Vci/VciWriteCatalog.cs:75`
  - `TiaMcpServer/Plc/PlcToolResponses.cs:7,23; Hmi/HmiToolResponses.cs:6,12; Vci/VciToolResponses.cs:5,7; Network/NetworkToolResponses.cs:8,22`
  - `TiaMcpServer/Safety/Pipeline/IWriteBindingGate.cs:57 AccessMode; Safety/Pipeline/WriteRecords.cs:6 WriteToolError`
- **Evidence:** Each read tool runs the same sequence: ValidateRead, then `workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite` (repeated in 4 tools plus OpennessWriteBindingGate.AccessMode), then ValidateAccessMode, ExecuteReadsAsync, StructuredOperationBatchPayloadBudget.Apply(Compose, ToolName, RetryGuidance), Compose, and Error(). Six ValidateAccessMode copies produce the same message; VciReadCatalog's version omits the operationId and the null/known-operation guard. PlcToolError, HmiToolError, VciToolError and NetworkToolError are identical (string Category, string Message) records, and WriteToolError has the same shape. The four XxxReadResponse records are identical too.
- **Proposal:** Add OperationBatches/StructuredReadTool.RunAsync<TItem,TResponse>(items, validate, workerMethodOf, execute, compose, retryGuidance), plus one OperationAccess.Denials<T>(items, mode, Func<string,string?> workerMethod) used by all catalogs. Expose the effective mode once, e.g. an OpennessWorkerClient.EffectiveAccessMode property. Replace the four ToolError records with one shared ToolError (or reuse WriteToolError), and the read responses with one StructuredReadResponse record.
- **Risk:** OutputSchemaType per tool: check that replacing per-domain record types does not change the published JSON schema in an observable way, e.g. $defs/title names, via ToolOutputContractConformanceTests. Read semantics are unchanged.
- **Verifier notes:** `workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite` appears at Plc/PlcReadTools.cs:35, Hmi/HmiReadTools.cs:36, Vci/VciReadTools.cs:35, Network/NetworkReadTools.cs:41 and Safety/Pipeline/IWriteBindingGate.cs:57. PlcToolError (Plc/PlcToolResponses.cs:7), HmiToolError (Hmi/HmiToolResponses.cs:6), VciToolError (Vci/VciToolResponses.cs:5) and NetworkToolError (Network/NetworkToolResponses.cs:8) are all `(string Category, string Message)`, the same shape as WriteToolError (Safety/Pipeline/WriteRecords.cs:6). All four GuardedWriteResponse records already use WriteToolError, so only the read side keeps per-domain copies. Plc :23, Hmi :12, Vci :7 and Network :22 define identical six-member ReadResponse records. VciReadCatalog.ValidateAccessMode (:66-68) has no operationId in its message and no null or known-operation guard, unlike VciWriteCatalog :75-78 and HmiWriteCatalog :155-158.

### HOSTDOMAINS-4 — Write-pipeline records and PlcWriteDomain/HmiWriteDomain are per-domain copies of generic shapes

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcWriteDomain.cs:14-89 (whole class)`
  - `TiaMcpServer/Hmi/HmiWriteDomain.cs:14-89 (whole class)`
  - `TiaMcpServer/Vci/VciWriteDomain.cs:22-31 Validate; Network/NetworkWriteDomain.cs:26-35 Validate`
  - `TiaMcpServer/Plc/PlcWriteModels.cs:41 PlcWriteEffectPresentation, :44 PlcGuardedWriteResponse, :52 PlcPlanResult, :54 PlcReplanResult`
  - `TiaMcpServer/Hmi/HmiWriteModels.cs:13-22 (EffectPresentation, GuardedWriteResponse, OperationVerification, WriteVerification, Resolution, PlanResult, ReplanResult)`
  - `TiaMcpServer/Vci/VciWriteModels.cs:9-17 (same set)`
  - `TiaMcpServer/Network/NetworkGuardedWriteModels.cs:41-42`
  - `TiaMcpServer/Plc/PlcWriteTools.cs:21-24; Hmi/HmiWriteTools.cs:19-21; Vci/VciWriteTools.cs:19-21; Network/NetworkWriteTools.cs:21-24 WriteCall construction`
- **Evidence:** PlcWriteDomain and HmiWriteDomain differ only in type names and MutateAsync: same _guards/_effects/_attempted caches, same PlanAsync caching loop, same ReplanAsync DependsOn preservation, same attempted-filtered VerifyAsync, same Compose. All four Validate methods are the same three steps with the same message, differing only in tool name. XxxWriteEffectPresentation (4×), XxxPlanResult/XxxReplanResult (3×) and Hmi/VciOperationVerification plus WriteVerification are structurally identical. XxxGuardedWriteResponse has the same 11 members in all four domains. All four write tools build WriteCall with the same expression `operations?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i?.ProjectPath))?.ProjectPath`.
- **Proposal:** In Safety/Pipeline, add generic WriteEffectPresentation<TEffect>, PlanResult<TEffect> and ReplanResult<TEffect>, and optionally GuardedWriteResponse<TEffect,TVerification>. Add a WriteCall.FromItems(items, dryRun) factory and a WriteValidation.Compose(catalogResult, protectedCoreChars, accessErrors, toolName) helper. For PLC and HMI, extract a small cached-guard helper class that both domains compose, rather than a base class, keeping MutateAsync and Project domain-specific.
- **Risk:** Generic records may change schema type names in structuredContent schemas, so verify the conformance tests. Domain behavior is unchanged. Do it incrementally (records first, then the domain helper).
- **Verifier notes:** Plc/PlcWriteDomain.cs and Hmi/HmiWriteDomain.cs (both 89 lines) are line-for-line identical except for type names, catalog and budget calls, and MutateAsync (Plc :69-73, Hmi :69-74). The duplicated parts are the _guards/_effects/_attempted/_items fields, PlanAsync, ReplanAsync with DependsOn preservation, the attempted-filtered VerifyAsync and Compose. Validate has the same three steps with the same message in Plc :29-37, Hmi :29-37 and Vci :22-31. The records are structurally identical: XxxWriteEffectPresentation (Plc :41, Hmi :13, Vci :9, Network :41), XxxPlanResult/XxxReplanResult (Plc :52/:54, Hmi :21/:22, Vci :16/:17), and XxxGuardedWriteResponse with the same members and WriteToolError in all four. The WriteCall projectPath expression is the same at Plc/PlcWriteTools.cs:23, Hmi/HmiWriteTools.cs:20, Vci/VciWriteTools.cs:20 and Network/NetworkWriteTools.cs:23. The schema-name risk of generic records is real and correctly flagged.

### HOSTDOMAINS-5 — Payload-contract failure and protocol-rejection scaffolding duplicated; diagnostic emission inconsistent

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcPayloadContract.cs:66-85 JsonException block, :168 Failed`
  - `TiaMcpServer/Hmi/HmiPayloadContract.cs:88-109 JsonException block, :160 Failed`
  - `TiaMcpServer/Network/NetworkPayloadContract.cs:120-147 TryWriteProtocolDiagnostic, :1078 Failed`
  - `TiaMcpServer/Network/HardwarePagePayloadContract.cs:162 Failed; Network/HardwarePaginationCoordinator.cs:206 Failed (wrapper)`
  - `TiaMcpServer/Vci/VciPayloadContract.cs:21-40 Project (local Failed lambda, unguarded Console.Error, reflection MakeGenericMethod per call)`
  - `TiaMcpServer/Hmi/HmiWritePayloadContract.cs:31; Vci/VciWritePayloadContract.cs:23-40`
- **Evidence:** The Failed(operation, category, message, warnings) helpers in Plc, Hmi, Network and HardwarePage build the identical StructuredOperationItem (PLC adds an optional BlockImportOutcome). The 'catch JsonException, then guarded writeProtocolDiagnostic, then protocol_error "did not match its declared result contract"' block is copied in Plc, Hmi and Network. VciPayloadContract writes Console.Error without the try/catch the others document as required. HmiWritePayloadContract and VciWritePayloadContract emit no server diagnostic at all. VciPayloadContract resolves a decoder per call via NormalizeMethod.MakeGenericMethod(type).CreateDelegate.
- **Proposal:** Add `StructuredOperationItem.Failed(IOperationBatchItem item, string category, string message, IReadOnlyList<string> warnings, BlockImportOutcomeInfo? outcome = null)` in OperationBatches. Add one StructuredPayloadProjection.Run(item, result, decode, writeDiagnostic) that owns the worker-failure, decode and protocol_error-plus-guarded-diagnostic path. Use it in all seven projections. In VCI, replace the reflection with a static Dictionary<string, Func<string, JsonElement>> of NormalizeWorkerPayload<T> delegates.
- **Risk:** Low. Item JSON is unchanged if the message text is kept; stderr diagnostics gain consistency. Payload-contract tests cover it.
- **Verifier notes:** Failed helpers build the same StructuredOperationItem at Plc/PlcPayloadContract.cs:168-182 (which adds an optional BlockImportOutcome), Hmi/HmiPayloadContract.cs:160-173, Network/NetworkPayloadContract.cs:1078-1091, HardwarePagePayloadContract.cs:162 and HardwarePaginationCoordinator.cs:206. The guarded diagnostic block is copied in Plc :66-84 and in NetworkPayloadContract.TryWriteProtocolDiagnostic :120-147. Vci/VciPayloadContract.cs:35 calls Console.Error.WriteLine with no try/catch, and :30 runs MakeGenericMethod(type).CreateDelegate on every call. A scoped search of */*WritePayloadContract.cs for Console.Error or Diagnostic returned citable absence, so the HMI and VCI write projections emit no diagnostic.

### HOSTDOMAINS-6 — StructuredOperationBatchExecutionEngine.ApplyWritesAsync is a leftover of the removed generic batch apply (test-only)

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs:71 ApplyWritesAsync`
  - `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs:223`
  - `TiaMcpServer.Tests/Network/NetworkSparseConfigurationTests.cs:48`
  - `TiaMcpServer.Tests/Network/NetworkToolsTests.cs:473`
- **Evidence:** Grep finds no production caller: every domain write mutates through WriteRun.MutateAndVerifyAsync (Safety/Pipeline/WriteRun.cs:353-413). Its doc describes the stop-at-first-failure loop that the removed apply_write_batch path owned. Only three Network tests call it, to drive projection.
- **Proposal:** Delete ApplyWritesAsync. Rewrite the three tests to call NetworkPayloadContract.Project directly or to go through WriteExecution with the existing pipeline fakes.
- **Risk:** Test-only impact; no contract change. The file is outside the strict HOSTDOMAINS scope (OperationBatches), so coordinate with that area's reviewer.
- **Verifier notes:** search_text for ApplyWritesAsync finds only its declaration (OperationBatches/StructuredOperationBatchExecutionEngine.cs:71) and three tests: NetworkMutationVerificationTests.cs:223, NetworkSparseConfigurationTests.cs:48 and NetworkToolsTests.cs:473. find_references returns 0, and there is no production or string-based caller. It is unused in production and used only by tests.

### HOSTDOMAINS-7 — Pass-through wrappers, test-only members and unused CancellationToken parameters

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcContentHashes.cs:6 PlcContentHashes.Compute (pass-through to ContentHashes.Compute)`
  - `TiaMcpServer/Safety/Pipeline/ContentHashes.cs:73 Sha256Hex (internal, test-only)`
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:33 HardwarePageCursorCodec(byte[] key) ctor (test-only)`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:135 TryGetSpec (test-only)`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:100 TryGetWriteFields (test-only)`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:84-89 InvokeReadAsync(cancellationToken), :118-123 InvokeWriteAsync(cancellationToken)`
  - `TiaMcpServer/Hmi/HmiWorkerInvoker.cs:9-15 InvokeReadAsync(cancellationToken)`
- **Corrected locations:**
  - `TiaMcpServer/Plc/PlcContentHashes.cs:8`
  - `TiaMcpServer/Safety/Pipeline/ContentHashes.cs:73`
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:33`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:136`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:100`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:9-14 InvokeReadAsync, :43-48 InvokeWriteAsync`
  - `TiaMcpServer/Hmi/HmiWorkerInvoker.cs:9-15`
- **Evidence:** PlcContentHashes has one member that forwards to ContentHashes.Compute and is used at PlcPayloadContract.cs:164 and PlcWriteVerifier.cs:161. ContentHashes.Sha256Hex forwards to ContentHashRules.Sha256Hex, which production calls directly (WriteRun.cs:554); its only callers are tests. The byte[] codec ctor, TryGetSpec and TryGetWriteFields have zero production references (only TiaMcpServer.Tests). Invoker callers pass only CancellationToken.None (PlcReadTools.cs:44, HmiReadTools.cs:45) or nothing (PlcWriteDomain.cs:72), so the tokens only feed a ThrowIfCancellationRequested that can never fire.
- **Proposal:** Delete PlcContentHashes and call ContentHashes.Compute. Delete ContentHashes.Sha256Hex and point tests at ContentHashRules.Sha256Hex. Delete the byte[] ctor (tests construct an AuthenticatedCursorProtector). Either delete TryGetSpec/TryGetWriteFields or mark them as test seams. Remove the unused cancellationToken parameters, or thread the tool's token through if cancellation is actually wanted.
- **Verifier-corrected proposal:** As proposed. Also update the roughly 8 test call sites of ContentHashes.Sha256Hex and PlcPayloadContractTests.cs:35, and remove PlcContentHashes.cs from TiaMcpServer.Tests.csproj:183.
- **Risk:** Test edits only; PlcContentHashes.cs must also be removed from TiaMcpServer.Tests.csproj Compile Include (line 183).
- **Verifier notes:** PlcContentHashes.Compute (Plc/PlcContentHashes.cs:8) is a pure forward to ContentHashes.Compute, used at PlcPayloadContract.cs:164, PlcWriteVerifier.cs:161 and PlcPayloadContractTests.cs:35. ContentHashes.Sha256Hex (Safety/Pipeline/ContentHashes.cs:73) has no production caller; WriteRun.cs:554 calls ContentHashRules.Sha256Hex directly. It is used by roughly 8 test sites (ContentHashesTests, NetworkGuardedWriteBudgetTests, WriteConfirmationPipelineTests, WriteExecutionTests, ContentHashRulesTests). The HardwarePageCursorCodec(byte[]) ctor (:33) is used only by tests (HardwarePaginationFakeWorkerTests.cs:156); Program.cs:104 uses the protector ctor. TryGetSpec (NetworkOperationCatalog.cs:136) and TryGetWriteFields (PlcOperationCatalog.cs:100) have only test callers. The PlcWorkerInvoker line numbers are wrong: InvokeReadAsync is at :9-14 and InvokeWriteAsync at :43-48, not :84-89/:118-123. Callers pass CancellationToken.None (PlcReadTools.cs:44, HmiReadTools.cs:45) or nothing (PlcWriteDomain.cs:72). No test passes a token (search returned citable absence).

### HOSTDOMAINS-8 — NetworkReadTools uses a static ConditionalWeakTable registry and a second 'compatibility' cursor codec instead of DI

- **Verdict:** confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/NetworkReadTools.cs:18-21 CompatibilityExecutors/CompatibilityCursorCodec, :50 GetValue, :64-72 RegisterExecutor, :74-82 CreateCompatibilityExecutor`
  - `TiaMcpServer/Program.cs:125-137 AddSingleton NetworkReadOperationExecutor + RegisterExecutor`
  - `TiaMcpServer/Hmi/HmiReadTools.cs:25-28 (HmiPageCursorCodec injected as tool parameter)`
- **Evidence:** The production executor is registered in DI and then copied into a static mutable ConditionalWeakTable after host build. Any OpennessWorkerClient not registered (tests) silently gets a separate process-scoped HardwarePageCursorCodec and pagination coordinator. HMI and ProjectTree take their codecs and coordinators as DI-injected tool parameters with no static state.
- **Proposal:** Inject NetworkReadOperationExecutor as a parameter of NetworkRead, as HmiRead does with HmiPageCursorCodec. Delete CompatibilityExecutors, CompatibilityCursorCodec, RegisterExecutor and CreateCompatibilityExecutor, and the RegisterExecutor call in Program.cs.
- **Risk:** Five test files call NetworkReadTools.NetworkRead directly and need to pass an executor. The SDK must resolve the extra parameter from services, which is already proven by HmiReadTools. No contract change.
- **Verifier notes:** Network/NetworkReadTools.cs:18-21 declares a static ConditionalWeakTable and a static CompatibilityCursorCodec. :50 uses GetValue with CreateCompatibilityExecutor (:74-82), and RegisterExecutor (:64-72) is called after host build at Program.cs:136-138. The executor is already a DI singleton (Program.cs:125-127). HmiRead takes HmiPageCursorCodec as a DI-resolved tool parameter (HmiReadTools.cs:45 passes cursorCodec). Test impact is larger than stated: about 20 direct NetworkReadTools.NetworkRead(client, ops) calls across 6+ test files (NetworkIoMapFakeWorkerTests, NetworkOperationFakeWorkerTests, HardwarePaginationFakeWorkerTests, NetworkConnectionEvidenceTests, NetworkSubnetLifecycleFakeWorkerTests and others).

### HOSTDOMAINS-9 — Cursor host-binding/project validators duplicated with divergent unbound rules

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:91-105 IsValidHostBinding, :107-114 SameProject, :149-156 SameProject (second copy in same file), :24 LowercaseSha256`
  - `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs:31 LowercaseSha256, :81-85 IsValidSnapshotId/IsValidQueryHash, :100-115 IsValidHostBinding`
  - `TiaMcpServer/Hmi/HmiPageCursorCodec.cs:44-81 Encode/Decode try/catch, :93-104 Validate (borrows ProjectTreeCursorCodec statics)`
  - `TiaMcpServer/Network/HardwarePagePayloadContract.cs:20 LowercaseSha256`
- **Corrected locations:**
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:24, :91-105 IsValidHostBinding, :107-114 SameProject, :149-156 HardwarePageCursorValidator.SameProject`
  - `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs:31, :81-85, :100-114`
  - `TiaMcpServer/Network/HardwarePageCursorState.cs:23-49 (preserveUnboundEpoch parameter)`
- **Evidence:** The three codecs wrap AuthenticatedCursorProtector with the same Encode/Decode catch filters and per-domain exception types. For an unbound state, HardwarePageCursorCodec.IsValidHostBinding requires BindingId null, while ProjectTreeCursorCodec.IsValidHostBinding requires BindingId non-blank (the unbound epoch). HMI reuses the ProjectTree rule via cross-domain static calls. SameProject appears twice verbatim in HardwarePageCursorCodec.cs. The `^[0-9a-f]{64}$` regex is declared three times.
- **Proposal:** Move the shared state rules (IsValidHostBinding(binding, allowUnboundEpoch), SameProject, IsLowerSha256, IsValidSnapshotId) into TiaMcpServer/Cursors next to AuthenticatedCursorProtector. Make the unbound-epoch difference an explicit parameter, and confirm with the maintainer that hardware-page cursors are meant to reject unbound-epoch states. Remove the duplicate SameProject and regexes.
- **Verifier-corrected proposal:** Do a behavior-neutral move only. Extract IsValidHostBinding(binding, preserveUnboundEpoch), SameProject and an IsLowerSha256 helper into TiaMcpServer/Cursors next to AuthenticatedCursorProtector or ProjectBindingCursorState. Hardware pages pass false and ProjectTree/HMI pass true, mirroring the existing FromSnapshot/Matches parameter. Delete the duplicate SameProject in HardwarePageCursorValidator and the duplicate regexes. Do not change which cursors are accepted.
- **Risk:** If the unbound-rule divergence is unintended, fixing it changes which cursors are accepted (cursor tests, possibly a live check of unbound paging). A pure move is behavior-neutral.
- **Verifier notes:** The duplication is real. The `^[0-9a-f]{64}$` regex appears at HardwarePageCursorCodec.cs:24 and ProjectTreeCursorCodec.cs:31. SameProject is copied verbatim in HardwarePageCursorCodec (:107-114) and in a second class, HardwarePageCursorValidator (:149-156), in the same file. The Encode/Decode catch filters are similar. The unbound-rule divergence is not an inconsistency, though. It is encoded deliberately by ProjectBindingCursorState.FromSnapshot/Matches(snapshot, preserveUnboundEpoch = false) (Network/HardwarePageCursorState.cs:23-49). Hardware pages use the default (false), so an unbound state carries BindingId null. ProjectTree (ProjectTreePageProjector.cs:115, ProjectTreeBrowseCoordinator.cs:151) and HMI (HmiPayloadContract.cs:151, HmiWorkerInvoker.cs:67) pass preserveUnboundEpoch: true. Each IsValidHostBinding matches how its states are issued, so maintainer confirmation of a behavior change is not needed.

### HOSTDOMAINS-10 — SHA-256 lower-hex helper re-implemented outside ContentHashRules

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcContentDiff.cs:188-189 Sha256`
  - `TiaMcpServer/Hmi/HmiPageCursorCodec.cs:90 QueryHash`
  - `TiaMcpServer/ProjectTree/ProjectTreeBrowseRequest.cs:91`
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:554 (uses ContentHashRules.Sha256Hex)`
- **Evidence:** `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()` appears in PlcContentDiff and HmiPageCursorCodec, while ContentHashRules.Sha256Hex already does exactly that and WriteRun uses it. ProjectTreeBrowseRequest hashes a byte buffer with the same lower-hex formatting.
- **Proposal:** Call ContentHashRules.Sha256Hex in PlcContentDiff and HmiPageCursorCodec. Optionally add a Sha256Hex(ReadOnlySpan<byte>) overload for ProjectTreeBrowseRequest.
- **Risk:** None if encoding and casing are identical; covered by ContentHashRulesTests.
- **Verifier notes:** `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(...))).ToLowerInvariant()` appears at Plc/PlcContentDiff.cs:189 and Hmi/HmiPageCursorCodec.cs:90. ProjectTree/ProjectTreeBrowseRequest.cs:91 hashes a byte buffer with the same formatting. ContentHashRules.Sha256Hex (TiaMcpServer.Contracts/Shared/ContentHashRules.cs:30-41) produces identical output: UTF-8 bytes and lower-case x2 hex. It is already used by WriteRun.cs:554.

### HOSTDOMAINS-11 — Network selector kind/field matrix encoded twice (request table vs hand-coded payload switch)

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs:466-512 ValidateSelectorShape (SelectorRequiredFields/SelectorApplicableFields tables), :555-708 ValidateSelectorValues`
  - `TiaMcpServer/Network/NetworkPayloadContract.cs:807-942 ValidateSelector switch, :985-1021 RejectSelectorFields/RejectSelectorField`
- **Evidence:** Host request validation drives required and applicable selector fields per kind from dictionaries. Worker-payload validation of NetworkObjectSelectorInfo hard-codes the same per-kind matrix as a 136-line switch with boolean reject flags (interfaceFields, nodeField, subnetField, ...). Adding a kind or field means editing both places in sync, with no test forcing it.
- **Proposal:** Move the per-kind required/applicable field table into one NetworkSelectorRules class. Use it in both the catalog and the payload contract, keeping payload-only checks (interfacePath vs itemPath exclusivity, expectedKind match) as small extras. At minimum, add a test that asserts both encodings agree for every NetworkObjectKinds value.
- **Risk:** Request and payload selector types differ (NetworkObjectTarget vs NetworkObjectSelectorInfo), so an accessor mapping is needed. protocol_error vs validation_error wording must stay. Network payload tests cover it, so no live check is needed.
- **Verifier notes:** NetworkOperationCatalog.cs:79-106 holds the per-kind tables SelectorRequiredFields and SelectorApplicableFields. NetworkPayloadContract.ValidateSelector (:807-942) hand-codes the same matrix through RejectSelectorFields boolean flags (:985-1013). There is already observable drift. For NetworkInterface, the catalog's applicable set excludes ioSystemIndex/ioSystemName, but the payload switch (:837-844) rejects node/subnet/number/connection fields and not ioSystemFields. SupportedConnectionTypes is also declared twice (NetworkOperationCatalog.cs:117-128 and NetworkPayloadContract.cs:690-701). Whether payload selectors should accept exactly the request matrix needs maintainer confirmation.

### HOSTDOMAINS-12 — Network files exceed 1,000 lines with 100–157-line methods

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** good-practice · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/NetworkPayloadContract.cs (1,092 lines): ValidateSelector :807-942, ValidateAttribute :729-805, ValidateDeviceItem :376-451`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs (1,000 lines): Validate :167-322, ValidateSelectorValues :555-708, ValidateConfigureNetworkDevice :709-823`
  - `TiaMcpServer/Network/NetworkWriteVerifier.cs:21 VerifyAsync (~157 lines)`
  - `TiaMcpServer/Plc/PlcWorkingState.cs:225 ResolveEffect (~135 lines)`
  - `TiaMcpServer/ProjectLifecycle/LifecycleWriteDomain.cs:293 Compose (~96 lines)`
- **Corrected locations:**
  - `TiaMcpServer/Network/NetworkPayloadContract.cs (1,092 lines): ValidateSelector :807-942, ValidateAttribute :729-805, ValidateDeviceItem :376-451`
  - `TiaMcpServer/Network/NetworkOperationCatalog.cs (1,000 lines): Validate :167-321, ValidateSelectorValues :555-702, ValidateConfigureNetworkDevice :709-816`
  - `TiaMcpServer/Network/NetworkWriteVerifier.cs:21-176 VerifyAsync`
  - `TiaMcpServer/Plc/PlcWorkingState.cs:225-358 ResolveEffect`
  - `TiaMcpServer/ProjectLifecycle/LifecycleWriteDomain.cs:293-387 Compose`
- **Evidence:** Line counts from wc -l and method spans from awk over declarations. Both Network files are past the 800-line review signal. Each mixes several independent validator families: hardware config/device items/IO details, catalog, subnet lifecycle, object list/inspection/attributes, selectors.
- **Proposal:** Split by family into partial or static classes without changing behavior: NetworkPayloadContract into HardwareConfigPayloadValidator, NetworkObjectPayloadValidator (attributes/inspection) and NetworkMutationPayloadValidator (configuration/subnet/verification); NetworkOperationCatalog into the spec table plus per-operation validators. Do this after HOSTDOMAINS-2 and HOSTDOMAINS-11, which remove some of the bulk.
- **Risk:** Pure move or refactor. Tests link Network files explicitly in TiaMcpServer.Tests.csproj, so new files must be added there.
- **Verifier notes:** The sizes are confirmed: NetworkPayloadContract class spans :24-1092 and NetworkOperationCatalog :31-1000. Method spans from the outline are ValidateSelector :807-942, ValidateAttribute :729-805, ValidateDeviceItem :376-451, Validate :167-321, ValidateSelectorValues :555-702 and ValidateConfigureNetworkDevice :709-816 (not :823). NetworkWriteVerifier.VerifyAsync spans :21-176, PlcWorkingState.ResolveEffect :225-358 and LifecycleWriteDomain.Compose :293-387. Only minor line-range corrections are needed.

### HOSTDOMAINS-13 — VCI verifier hard-codes failure-category strings and infers attempts differently from siblings

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:11-12 NeverRan, :17 fatal categories`
  - `TiaMcpServer/Vci/VciWriteDomain.cs:73-74 VerifyAsync (no attempted filter)`
  - `TiaMcpServer/Plc/PlcWriteDomain.cs:71,78-80; Hmi/HmiWriteDomain.cs:71,78-80; Network/NetworkWriteDomain.cs:91,99-101 (_attempted set)`
- **Corrected locations:**
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:11-12 NeverRan, :17 fatal categories`
- **Evidence:** NeverRan lists "validation_error", "target_not_found", "state_changed", "guard_blocked", "binding_conflict", "access_denied" and others, and the fatal check uses "portal_lost", "worker_timeout", "worker_crashed", all as literals, although every one is a WorkerFailureCategories constant. PLC, HMI and Network record attempted items in MutateAsync and verify only those. VCI passes the whole batch and guesses which items never ran from failure categories, so a new pre-dispatch category would be misclassified as attempted.
- **Proposal:** Replace the literals with WorkerFailureCategories constants. Add the same _attempted HashSet in VciWriteDomain.MutateAsync, filter the batch in VerifyAsync like the siblings, and drop NeverRan (keep the fatal check).
- **Verifier-corrected proposal:** Replace the literals in NeverRan and the fatal check with WorkerFailureCategories constants. Keep VCI's category-based attempted inference and its unfiltered batch, because it intentionally classifies post-dispatch non-mutating failures and emits notVerified for skipped items. Optionally add a comment explaining why VCI differs from Plc/Hmi.
- **Risk:** VCI verification output for never-dispatched items must stay 'notVerified'; covered by VCI verifier tests. Scoped live VCI acceptance exists, so re-run a quick vci_write smoke only if the attempted logic changes.
- **Verifier notes:** The literals are confirmed. Vci/VciWriteVerifier.cs:11-12 (NeverRan) and :17 (fatal) use strings that all exist as WorkerFailureCategories constants: ValidationError, TargetNotFound, TargetAmbiguous, StateChanged, GuardBlocked, BindingConflict, AccessDenied, PortalLost, WorkerTimeout and WorkerCrashed. The _attempted proposal is wrong, though. NeverRan means 'no mutation took effect', not 'never dispatched'. The VCI worker returns state_changed/target_not_found after dispatch from its fresh owner checks and adjacent recheck (AGENTS.md VCI bullet), and with an _attempted filter those items would be read back instead of reported notVerified. The verifier also needs Skipped items in the batch to emit 'notVerified' (:26-27), and the `attempted` list drives the 'superseded' logic (:28-29). Filtering the batch as Plc/Hmi do (PlcWriteDomain.cs:78-80, HmiWriteDomain.cs:78-80) would drop or alter those checks.

### HOSTDOMAINS-14 — PlcWriteDomain.MutateAsync ignores the pipeline's pinned projectPath

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Plc/PlcWriteDomain.cs:69-73 MutateAsync`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:118-144 InvokeWriteAsync (uses op.ProjectPath)`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:364-372 (blank projectPath items ignored in same-project check)`
  - `TiaMcpServer/Hmi/HmiWriteDomain.cs:73; Vci/VciWriteDomain.cs:62-63; Network/NetworkWriteDomain.cs:91 (pass projectPath)`
- **Evidence:** WriteRun passes _call.ProjectPath (the first non-blank item path) to PlanAsync, ReplanAsync, MutateAsync and VerifyAsync. PLC planning and verification use it, but MutateAsync drops it and the invoker sends each item's own ProjectPath. Validation allows a blank-path item next to an explicit one, so that item is planned against X but mutated with null (the active project). The verified binding gate makes that the same project today, but PLC is the only domain that relies on it.
- **Proposal:** Add a projectPath parameter to PlcWorkerInvoker.InvokeWriteAsync (as NetworkWorkerInvoker.InvokeWriteAsync has commonProjectPath) and pass the pipeline value from PlcWriteDomain.MutateAsync.
- **Risk:** Worker requests from mixed blank/explicit calls would now carry the explicit path, which should be equivalent under a verified binding. PlcWriteInvokerTests need updating, and a plc_write live smoke is advisable.
- **Verifier notes:** Plc/PlcWriteDomain.cs:69-73 MutateAsync ignores its projectPath parameter and calls PlcWorkerInvoker.InvokeWriteAsync(client, item). The invoker (:43-69) sends op.ProjectPath per item. By contrast, Hmi/HmiWriteDomain.cs:73, Vci/VciWriteDomain.cs:61-62 and Network/NetworkWriteDomain.cs:91 pass the pipeline projectPath. PLC planning and verification (:41, :78) do use projectPath. This is benign under a verified binding, so severity is low as stated.

## HOSTCORE

The core is in good shape overall. The guarded pipeline (WriteExecution/WriteRun) is the only write path, there is one shared HMAC sealer (AuthenticatedCursorProtector) for cursors, and CanonicalJson is used consistently. The main debt is leftovers from removed features: an AllowTiaConfirmations wire flag the worker never reads, the old preview/apply wording, a legacy text-envelope formatter that nothing in production calls, and ApplyWritesAsync, which only tests use. OpennessWorkerClient (2,557 lines) repeats its binding-pin and startup-grounding logic. Sibling seams also behave differently: gate failure categories, the binding-consistency rules, cursor error categories, the guard-severity validation, and the Confirm fence.

### HOSTCORE-1 — WorkerRequest.AllowTiaConfirmations is a dead wire field: the host sets it ~22 times, the worker never reads it

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56 Confirm/AllowTiaConfirmations`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945,985,1011,1039,1057,1128,1204,1275,1295,1321,1357,1379,1405,1431,1453,1474,1672,1768,1802,1844,1872,1886 (request.AllowTiaConfirmations = true)`
  - `TiaMcpServer.OpennessWorker/Program.cs:32 _sharedSession = new(allowTiaConfirmations: true)`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:24-31,949`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleForwardingTests.cs:233,265,331`
- **Corrected locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945,985,1011,1039,1057,1128,1185(doc),1204,1275,1295,1321,1357,1379,1405,1431,1453,1475,1489,1503,1517,1672,1768,1802,1844,1872,1886`
  - `TiaMcpServer.OpennessWorker/Program.cs:32`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleForwardingTests.cs:233,265,331`
- **Evidence:** In TiaMcpServer.OpennessWorker/**, AllowTiaConfirmations appears only as the TiaPortalSession constructor flag, and Program.cs:32 hardcodes it to true for the shared session. No worker code reads request.AllowTiaConfirmations. The doc comments on WorkerRequest.cs:50-55 ("Set by every write operation, including update_block_logic") describe a gate that no longer exists. The only consumers are tests that check the flag is serialized.
- **Proposal:** Delete WorkerRequest.AllowTiaConfirmations and all of its host assignments. Drop the three forwarding-test assertions. Fix the WorkerRequest.Confirm doc comment so it no longer mentions AllowTiaConfirmations.
- **Risk:** Wire-shape change only. Host and worker ship together, and the worker reads requests leniently, so an older worker still works without the field. Check whether WorkerProtocol.Version must change under the repo's policy. No live verification needed.
- **Verifier notes:** search_text for AllowTiaConfirmations: the worker only has the TiaPortalSession ctor flag (TiaPortalSession.cs:13,24,31,949) and Program.cs:32 hardcodes new(allowTiaConfirmations: true). No worker code reads request.AllowTiaConfirmations, and FakeWorker has no reference. Host assignments are in OpennessWorkerClient.cs, and also at 1489, 1503 and 1517, which the finding's list missed. Tests only check serialization (NetworkSubnetLifecycleForwardingTests.cs:233,265,331). The doc comments at WorkerRequest.cs:49-55 are stale.

### HOSTCORE-2 — The worker 'Confirm' mutation fence is applied unevenly (HMI/VCI never set it) and its errors point at a removed public confirm=true

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:944,984,1010,1274,1294,1320 (Network/PLC wrappers set Confirm=true)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1231-1232 WriteVciAsync, 1244-1245 WriteHmiAsync (no Confirm)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1104-1130 UpdateBlockLogicAsync (no Confirm)`
  - `TiaMcpServer.OpennessWorker/Program.cs:550,652,672,707,750,788,1141,1164,1182,1200,1370 if (!request.Confirm) ... "Set confirm=true to proceed"`
  - `TiaMcpServer.OpennessWorker/Program.cs:1381-1390 ProjectLifecycle(requiresConfirm)`
- **Corrected locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1119-1130,1195-1206,1231-1245`
  - `TiaMcpServer.OpennessWorker/Program.cs:550-554,652-656,672-676,707-711,750-754,788-792,1141-1204,1370-1374,1385-1389`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs:33`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-53`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerDispatchTests.cs:30-39`
- **Evidence:** The host sets Confirm=true unconditionally in every Network, PLC-tag/block and lifecycle wrapper, so the flag decides nothing at runtime. It is only a fence against host code that forgets it (WriteSafetyLeaseConcurrencyTests.cs:215 "Worker mutation fence missing"). The HMI and VCI write paths and update_block_logic never set it. The worker's failure text tells the caller to "Set confirm=true", but AGENTS.md says public confirm arguments are rejected at the SDK entry.
- **Proposal:** Pick one model. (a) Remove Confirm and the worker checks, and rely on the capability gates (OperationPolicyCatalog/ProjectCapabilityCatalog). Or (b) set Confirm in exactly one place, SendBoundProjectRequestCoreAsync, whenever OperationPolicyCatalog marks the method as mutating, so HMI/VCI are covered as well, and remove the per-wrapper assignments. In both cases, reword the worker message so it names a host fault (protocol_error-style), not a caller action.
- **Verifier-corrected proposal:** Prefer (b). Set Confirm once in SendBoundProjectRequestCoreAsync when OperationPolicyCatalog classifies the method as mutating, and remove the per-wrapper assignments. Keep the worker fences, including IoSystemQualificationProbeInfo.Validate. Reword the worker messages to name a host/protocol fault, not a caller action. Correct the WorkerRequest.Confirm doc. If (a) is chosen, also update NetworkSubnetLifecycleWorkerDispatchTests, IoSystemQualificationProbeContractTests and WriteSafetyLeaseConcurrencyTests.cs:215.
- **Risk:** (a) changes worker behaviour for a host that forgets the fence. (b) newly fences HMI/VCI: confirm their worker handlers tolerate the flag. Unit tests that check the fence must be updated. No live run is needed if (b) is limited to the host.
- **Verifier notes:** Confirmed: the host sets Confirm=true unconditionally in Network, PLC and lifecycle wrappers. It does not set it in WriteVciAsync/WriteHmiAsync (OpennessWorkerClient.cs:1231-1245), UpdateBlockLogicAsync (1119-1130) or UpdateTypeContentAsync (1195-1206). The worker messages say 'Set confirm=true to proceed' (Program.cs:554,656,...,1374,1389). The WorkerRequest.Confirm doc (WorkerRequest.cs:49-52) is stale: it names update_block_logic as the only exception. The finding missed that option (a) also breaks source-text tests: NetworkSubnetLifecycleWorkerDispatchTests.cs:30-39 asserts handler bodies contain "if (!request.Confirm)", and the fence also exists in the shared contract IoSystemQualificationProbeInfo.cs:33 with IoSystemQualificationProbeContractTests.cs:16,70-72,130-132. The proposal should prefer (b), or simply reword the messages.

### HOSTCORE-3 — Pinned-binding staleness check (with stale 'after preview' text) and configured-project grounding are each copy-pasted 3–4× in OpennessWorkerClient

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:351-357 ExecutePinnedCoreAsync`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1648-1656 OpenProjectCoreAsync`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1749-1757 CreateProjectCoreAsync`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1948-1957 SendBoundProjectRequestCoreAsync`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:384-396 RequireVerifiedWriteBindingAsync; 709-727 ReadHardwarePageCandidatesCoreAsync; 1921-1934 SendBoundProjectRequestCoreAsync (ConfiguredUnverified grounding)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:293-297 doc 'legacy preview/apply critical section'`
- **Corrected locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:351-357,1648-1656,1749-1757,1948-1957`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:384-396,711-726,1921-1934`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:294,373`
  - `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs:178`
- **Evidence:** Four sites repeat `bindingBeforeCall = _bindingOperationContext.Value?.PinnedBinding ?? current; if (pinned is not null && !SameBinding) Fail(BindingConflict, "The worker/Portal/project binding changed after preview. No operation was performed; request a fresh preview.")`. Previews were the token-era preview/apply flow, which has been removed. Today a dryRun is a separate call, and this check fires between the gate and the lease. Three sites repeat `if (State==ConfiguredUnverifiedState && ProjectPath non-blank) await GetProjectStatusAsync(initial.ProjectPath)`, each with slightly different post-handling.
- **Proposal:** Extract `private bool TryGetCallBinding(out ProjectBindingSnapshot bindingBeforeCall, out WorkerCallResult? failure)` and `private async Task<WorkerCallResult?> GroundConfiguredStartupProjectAsync()`, and call both from all sites. Reword the message to e.g. "The project binding changed before this operation was dispatched. Nothing was sent; inspect project status and retry." Fix the 'legacy preview/apply' docs at lines 294 and 373.
- **Verifier-corrected proposal:** As proposed. The grounding helper must return the failed verification untouched so each caller keeps its own wrapping. Update the OpennessWorkerClientIntegrationTests.cs:178 assertion together with the reworded message.
- **Risk:** Error text changes, so grep tests for the 'fresh preview' string. Behaviour is otherwise unchanged, and the existing OpennessWorkerClientIntegrationTests/WriteSafetyLeaseConcurrencyTests cover it. No live run needed.
- **Verifier notes:** The identical 'changed after preview ... request a fresh preview' check is at OpennessWorkerClient.cs:351-357, 1648-1656, 1749-1757 and 1948-1957. ConfiguredUnverified grounding is repeated at 384-396, 711-726 and 1921-1934, with different post-handling (HardwarePageWorkerCallResult wrap, and an update_block_logic NotSent special case). The 'legacy preview/apply' docs are at 294 and 373. A test pins the text: OpennessWorkerClientIntegrationTests.cs:178 Assert.Contains("changed after preview").

### HOSTCORE-4 — Default write gate collapses failure categories to binding_conflict and uses a different 'binding moved' rule than the lifecycle strategy

- **Verdict:** confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:117-133 ExecuteCoreAsync (gate failure → BindingConflict)`
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:156-170 PinBinding`
  - `TiaMcpServer/ProjectLifecycle/LifecycleBindingStrategy.cs:52-70 PrepareVerifiedSourceAsync/Reject`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:377-401 RequireVerifiedWriteBindingAsync (returns GetProjectStatusAsync failure verbatim)`
- **Corrected locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:117-133,156-170`
  - `TiaMcpServer/ProjectLifecycle/LifecycleBindingStrategy.cs:52-70`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:377-401`
- **Evidence:** RequireVerifiedWriteBindingAsync can return worker_timeout, worker_crashed, portal_lost or protocol_error from its grounding status call. WriteRun replaces every one of these with WorkerFailureCategories.BindingConflict. LifecycleBindingStrategy.Reject keeps `result.FailureCategory`. The consistency predicates also differ. PinBinding accepts a configured→verified transition on a path match alone. PrepareVerifiedSourceAsync also requires `prepared.Revision == before.Revision + 1`. Scenario: with a startup --project and a worker timeout during grounding, plc_write/network_write/hmi_write/vci_write report binding_conflict, while open/save report worker_timeout.
- **Proposal:** Make the default path an `IWriteBindingStrategy` (e.g. VerifiedProjectBindingStrategy) so WriteRun has one code path. Share a single `BindingPreparation.IsConsistent(before, after)` predicate with LifecycleBindingStrategy, and keep the gate's category (fall back to BindingConflict only when it is null).
- **Verifier-corrected proposal:** Minimal change: keep gate.FailureCategory ?? BindingConflict at WriteRun.cs:123-125, and share one IsConsistent(before, after) predicate between PinBinding and PrepareVerifiedSourceAsync. Wrapping the default path as an IWriteBindingStrategy is optional. Check pipeline tests (WriteBindingStrategyTests, OpennessWriteBindingGateTests, WriteExecutionTests) and the tool docs that list error categories.
- **Risk:** Changes error categories reported by the four data-write tools, which is a contract change, so update docs and affected pipeline tests. Adopting the revision+1 rule could reject a case that passes today. Live verification not required.
- **Verifier notes:** WriteRun.cs:119-126 replaces any RequireVerifiedWriteBindingAsync failure with BindingConflict. That method can return the GetProjectStatusAsync failure verbatim (OpennessWorkerClient.cs:391-395), e.g. worker_timeout or worker_crashed. LifecycleBindingStrategy.Reject (LifecycleBindingStrategy.cs:68-70) keeps result.FailureCategory. The predicates differ: PinBinding (WriteRun.cs:156-170) accepts configured→verified on a path match, while PrepareVerifiedSourceAsync (:52-66) also requires Revision == before.Revision+1. AGENTS.md does not document the difference. OpennessWriteBindingGate.cs:79-83 likewise maps lease failures to BindingConflict, but ExecuteWithPinnedBindingAsync only produces BindingConflict, so that part is harmless.

### HOSTCORE-5 — StructuredOperationBatchExecutionEngine.ApplyWritesAsync is unused in production (generic batch remnant); WriteRun duplicates it

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs:59-101 ApplyWritesAsync`
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs:17-37 ExecuteReadsAsync → private ExecuteDirectReadsAsync pass-through`
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:353-374 MutateAndVerifyAsync, 596-599 Skipped`
  - `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs:223; NetworkSparseConfigurationTests.cs:48; NetworkToolsTests.cs:473`
- **Corrected locations:**
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs:17-37,59-101`
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:353-374,596-599`
  - `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs:223`
  - `TiaMcpServer.Tests/Network/NetworkSparseConfigurationTests.cs:48`
  - `TiaMcpServer.Tests/Network/NetworkToolsTests.cs:473`
- **Evidence:** search_text shows ApplyWritesAsync has zero callers under TiaMcpServer/. All four write tools go through WriteExecution → WriteRun.MutateAndVerifyAsync, which re-implements the same stop-at-first-non-succeeded loop. Both places build the same Skipped item with StructuredOperationSkipReasons.EarlierOperationFailed. The public 2-argument ExecuteReadsAsync only forwards to a private method with the same body.
- **Proposal:** Delete ApplyWritesAsync and move the three Network tests to WriteExecution, or give them a local test helper. Add a `StructuredOperationItem.Skipped(IOperationBatchItem)` factory and use it from WriteRun. Inline ExecuteDirectReadsAsync into the public ExecuteReadsAsync, and optionally express the 3-argument overload through the 2-argument one.
- **Risk:** Only tests change. No production behaviour change.
- **Verifier notes:** search_text: ApplyWritesAsync (StructuredOperationBatchExecutionEngine.cs:71) is called only from tests: NetworkMutationVerificationTests.cs:223, NetworkSparseConfigurationTests.cs:48 and NetworkToolsTests.cs:473. WriteRun.MutateAndVerifyAsync (WriteRun.cs:353-374) re-implements the stop-at-first-non-succeeded loop, and WriteRun.Skipped (596-599) builds the same item as engine lines 83-91. The 2-arg ExecuteReadsAsync (17-21) is a pure pass-through to the private ExecuteDirectReadsAsync (23-37).

### HOSTCORE-6 — Legacy text-contract helpers are dead in production: StandaloneToolResultFormatter, WorkerCallResult.ToText/ToEnvelopeText; TiaJson doc is stale

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs:6-34`
  - `TiaMcpServer/Worker/WorkerCallResult.cs:80-103 ToText, ToEnvelopeText`
  - `TiaMcpServer/Json/TiaJson.cs:5-34 class doc`
  - `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:146-147`
  - `TiaMcpServer/Safety/Pipeline/WriteAudit.cs:123 (audit uses CanonicalJson)`
- **Corrected locations:**
  - `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs:6-34`
  - `TiaMcpServer/Worker/WorkerCallResult.cs:80-103`
  - `TiaMcpServer/Json/TiaJson.cs:5-34`
  - `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:146-147`
  - `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs:869`
- **Evidence:** A repo-wide grep finds StandaloneToolResultFormatter only in its own file, its tests and the csproj link. ToText has only test callers, and ToEnvelopeText is called only by that formatter. ToEnvelopeText puts `payload` in as a string, which is the 'nested JSON string' AGENTS.md forbids. TiaJson's doc says it serves 'tools not yet on the structured contract' and that 'audit records and their hashes are derived through it'. AGENTS.md says every tool is now structured, and audit records are serialized and hashed through CanonicalJson (WriteAudit.cs:123, WriteRun.cs:554). In production, TiaJson.Presentation is now used only by the size check at BlockImportOutcomeValidator.cs:93.
- **Proposal:** Delete StandaloneToolResultFormatter and its tests and csproj link, plus WorkerCallResult.ToText and ToEnvelopeText and their WorkerCallResultTests cases. Rewrite the TiaJson doc to its one remaining purpose (measuring BlockImportOutcome size to match the worker projection), or move the options next to BlockImportOutcomeValidator.
- **Verifier-corrected proposal:** As proposed. Also update OpennessWorkerClientIntegrationTests.cs:869, which uses ToText; assert on result.Error instead. Keep TiaJson, which BlockImportOutcomeValidator, the worker projection comment and the CompileChecker tests rely on.
- **Risk:** No production behaviour change. Some tests are removed. CompileChecker/CompileReportProjection tests use TiaJson.Presentation, so keep the type.
- **Verifier notes:** StandaloneToolResultFormatter appears only in its own file, StandaloneToolResultFormatterTests and the csproj link (TiaMcpServer.Tests.csproj:146-147). ToEnvelopeText is called only by that formatter (StandaloneToolResultFormatter.cs:32) and by WorkerCallResultTests. ToText has only test callers (WorkerCallResultTests, OpennessWorkerClientIntegrationTests.cs:869). The TiaJson.cs:9-11,20-21,31-32 docs describe legacy text and audit hashing, but the audit uses CanonicalJson (WriteAudit.cs:123) and the hash uses ContentHashRules (WriteRun.cs:554). In production, TiaJson.Presentation is used only by BlockImportOutcomeValidator.cs:93 and by WorkerCallResult.cs:103, which is the ToEnvelopeText being deleted.

### HOSTCORE-7 — Docs and messages still refer to removed features (PLC run/stop, legacy audit stream, batch tools, preview/apply)

- **Verdict:** confirmed · **Severity:** low · **Category:** docs-drift · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:21-31 WorkerTransportFailureGuidance ("PLC runtime mutation", "PLC runtime state")`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:85-86 AccessPolicy doc "Used by batch tools"`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:294 "legacy preview/apply", 373 "project-mutating preview/apply path"`
  - `TiaMcpServer/Safety/Pipeline/WriteAudit.cs:95-97,106 "beside the legacy daily audit files", "same default as the legacy audit"`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:50-55`
- **Corrected locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:21-31,85-86,294,373`
  - `TiaMcpServer/Safety/Pipeline/WriteAudit.cs:94-97,106`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-55`
- **Evidence:** AGENTS.md says PLC run/stop, the generic batch tools and the legacy write audit stream were removed. The user-facing timeout and crash guidance still says 'No project or PLC runtime mutation was requested' and 'The project or PLC runtime state may have changed'.
- **Proposal:** Reword the guidance to 'project state', and update the four doc comments. TiaPortalSessionBindingGuidanceSourceTests.cs:54 reads the source text around RequireVerifiedWriteBindingAsync, so update it alongside the edit.
- **Risk:** Tests that pin the guidance constants or read source text will need matching updates. Text only.
- **Verifier notes:** Confirmed by reading the code. OpennessWorkerClient.cs:21-31 still says 'No project or PLC runtime mutation' and 'project or PLC runtime state'. OpennessWorkerClient.cs:85-86 says 'Used by batch tools'. Lines 294 and 373 still say 'legacy preview/apply'. WriteAudit.cs:96 says 'beside the legacy daily audit files' and :106 says 'same default as the legacy audit'. WorkerRequest.cs:49-55 is stale.

### HOSTCORE-8 — Small dead members and pass-through layers left from earlier designs

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:950-961 ConfigureNetworkDeviceAsync(deviceName,nodeId,...) overload`
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:33-36 HardwarePageCursorCodec(byte[] key) 'compatibility' ctor`
  - `TiaMcpServer/Safety/Pipeline/WriteGuards.cs:33 WriteGuardCatalog.Production`
  - `TiaMcpServer/Safety/Pipeline/IWriteBindingGate.cs:31-41,64-68 non-cancellable RunUnderLeaseAsync overload`
  - `TiaMcpServer/Safety/Pipeline/ContentHashes.cs:31 Compute, 73 Sha256Hex; TiaMcpServer/Plc/PlcContentHashes.cs:8`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2338 `_accessPolicy is not null`; TiaMcpServer/Safety/Pipeline/IWriteBindingGate.cs:57 `AccessPolicy?.Mode ?? ReadWrite``
- **Corrected locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:950-961,2338`
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:33-36`
  - `TiaMcpServer/Safety/Pipeline/WriteGuards.cs:32-33`
  - `TiaMcpServer/Safety/Pipeline/IWriteBindingGate.cs:31-41,57,64-68`
  - `TiaMcpServer/Plc/PlcContentHashes.cs:8`
  - `TiaMcpServer/Safety/Pipeline/ContentHashes.cs:31,73`
- **Evidence:** The string overload of ConfigureNetworkDeviceAsync has no callers. NetworkWorkerInvoker.cs:97 and the integration test both use the selector overload. The byte[] codec ctor is used only by tests, because Program.cs:104 injects the process-scoped protector. WriteGuardCatalog.Production is used only by WriteGuardCatalogTests, and the real catalog is WriteGuardRegistration.ProductionCatalog() (McpToolRegistration.cs:31), so the name misleads. WriteRun.cs:144 calls only the CancellationToken lease overload. Two thin forwarding layers sit on ContentHashRules.Compute (PlcContentHashes.Compute → ContentHashes.Compute → ContentHashRules.Compute). ContentHashes.Sha256Hex has no callers, since WriteRun.cs:554 calls ContentHashRules directly. _accessPolicy is a non-nullable readonly field that the constructor always assigns.
- **Proposal:** Delete the string overload and the byte[] ctor (point tests at a protector). Rename WriteGuardCatalog.Production to PipelineOnly or delete it. Collapse IWriteBindingGate to one `RunUnderLeaseAsync(binding, op, CancellationToken ct = default)`. Have callers use ContentHashRules.Compute directly, keeping only ContentHashes.Check, and drop PlcContentHashes. Remove the two impossible null checks.
- **Verifier-corrected proposal:** As proposed. When removing ContentHashes.Sha256Hex, rewrite its many test uses (NetworkGuardedWriteBudgetTests, WriteExecutionTests, WriteConfirmationPipelineTests, LifecycleMcpProtocolTests, ContentHashesTests, ContentHashRulesTests) to use ContentHashRules.Sha256Hex.
- **Risk:** Updates needed in test fakes: PipelineFakes.FakeWriteBindingGate, PlcGuardedWriteFixture.ShiftingGate. No runtime change.
- **Verifier notes:** The string overload ConfigureNetworkDeviceAsync (OpennessWorkerClient.cs:950-961) has no callers. NetworkWorkerInvoker.cs:97-98 passes MapSelector(...), and the integration test (:46) passes a selector. The byte[] HardwarePageCursorCodec ctor (HardwarePageCursorCodec.cs:33-36) is used only by tests (HardwarePaginationFakeWorkerTests.cs:156); Program.cs:104 uses the protector ctor. WriteGuardCatalog.Production (WriteGuards.cs:33) is used only by WriteGuardCatalogTests.cs:13,65. PlcContentHashes.Compute (PlcContentHashes.cs:8) just forwards to ContentHashes.Compute. ContentHashes.Sha256Hex has only test callers. _accessPolicy is always assigned non-null (OpennessWorkerClient.cs:82), so the check at :2338 and AccessPolicy?.Mode (IWriteBindingGate.cs:57) are redundant. The non-cancellable lease overload is only reached through the interface default and fakes.

### HOSTCORE-9 — Generic WriteRun hard-codes a lifecycle type check when comparing consequences

- **Verdict:** confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:340-347 ConsequenceDocument`
  - `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs:89-124`
- **Corrected locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:340-347`
  - `TiaMcpServer/ProjectLifecycle/LifecycleEffects.cs:21`
- **Evidence:** `typeof(TEffect) == typeof(LifecycleEffects) ? ... effect.ForConsequenceComparison() ...` puts knowledge of one domain inside the domain-free pipeline. Its own doc says "A domain supplies the resolver, guards...; the order and the safety rules live here".
- **Proposal:** Add a default interface member `TEffect ForConsequenceComparison(TEffect effect) => effect` to IWriteDomain and override it in LifecycleWriteDomain. ConsequenceDocument then maps through _domain.
- **Risk:** None functionally. WriteConfirmationPipelineTests and lifecycle tests already cover it.
- **Verifier notes:** WriteRun.cs:340-347 checks typeof(TEffect) == typeof(LifecycleEffects) and calls LifecycleEffects.ForConsequenceComparison (LifecycleEffects.cs:21), its only caller. This puts lifecycle knowledge into the generic pipeline.

### HOSTCORE-10 — PersistentWorkerTransport runs VCI-specific checks and parses each response up to three times

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** good-practice · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs:109-157 ExchangeAsync`
  - `TiaMcpServer/Vci/VciWritePayloadContract.cs:12-21 ValidateEnvelopeOutcome`
  - `TiaMcpServer/Worker/ProjectContextPayloadContract.cs:99-117 ValidateEnvelopeIdentity (re-parses responseLine)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2367-2396 InvokeWorkerAsync (maps HmiWriteOutcome/VciWriteOutcome)`
- **Corrected locations:**
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs:109-157`
  - `TiaMcpServer/Vci/VciWritePayloadContract.cs:12-21`
  - `TiaMcpServer/Worker/ProjectContextPayloadContract.cs:99-117`
  - `TiaMcpServer/Hmi/HmiWritePayloadContract.cs:19-35`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2367-2396`
- **Evidence:** The transport describes itself as owning the line protocol, yet it calls Vci.VciWritePayloadContract.ValidateEnvelopeOutcome on every response. The HMI write outcome gets no such check at the envelope level. Each line goes through JsonDocument.Parse (line 112), JsonSerializer.Deserialize (line 126), and another JsonDocument.Parse inside ValidateEnvelopeIdentity for local sessions. The two protocol_error returns (lines 119-124, 149-154) both repeat the same new WorkerResponse block.
- **Proposal:** Have the transport parse once and return (WorkerResponse, JsonElement root clone). Move the outcome and identity contract checks to InvokeWorkerAsync, where the outcomes are already mapped, and apply HMI and VCI symmetrically through one helper. Keep the transport domain-free.
- **Verifier-corrected proposal:** Parse once in the transport and keep a cloned raw `vciWriteOutcome`/`sessionIdentity` element (or the root clone) on the response. Run both strict checks from InvokeWorkerAsync or from the domain payload contracts. Optionally make the HMI outcome use the same strict raw-element check for symmetry. Keep the no-kill protocol_error behaviour, and extract one ProtocolErrorResponse(message) helper for the two duplicated blocks.
- **Risk:** Protocol-error paths must still keep the worker alive (no kill), as they do today. Existing transport and protocol tests cover this. No live run needed.
- **Verifier notes:** Confirmed: PersistentWorkerTransport.ExchangeAsync calls Vci.VciWritePayloadContract.ValidateEnvelopeOutcome (PersistentWorkerTransport.cs:112-126), parses with JsonDocument and then deserializes. ValidateEnvelopeIdentity (ProjectContextPayloadContract.cs:99-117) re-parses, but only for local/.amc21 or context-bearing identities. The protocol_error blocks at 119-124 and 149-154 are duplicates. The claim that HMI gets 'no check' is wrong: HmiWritePayloadContract.cs:19,35 validates result.HmiWriteOutcome, just on the leniently deserialized object. The VCI check runs at the envelope because the strict worker-payload reader needs the raw JSON element (required members, exact-case name). So a move must carry the raw element, which the proposal already covers.

### HOSTCORE-11 — Transport lifetime: unsynchronized Dispose, hand-rolled timeout race, client never disposes its semaphore

- **Verdict:** confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs:281-315 Dispose`
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs:89-100 Task.WhenAny(responseLineTask, Task.Delay(Infinite, cts.Token))`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:66 _bindingOperationGate, 2480-2487 Dispose`
- **Corrected locations:**
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs:44-71,89-100,281-315`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:66,2440-2452,2480-2487`
- **Evidence:** Dispose reads _process and disposes _gate without holding _gate. A SendAsync in flight then hits ObjectDisposedException in its finally Release. Because ObjectDisposedException derives from InvalidOperationException, InvokeWorkerAsync reports it as worker_crashed and invalidates the binding during shutdown. SendAsync has no _disposed check either. The timeout uses Task.Delay(InfiniteTimeSpan, token) inside WhenAny, while net10 already offers responseLineTask.WaitAsync(_requestTimeout). OpennessWorkerClient.Dispose never disposes _bindingOperationGate.
- **Proposal:** In Dispose, wait on _gate (with a bounded timeout) before tearing down. Have SendAsync throw ObjectDisposedException early when _disposed is set. Replace the WhenAny/Delay with `await responseLineTask.WaitAsync(_requestTimeout)` and catch TimeoutException → KillProcess. Dispose _bindingOperationGate in OpennessWorkerClient.Dispose.
- **Verifier-corrected proposal:** As proposed. The bounded wait in Dispose must stay well below the worker request timeout (5-60 min); a short wait followed by a kill is enough. Do not touch TiaPortalProcess.
- **Risk:** Only shutdown behaviour changes. Memory note: never dispose TiaPortalProcess. This finding concerns the worker child process and semaphores, not Portal.
- **Verifier notes:** PersistentWorkerTransport.Dispose (281-315) touches _process and calls _gate.Dispose() without taking _gate. SendAsync (44-71) has no _disposed check. A later WaitAsync or the in-flight finally Release throws ObjectDisposedException, which is an InvalidOperationException, so it lands in the catch at OpennessWorkerClient.cs:2440-2451 as worker_crashed and invalidates the binding. The timeout uses Task.WhenAny with Task.Delay(Infinite, token) (89-100). OpennessWorkerClient.Dispose (2480-2487) never disposes _bindingOperationGate (:66). Severity is low: an in-flight request during shutdown would fail anyway once the process is killed.

### HOSTCORE-12 — Cursor codecs share the HMAC sealer but each re-implements decode/error mapping, and they disagree on categories

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:10-18,45-64 (own exception, foreign→invalid_cursor, size not mapped in Encode)`
  - `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs:16-25,40-78 (foreign→snapshot_unavailable, size→invalid_cursor)`
  - `TiaMcpServer/Hmi/HmiPageCursorCodec.cs:18-28,44-79 (foreign→invalid_cursor via a thrown JsonException)`
  - `TiaMcpServer/Network/HardwarePageProjector.cs:63 catch AuthenticatedCursorSizeException`
  - `TiaMcpServer/Cursors/AuthenticatedCursorProtector.cs:9-13,101-176`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectCursorCodec.cs:29-80 (worker-issued, unsigned, hash-validated)`
- **Corrected locations:**
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:10-18,38-64`
  - `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs:16-25,40-79`
  - `TiaMcpServer/Hmi/HmiPageCursorCodec.cs:18-27,44-80`
  - `TiaMcpServer/Network/HardwarePageProjector.cs:63`
- **Evidence:** All three host codecs call _protector.Protect/Unprotect, so sealing is not duplicated. Each one does duplicate the try/catch filter (AuthenticatedCursorException or ArgumentException or JsonException), the Status check and a private exception class. The results differ. A cursor from a previous server process is snapshot_unavailable for browse_project_tree but invalid_cursor for hmi_read and for hardware pages. An oversized cursor is mapped inside the codec for tree/HMI but in HardwarePageProjector for hardware. list_network_objects cursors are issued by the worker and are neither HMAC-sealed nor process-scoped, unlike the other three.
- **Proposal:** Add one helper to AuthenticatedCursorProtector, e.g. `bool TryOpen<TState>(string purpose, string cursor, Action<TState> validate, out TState? state, out string category)`, plus a shared `TrySeal`. Use a single `CursorException(category)` in place of the three exception classes. Choose one foreign-process category for every tool (snapshot_unavailable is the more actionable one). Separately, decide whether the network-object cursor should be host-sealed like the others (it is in the Network reviewer's area).
- **Verifier-corrected proposal:** Deduplicate only. Add one shared helper on AuthenticatedCursorProtector, e.g. Open<TState>(purpose, cursor, validate, foreignProcessCategory) and Seal(purpose, state, validate), throwing a single CursorException(category). Each codec passes its documented foreign-process category: snapshot_unavailable for project-tree, invalid_cursor for HMI and hardware. Optionally move the AuthenticatedCursorSizeException mapping into the hardware codec's Encode (it is currently in HardwarePageProjector.cs:63) so all three behave alike. Unify categories only if the maintainer decides to, with doc updates.
- **Risk:** Changing a cursor error category is a public contract change: update tool docs (HMI_OPERATIONS_SUMMARY, project-tree docs) and the codec tests. No Openness involvement, so no live run is needed.
- **Verifier notes:** The duplicated plumbing is real: three exception classes (HardwarePageCursorCodec.cs:10-18, ProjectTreeCursorCodec.cs:16-25, HmiPageCursorCodec.cs:18-27) and near-identical try/catch filters (ProjectTreeCursorCodec.cs:40-78, HmiPageCursorCodec.cs:44-79, HardwarePageCursorCodec.cs:45-64). The category split, however, is documented per tool. Project tree uses snapshot_unavailable for a previous host process (docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md:188, docs/ARCHITECTURE.md:210). HMI uses invalid_cursor (HMI_OPERATIONS_SUMMARY.md:105). Hardware uses invalid_cursor 'intentionally' (docs/guides/troubleshooting.md:20, NETWORK_OPERATIONS_SUMMARY.md:185). The proposal to choose one category is therefore a public contract change against documented behaviour, not a defect fix. The network-object worker cursor is outside this area.

### HOSTCORE-13 — The 'no acknowledge guards' rule is copied in Network and PLC and missing from HMI and VCI

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Network/NetworkGuardDefinitions.cs:9-22 Validate`
  - `TiaMcpServer/Plc/PlcGuardDefinitions.cs:19,33-40 Validate`
  - `TiaMcpServer/Hmi/HmiGuardDefinitions.cs:10 Array.AsReadOnly`
  - `TiaMcpServer/Vci/VciGuardDefinitions.cs:13 Array.AsReadOnly`
  - `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs:96 ConfirmsEveryCall`
- **Corrected locations:**
  - `TiaMcpServer/Network/NetworkGuardDefinitions.cs:9-22`
  - `TiaMcpServer/Plc/PlcGuardDefinitions.cs:19-40`
  - `TiaMcpServer/Hmi/HmiGuardDefinitions.cs:10`
  - `TiaMcpServer/Vci/VciGuardDefinitions.cs:13`
- **Evidence:** Network and PLC each contain an identical Validate method that throws if any definition is Acknowledge and builds a throwaway WriteGuardCatalog. HMI and VCI, which AGENTS.md also describes as having no acknowledge guards, have no such check. NetworkGuardDefinitions also uses the literals "info"/"block" (lines 11-12), while the other domains use WriteGuardSeverities constants.
- **Proposal:** Add `WriteGuardCatalog.NonInteractive(IEnumerable<WriteGuardDefinition> defs, string domain)`, or enforce in the pipeline that a domain with ConfirmsEveryCall=false never fires an acknowledge guard. Use it for all four data-write domains. Replace the Network literals with WriteGuardSeverities.Info/Block.
- **Risk:** None at runtime, because no such guard exists today. Startup or type-init could throw if a future HMI/VCI guard is misclassified, which is the point of the check.
- **Verifier notes:** NetworkGuardDefinitions.Validate (NetworkGuardDefinitions.cs:15-22) and PlcGuardDefinitions.Validate (PlcGuardDefinitions.cs:33-40) are identical apart from the message. HmiGuardDefinitions.cs:10 and VciGuardDefinitions.cs:13 use a bare Array.AsReadOnly with no acknowledge check, although AGENTS.md says HMI/VCI have no acknowledge guards. NetworkGuardDefinitions.cs:11-12 uses the literals "info"/"block" while the other domains use WriteGuardSeverities constants.

### HOSTCORE-14 — Audit confirmation provenance uses bare string literals when constants already exist; a 12,000-char limit is duplicated across processes

- **Verdict:** confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:41 new("none","not_requested"), 269 new("policy",...), 284/294/297 new("user",...), 568 `_auditConfirmation.By == "policy"``
  - `TiaMcpServer/Safety/Pipeline/WriteVocabulary.cs:37-44 GuardSatisfactions.User/Policy`
  - `TiaMcpServer/Safety/Pipeline/UserConfirmation.cs:8-26 UserConfirmationOutcomes (no NotRequested)`
  - `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs:15 MaxSerializedChars = 12_000`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportOutcomeProjection.cs:14 MaxSerializedChars = 12_000`
- **Corrected locations:**
  - `TiaMcpServer/Safety/Pipeline/WriteRun.cs:41,269,284,294,297,568`
  - `TiaMcpServer/Safety/Pipeline/WriteVocabulary.cs:37-44`
  - `TiaMcpServer/Safety/Pipeline/UserConfirmation.cs:8-26`
  - `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs:15`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportOutcomeProjection.cs:14`
- **Evidence:** WriteAuditConfirmation.By is compared and set with literals that match GuardSatisfactions.Policy/User, and "none" and "not_requested" have no constant. Audit v2 is a persisted format, so drift would corrupt records without any error. The block-import outcome cap is declared separately in host and worker, and the host validator depends on both matching.
- **Proposal:** Add WriteAuditConfirmationSources {User, Policy, None} (or reuse GuardSatisfactions plus None) and UserConfirmationOutcomes.NotRequested, and use them in WriteRun. Move the 12,000 cap to a public const on BlockImportOutcomeInfo in TiaMcpServer.Contracts and reference it from both sides.
- **Risk:** None. The values stay the same.
- **Verifier notes:** WriteRun.cs uses literals: :41 new("none","not_requested"), :269 new("policy",...), :284/294/297 new("user",...), and :568 _auditConfirmation.By == "policy". GuardSatisfactions.Policy/User already exist (WriteVocabulary.cs). UserConfirmationOutcomes has no NotRequested (UserConfirmation.cs:8-26). MaxSerializedChars = 12_000 is declared in both BlockImportOutcomeValidator.cs:15 and BlockImportOutcomeProjection.cs:14, and the tests repeat 12_000 (BlockImportOutcomeInfoTests.cs:390).

### HOSTCORE-15 — OpennessWorkerClient is a 2,557-line class mixing binding, lifecycle, transport mapping and ~40 near-identical per-operation wrappers

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** L · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:56-2554 OpennessWorkerClient`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1260-1520 PLC tag/constant/block wrappers`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1231-1245 WriteVciAsync/WriteHmiAsync (single typed request)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:93-169 InspectPortalAsync, 178-291 BindOpenProjectCoreAsync`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:53-64`
- **Corrected locations:**
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:56-2557`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:50-66`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1231-1245`
  - `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:87-88`
- **Evidence:** Each PLC wrapper is an 11–35-line SendBoundProjectRequestAsync call that copies arguments onto WorkerRequest and re-sets Confirm/AllowTiaConfirmations. PlcWorkerInvoker is a switch that only fans out to them. HMI and VCI already pass one typed request object through a single method (WriteHmiAsync(method, HmiWriteRequestInfo)). BindOpenProjectCoreAsync (113 lines, with 4 local functions) and InspectPortalAsync (77 lines) are long and deeply nested.
- **Proposal:** Short term, with no contract change: split the class into partial files by concern (Binding/Inspection, Lifecycle, Network, Plc, InvokeWorker/warnings). Longer term, give PLC writes a typed PlcWriteRequestInfo like HMI/VCI, which removes the per-operation wrappers and the invoker fan-out.
- **Risk:** Partial-file split is mechanical. Note: the test csproj links host files individually, so new partial files must be added to TiaMcpServer.Tests.csproj. The typed PLC request changes the worker contract and needs PLC write regression tests, plus a live smoke run of plc_write.
- **Verifier notes:** OpennessWorkerClient.cs is 2,557 lines (wc -l). PlcWorkerInvoker.cs:50-66 is a pure switch fanning out to the per-operation client wrappers. HMI and VCI each pass one typed request (OpennessWorkerClient.cs:1231-1245). The test csproj links the file individually (TiaMcpServer.Tests.csproj:87-88), so new partial files must be added there too, as the finding says.

## HOSTCLI

The area is small (about 4.3k lines) and well tested. The main debt is the same "--flag value | --flag=value, at most once" parsing written four or more times, even though StartupOptionReader.ReadSingle exists. That copying has caused real drift. Doctor treats access-mode and project-path rules differently from the server and from bind_project. The installer works out launch arguments it never uses, so the MiMoCode guide leaves out --tia-project. The installer also keeps unreachable MiMoCode/--json branches, and its four client classes differ only in data. Diagnostics wrappers swallow errors and show them as misleading check results. The publish workflow has no test gate and does not use -m:1.

### HOSTCLI-1 — Doctor silently falls back to read-write on an invalid TIA_MCP_ACCESS_MODE that makes the server refuse to start

- **Verdict:** confirmed · **Severity:** high · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/DoctorCliParser.cs:37-40 Parse (hasCliAccessMode branch)`
  - `TiaMcpServer/Cli/DoctorCliParser.cs:154-170 IsAccessModeArgument/ResolveAccessMode`
  - `TiaMcpServer/Cli/AccessModeParser.cs:11 EnvVarName (private)`
  - `TiaMcpServer/Cli/AccessModeParser.cs:106-111 Parse env branch`
  - `TiaMcpServer/Cli/WorkerTimeoutParser.cs:32 Parse(args, getEnvironmentVariable)`
  - `TiaMcpServer/Cli/PayloadBudgetParser.cs:21 Parse(args, getEnvironmentVariable)`
- **Evidence:** With TIA_MCP_ACCESS_MODE=bogus, AccessModeParser.Parse returns ParseValue(envValue), which is IsValid=false, so Program.Main exits 1. DoctorCliParser.ResolveAccessMode keeps the env value only when `ParseValue(...) is { IsValid: true }` and otherwise returns McpAccessMode.ReadWrite. Doctor therefore prints read-write diagnostics, exit 0 or 1, for a configuration the server rejects. Doctor needed its own copy because AccessModeParser.Parse cannot take an injected env getter, unlike WorkerTimeoutParser and PayloadBudgetParser. As a result the env var name is written twice: a private const at AccessModeParser:11 and a literal at DoctorCliParser:162.
- **Proposal:** Add `Func<string,string?>? getEnvironmentVariable = null` to AccessModeParser.Parse to match the sibling parsers, and make EnvVarName a public const EnvVar. In DoctorCliParser, call AccessModeParser.Parse(args, getEnvironmentVariable) every time and return its error. Delete IsAccessModeArgument and ResolveAccessMode.
- **Risk:** Behavior change: doctor now exits 2 on an invalid env access mode. That matches the server and is the intended trust-boundary behavior. Update or add a DoctorCliParser test; no live verification needed.
- **Verifier notes:** DoctorCliParser.cs:37-40 uses ResolveAccessMode (160-170), which returns ReadWrite whenever ParseValue(env) is not valid. AccessModeParser.cs:106-111 returns the invalid result, and Program.cs:47-52 exits 1 on it. Doctor is also inconsistent with itself: for an invalid worker-timeout env it returns the shared error (DoctorCliParserTests.cs:50-53, through WorkerTimeoutParser.Parse(args, getEnv)), but for an invalid access-mode env it quietly falls back. The env var string appears twice, at AccessModeParser.cs:11 (private) and DoctorCliParser.cs:162. No test asserts the fallback, and Program's call site keeps working if the env getter parameter is optional.

### HOSTCLI-2 — Startup, doctor and bind_project apply three different rules to a configured project path

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer/Program.cs:142-194 TryResolveStartupProjectPath`
  - `TiaMcpServer/Program.cs:192 literal "TIA_MCP_PROJECT_PATH"`
  - `TiaMcpServer.Contracts/Project/ProjectPathNormalization.cs:21-40 Canonicalize`
  - `TiaMcpServer/Diagnostics/Checks/ProjectBindingCheck.cs:8 ProjectEnvVar, :60-80 absolute/.ap21 checks`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:234-239 BindOpenProjectAsync asserted-path validation`
- **Corrected locations:**
  - `TiaMcpServer/Program.cs:56-60,142-194`
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:19-25`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:232-239`
  - `TiaMcpServer/Diagnostics/Checks/ProjectBindingCheck.cs:8`
  - `docs/guides/installation.md:107,133-136`
- **Evidence:** At startup Program accepts any --project or env value without validation. ProjectSessionBinding then canonicalizes it with Path.GetFullPath, so a relative path is silently resolved against whatever working directory the MCP client launched the server in. bind_project rejects that later with 'projectPath must be an absolute .ap21 or .amc21 engineering path'. Doctor's ProjectBindingCheck accepts only `.ap21`, so it reports Failed for a `.amc21` local-session path that bind_project accepts. The env var name is also written twice (Program:192 literal, ProjectBindingCheck:8 const).
- **Proposal:** Move one rule, `ConfiguredProjectPathRules.Validate(path) -> string? error` (fully qualified; extension .ap21 or .amc21), into Contracts/Project next to ProjectPathNormalization. Call it from Program after the path is resolved (fail fast with exit 1), from ProjectBindingCheck in place of lines 60-80, and from OpennessWorkerClient:234-239. Expose one ProjectEnvVar constant.
- **Verifier-corrected proposal:** Add one shared check, Validate(path) -> error (fully qualified, extension .ap21 or .amc21), in Contracts/Project. Call it from Program right after TryResolveStartupProjectPath (exit 1 on error) and from OpennessWorkerClient.cs:234-239. Expose a single ProjectEnvVar constant for Program and ProjectBindingCheck. Leave doctor's .ap21-only file check alone (documented). If it is widened anyway, update installation.md:107,133-136 and DoctorCommand usage text in the same change. Startup becoming stricter is a contract change and needs a release note.
- **Risk:** Startup becomes stricter: a relative or wrong-extension --project now stops the server instead of failing at the first bind. That is a contract change that needs a release note. Whether `.amc21` is meant to be a supported startup --project is inferred from the bind validation, so confirm with the maintainer before widening doctor's rule. The doctor tests for .ap21 messages need updating.
- **Verifier notes:** Confirmed: Program.cs:142-194 does not validate --project or the env value. ProjectSessionBinding.cs:21-22 canonicalizes it with Path.GetFullPath, so a relative path resolves against the client's working directory. The env name is duplicated at Program.cs:192 and ProjectBindingCheck.cs:8. Refuted parts: (a) bind_project does NOT reject the relative path later. The configured path is already absolute after canonicalization, so the assertedPath check at OpennessWorkerClient.cs:232-239 passes the CWD-resolved path, which is worse than the finding says. (b) Doctor accepting only .ap21 is documented as deliberate in docs/guides/installation.md:107,133-136 ('Doctor's file-binding diagnostic still accepts only existing .ap21 files ... Doctor does not validate them'), and DoctorCommand.cs:15 says the same. It is not an undocumented inconsistency.

### HOSTCLI-3 — The --flag value / --flag=value parsing is copied four or more times instead of using StartupOptionReader.ReadSingle

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/StartupOptionReader.cs:5-35 ReadSingle`
  - `TiaMcpServer/Program.cs:142-194 TryResolveStartupProjectPath`
  - `TiaMcpServer/Cli/DoctorCliParser.cs:108-143 --project handling`
  - `TiaMcpServer/Cli/AccessModeParser.cs:59-98 --access-mode handling`
  - `TiaMcpServer/Cli/Install/InstallCliParser.cs:52-146 --name/--access-mode/--tia-project/--server-path`
  - `TiaMcpServer/Program.cs:23-140 Main (option parsing plus DI in a ~120-line method)`
- **Evidence:** ReadSingle already handles both forms, empty values and 'may be specified only once', and WorkerTimeoutParser and PayloadBudgetParser use it. TryResolveStartupProjectPath and DoctorCliParser repeat the same --project logic almost line for line. The copies have drifted: InstallCliParser does not reject repeated flags (`--name a --name b` means last one wins), while every other parser rejects repeats. The doctor --project error paths are duplicated twice inside one method (lines 114/131, 119/137).
- **Proposal:** Use StartupOptionReader.ReadSingle(args, "--project") in Program and DoctorCliParser, and delete TryResolveStartupProjectPath. Add a small `StartupOptions.Parse(args, env)` returning access mode, project path, budgets and timeout, used by both Program.Main and DoctorCliParser, so doctor reports the same view the server would have. In InstallCliParser, read each value flag with ReadSingle first and keep the loop only to reject unknown arguments and take the positional client.
- **Risk:** Install gains duplicate-flag rejection, a small behavior change. Error message text stays the same if ReadSingle's wording is kept. The many existing parser tests cover this; no live verification needed.
- **Verifier notes:** StartupOptionReader.cs:5-35 ReadSingle gives the same messages ('requires a value', 'may be specified only once') and the same handling of empty values and '--'-prefixed next args as Program.cs:142-194 and DoctorCliParser.cs:108-143. DoctorCliParser repeats its own error returns (114/131, 119/137). InstallCliParser.cs:52-146 assigns repeated --name/--access-mode/--tia-project/--server-path without rejecting them (last one wins), unlike every other parser. The tests compile TiaMcpServer/Cli/*.cs and Cli/Install/*.cs (TiaMcpServer.Tests.csproj:97-99), so the internal ReadSingle is reachable from all callers.

### HOSTCLI-4 — McpLaunchSpec.Arguments is built but never read, and the MiMoCode guide drops --tia-project

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:123-130 launchArgs/McpLaunchSpec`
  - `TiaMcpServer/Cli/Install/McpLaunchSpec.cs:3-6 Arguments`
  - `TiaMcpServer/Cli/Install/ClaudeCodeInstaller.cs:24-33 BuildInstallCommand`
  - `TiaMcpServer/Cli/Install/CodexInstaller.cs:24-33 BuildInstallCommand`
  - `TiaMcpServer/Cli/Install/OpenCodeInstaller.cs:24-33 BuildInstallCommand`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:393-397 PrintInteractiveGuide`
- **Corrected locations:**
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:123-130,393-397`
  - `TiaMcpServer/Cli/Install/ClaudeCodeInstaller.cs:19-36`
  - `TiaMcpServer/Cli/Install/CodexInstaller.cs:19-36`
  - `TiaMcpServer/Cli/Install/OpenCodeInstaller.cs:19-36`
  - `TiaMcpServer.Tests/Cli/ClientInstallerTests.cs:8-17,47-53,114-119`
- **Evidence:** No `spec.Arguments` read exists in production or tests (checked with grep). Each of the three non-interactive installers rebuilds `--access-mode <mode> [--project <path>]` from options. The MiMoCode interactive guide prints only `Server args: --access-mode {options.AccessMode}`. So `tia-mcp install mimo --tia-project C:\x.ap21` tells the user to register the server without the requested project binding.
- **Proposal:** Have the installers append `spec.Arguments` after `spec.ExecutablePath` instead of rebuilding them. Print `string.Join(" ", spec.Arguments)` (quoted) in PrintInteractiveGuide. This leaves one place that decides the server launch arguments.
- **Verifier-corrected proposal:** Make the installers append spec.Arguments after spec.ExecutablePath, and print the quoted spec.Arguments in PrintInteractiveGuide. Update ClientInstallerTests so CreateSpec builds its arguments from the same access mode and project as CreateOptions (or assert on the spec). Keep the PrintInteractiveGuide signature or update the reflection test.
- **Risk:** Fixes a user-visible omission for MiMoCode. The non-interactive outputs stay identical, so the ClientInstallerTests argument assertions still pass.
- **Verifier notes:** Confirmed: McpLaunchSpec.Arguments (built at InstallCommand.cs:123-130) is never read in production. No 'spec.Arguments' or '.Arguments' read on a McpLaunchSpec exists. All three installers rebuild the arguments from options (ClaudeCodeInstaller.cs:24-35, CodexInstaller.cs:24-35, OpenCodeInstaller.cs:24-35). The MiMoCode guide at InstallCommand.cs:396 drops --project. Refuted risk claim: ClientInstallerTests do NOT keep passing. CreateSpec() (ClientInstallerTests.cs:8-17) always builds '--access-mode read-only' with no project, while tests pass CreateOptions("read-write") or a project and assert on them (lines 47-53, 77-78, 114-119, 131-132, 172). Switching to spec.Arguments breaks those tests until they build a consistent spec. PrintInteractiveGuide is also invoked by reflection in ExecutableResolutionCoverageTests.cs:327-345.

### HOSTCLI-5 — Four installer classes, an interface and a registry hold data, not behavior, and carry unused parameters

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/Install/IMcpClientInstaller.cs:3-17`
  - `TiaMcpServer/Cli/Install/ClaudeCodeInstaller.cs:7-41`
  - `TiaMcpServer/Cli/Install/CodexInstaller.cs:7-41`
  - `TiaMcpServer/Cli/Install/OpenCodeInstaller.cs:7-41`
  - `TiaMcpServer/Cli/Install/MiMoCodeInstaller.cs:7-33`
  - `TiaMcpServer/Cli/Install/ClientInstallerRegistry.cs:5-21`
  - `TiaMcpServer/Cli/Install/ClientAliasResolver.cs:5-37`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:405-421 FormatClientName/GetClientCommand`
- **Evidence:** The installers differ only in: command name, extra add args (`--scope user`), verify args and the Interactive flag. DetectAsync is the same in all four apart from strings, returns Task.FromResult, and ignores cancellationToken. BuildInstallCommand's `resolveClientExe` parameter is unused in all four, and BuildVerificationCommand's `options` is unused in all four. `IMcpClientInstaller.Client` is read only by tests (ClientInstallerTests.cs:252-281). The client display name, command name and aliases are also repeated in InstallCommand's two switches and in ClientAliasResolver.
- **Proposal:** Replace the interface, the four classes, the registry and the alias resolver with one `ClientProfile` record (Kind, DisplayName, Command, Aliases, AddArgsPrefix, VerifyArgs, Interactive) in a static table. Write one function that builds the NativeCommand from the profile plus spec, and one detection helper that is synchronous. InstallCommand looks up the profile once and stops using its switches.
- **Risk:** Internal types only; no CLI contract change. ClientInstallerTests and InstallCliParserTests are written against the classes and must be ported to the table, so expect sizeable test churn. Run a dry run per client to verify.
- **Verifier notes:** The four installer classes (ClaudeCode/Codex/OpenCode/MiMoCode *Installer.cs) differ only in strings, the '--scope user' prefix, verify args and the Interactive flag. DetectAsync wraps synchronous code in Task.FromResult and ignores the CancellationToken. The resolveClientExe parameter of BuildInstallCommand and the options parameter of BuildVerificationCommand are unused in all four. IMcpClientInstaller.Client is read only by ClientInstallerTests.cs:252-281. Name, command and alias data are repeated in ClientAliasResolver.cs:14-33 and InstallCommand.cs:405-421. Additional dead code found: TiaMcpServer/Cli/Install/InstallResult.cs has no references anywhere in production or tests, so it can be deleted with this change. Expect heavy test churn (ClientInstallerTests, reflection-based coverage tests).

### HOSTCLI-6 — Unreachable MiMoCode+--json branch, exit codes chosen by matching error text, and a stale access-mode error message

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/Install/InstallCliParser.cs:181-195 MiMoCode+json rejection`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:155-161 duplicate MiMoCode+json rejection`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:81-93 ParseError.Contains exit-code mapping`
  - `TiaMcpServer/Cli/Install/InstallCliParser.cs:82,94 '--access-mode requires a value. Valid values: read-only, read-write'`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:359-378 ResolveInstallExecutable`
- **Evidence:** The parser already returns Valid=false for MiMoCode with --json, so the check at InstallCommand:155 cannot be reached. Exit codes 8 and 3 are picked by substring checks on the error message, and `Contains("does not support --json")` is redundant next to `Contains("does not support")`. The missing-value message lists only read-only and read-write, yet line 174 accepts 'full' and line 176 lists all three. In ResolveInstallExecutable, the comment for the different-executable branch says 'MiMoCode uses claude for install', but every installer now uses the same executable for detection and install, so that branch is only reachable from tests.
- **Proposal:** Add an `InstallParseError` kind or exit code to InstallOptions and map it to the exit code directly. Delete InstallCommand:155-161. Correct the message to list read-only, read-write and full. Shrink ResolveInstallExecutable to reuse the detection result (or remove it if HOSTCLI-5 lands) and drop the stale comment.
- **Risk:** Exit codes and messages are part of the CLI contract. Keep the numbers unchanged; only the error text for the missing-value case changes.
- **Verifier notes:** InstallCliParser.cs:181-195 returns Valid=false for MiMoCode with --json. InstallCommand.cs:65-94 returns before line 155, so lines 155-161 are unreachable. At InstallCommand.cs:82-83, Contains("does not support --json") is already covered by Contains("does not support"). The missing-value message at InstallCliParser.cs:82,94 lists only read-only and read-write, while line 174 accepts 'full' and line 176 lists all three. ResolveInstallExecutable at InstallCommand.cs:359-378 has a stale 'MiMoCode uses claude' comment; MiMoCodeInstaller builds 'mimo' for both detection and install. Its fallback branch is exercised only by the reflection test ExecutableResolutionCoverageTests.cs:286-289. Risk note: shrinking or removing ResolveInstallExecutable breaks the reflection-based tests at EnvironmentFallbackCoverageTests.cs:113-116 and ExecutableResolutionCoverageTests.cs:286-289, so those must be rewritten together with it.

### HOSTCLI-7 — COMSPEC lookup copied three times; FormatCommand re-implements NativeProcessRunner.BuildProcessArgs

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:209-218 dry-run execution method`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:337-357 FormatCommand`
  - `TiaMcpServer/Cli/Install/NativeProcessRunner.cs:61-80 BuildProcessArgs`
- **Evidence:** `Environment.GetEnvironmentVariable("COMSPEC") ?? Path.Combine(SpecialFolder.System, "cmd.exe")` is repeated verbatim at InstallCommand:211, InstallCommand:344 and NativeProcessRunner:68, together with the `/d /s /c` wrapping. FormatCommand is BuildProcessArgs with Path.GetFileName applied to the first token.
- **Proposal:** Implement FormatCommand as `var (f,a)=NativeProcessRunner.BuildProcessArgs(cmd); return [Path.GetFileName(f), ..a]` when the kind is not Native, otherwise `[f, ..a]`. Base the dry-run 'Execution method' line on the same result. Delete the other two COMSPEC copies.
- **Risk:** None at the contract level. Dry-run output stays identical; the EnvironmentFallbackCoverageTests for COMSPEC still exercise the single path.
- **Verifier notes:** The COMSPEC ?? System\cmd.exe expression appears at InstallCommand.cs:211-214, InstallCommand.cs:344-347 and NativeProcessRunner.cs:68-71. FormatCommand (InstallCommand.cs:337-357) equals BuildProcessArgs (NativeProcessRunner.cs:61-80) except that Path.GetFileName is applied to cmd.exe for script kinds. ExecutableKind has only Native, CommandScript and BatchScript, so 'kind != Native' matches the existing check. The dry-run check at line 209 (!= Native) and FormatCommand at line 342 (== Cmd||Bat) express the same condition differently. Keep the FormatCommand name and signature, because EnvironmentFallbackCoverageTests.cs:93-94 invokes it by reflection.

### HOSTCLI-8 — ExecutableResolver: a legacy method used only by tests, duplicate where.exe runners, and an unkilled process on timeout

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/Install/ExecutableResolver.cs:85-92 FindClientExecutable`
  - `TiaMcpServer/Cli/Install/ExecutableResolver.cs:221-256 RunWhere`
  - `TiaMcpServer/Cli/Install/ExecutableResolver.cs:261-295 RunWhereAll`
  - `TiaMcpServer/Cli/Install/ExecutableResolver.cs:12-20 ResolveServerExecutable explicit path`
  - `TiaMcpServer/Cli/Install/InstallCommand.cs:103 not-found message`
- **Evidence:** FindClientExecutable is documented as 'Legacy method for backward compatibility', and its only callers are ExecutableResolverTests.cs:215-225. RunWhere is RunWhereAll(x).FirstOrDefault() without RunWhereAll's .exe preference. Both call WaitForExit(5000) and then read ExitCode, which throws if where.exe is still running. The exception is swallowed by a bare catch and the child process is never killed. RunWhereAll uses `startProcess(psi)!` and relies on the catch to absorb the null. When an explicit --server-path does not exist, the user is told 'Install it with: dotnet tool install -g TiaMcpServer' instead of being told the given path does not exist.
- **Proposal:** Delete FindClientExecutable and its two tests. Replace RunWhere with `RunWhereAll("tia-mcp").OrderBy(ExtensionPriority).FirstOrDefault()`. In RunWhereAll, handle a null process explicitly, and when WaitForExit times out, Kill(entireProcessTree) and return empty. Return a distinct 'server path not found: <path>' error when --server-path was given.
- **Risk:** Small behavior change: server resolution now prefers .exe over a .cmd shim when where.exe returns several matches. Low risk; only unit tests are affected.
- **Verifier notes:** FindClientExecutable (ExecutableResolver.cs:85-92) is referenced only by ExecutableResolverTests.cs:212-225. It is public on an internal static class, so there is no external backward-compatibility consumer to preserve. RunWhere (221-256) duplicates RunWhereAll (258-295) and lacks the ExtensionPriority ordering used at 132-135. RunWhereAll reads ExitCode at 279-280 right after a WaitForExit(5000) that may have timed out. That throws InvalidOperationException, which the bare catch swallows, and the child is never killed. 'startProcess(psi)!' is null-forgiven at 278. ResolveServerExecutable returns null for a missing explicit --server-path (12-20), and InstallCommand.cs:103 then gives generic dotnet-tool-install advice. Minor correction: RunWhere reads stdout before ExitCode, so its throw window is smaller than RunWhereAll's.

### HOSTCLI-9 — Diagnostics services swallow exceptions, and the checks report them as the wrong failure

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Diagnostics/WindowsIdentityService.cs:72-79 CheckGroupMembership catch(Exception)`
  - `TiaMcpServer/Diagnostics/IWindowsIdentityService.cs:5-9 OpennessGroupMembership.ErrorMessage`
  - `TiaMcpServer/Diagnostics/Checks/OpennessGroupCheck.cs:41-55`
  - `TiaMcpServer/Diagnostics/WindowsIdentityService.cs:117-124 redundant outer catches`
  - `TiaMcpServer/Diagnostics/ProcessEnumerationService.cs:36-39 catch-all`
  - `TiaMcpServer/Diagnostics/Checks/TiaPortalProcessCheck.cs:93-99`
  - `TiaMcpServer/Diagnostics/ApplicationInfoService.cs:104-107 bare catch`
- **Corrected locations:**
  - `TiaMcpServer/Diagnostics/WindowsIdentityService.cs:53-61,72-79,105-124`
  - `TiaMcpServer/Diagnostics/IWindowsIdentityService.cs:5-9`
  - `TiaMcpServer/Diagnostics/Checks/OpennessGroupCheck.cs:41-55`
  - `TiaMcpServer/Diagnostics/ProcessEnumerationService.cs:36-39`
- **Evidence:** Any exception in CheckGroupMembership, such as an access-denied error from IsInRole, becomes GroupExists:false. OpennessGroupCheck then reports 'The local group "Siemens TIA Openness" does not exist' with the remediation 'Install TIA Portal V21 with the Openness option'. ErrorMessage is filled in but no production code reads it; only tests construct it. A failed Process.GetProcesses() likewise becomes 'No running TIA Portal instance was detected.' In TryResolveGroupSid, the outer `catch (IdentityNotMappedException)` is redundant because the following `catch (SystemException)` already covers it.
- **Proposal:** Add `evidence["error"] = membership.ErrorMessage`. When ErrorMessage is set but the group was not resolved as absent, return Warning 'Group membership could not be verified: <msg>' instead of the 'does not exist' failure. Have ListProcesses return null or an error so TiaPortalProcessCheck can warn 'process enumeration failed'. Drop the redundant catch clauses.
- **Verifier-corrected proposal:** Add the error to the evidence (evidence["error"] = membership.ErrorMessage) and give OpennessGroupMembership an explicit 'could not verify' state. Return GroupExists:false only when Translate threw IdentityNotMappedException for both forms; for any other exception return Warning 'Group membership could not be verified: <msg>'. Delete the redundant IdentityNotMappedException/outer catch clauses. Optionally make the process check say 'process enumeration failed'. Leave ApplicationInfoService alone. Add one OpennessGroupCheck test for the error path.
- **Risk:** Doctor output changes only in error paths: Failed becomes Warning for verification errors. Add one OpennessGroupCheck test for the ErrorMessage path.
- **Verifier notes:** Confirmed for group membership: WindowsIdentityService.cs:72-79 turns any exception into GroupExists:false. Separately, TryResolveGroupSid's inner catch (SystemException) at 109-112 turns any Translate failure (not just IdentityNotMapped) into null, which becomes 'does not exist' at 54-61. OpennessGroupCheck.cs:41-55 (file lines ~186-198) then reports 'does not exist' with the remediation to install TIA. ErrorMessage is never read in production (grep shows only constructions). Both the inner IdentityNotMappedException catch (105-108) and the outer one (117-120) are redundant next to catch (SystemException). Overreach: ApplicationInfoService.cs:104-107 is a commented, intentional registry fallback to a build-number heuristic and does not misreport, so drop it. ProcessEnumerationService.cs:36-39 failing only changes the Warning message; the status stays Warning (TiaPortalProcessCheck.cs:93-99), so it is low value.

### HOSTCLI-10 — Doctor checks repeat the same discovery and report one root cause several times

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Diagnostics/Checks/TiaPortalInstallationCheck.cs:26 Locate, :29-40 evidence`
  - `TiaMcpServer/Diagnostics/Checks/OpennessAssembliesCheck.cs:26-39 Locate + 'No TIA Portal V21 installation was found'`
  - `TiaMcpServer/Diagnostics/Checks/OpennessWorkerCheck.cs:37,61 Locate/GetFileVersion`
  - `TiaMcpServer/Diagnostics/Checks/HostWorkerVersionCheck.cs:25-37 Locate/GetFileVersion`
  - `TiaMcpServer/Cli/DoctorCommand.cs:91-121 BuildRunner`
- **Corrected locations:**
  - `TiaMcpServer/Diagnostics/Checks/TiaPortalInstallationCheck.cs:29-40`
  - `TiaMcpServer/Diagnostics/Checks/OpennessAssembliesCheck.cs:26-39`
- **Evidence:** TiaPortalInstallationLocator.Locate, which reads the registry and file system, runs in two checks, and both fail when no installation exists. OpennessWorkerLocator.Locate and GetFileVersion run in two checks as well. In TiaPortalInstallationCheck the evidence keys duplicate each other: `candidate[i].path` and `candidate[i].apiPath` both hold candidate.Path, and `directoryPresent` and `apiDirectoryPresent` both hold candidate.DirectoryPresent.
- **Proposal:** In BuildRunner, locate the installation and the worker once and pass the results into the checks through their constructors. When the installation is missing, OpennessAssembliesCheck reports Warning 'skipped: no installation' instead of a second Failed. Remove the duplicated evidence keys, or keep them if an external consumer is known to depend on them.
- **Verifier-corrected proposal:** Remove only the duplicate candidate[i].apiPath and candidate[i].apiDirectoryPresent evidence keys. Optionally have OpennessAssembliesCheck return Warning 'skipped: no TIA Portal installation' instead of a second Failed. Do not restructure BuildRunner or the check constructors.
- **Risk:** The verbose evidence keys and the failed count in doctor JSON change, which matters if scripts parse them. The check-level tests need their constructors adjusted.
- **Verifier notes:** Confirmed: TiaPortalInstallationCheck writes duplicate evidence keys, candidate[i].path and .apiPath both from candidate.Path, and directoryPresent and apiDirectoryPresent both from candidate.DirectoryPresent (TiaPortalInstallationCheck.cs Run, file lines ~32-38). No test or doc references the candidate[i].apiPath or candidate[i].apiDirectoryPresent keys; OpennessAssembliesCheckTests.cs:66 asserts a different, top-level apiDirectoryPresent key in OpennessAssembliesCheck. Overreach: calling TiaPortalInstallationLocator/OpennessWorkerLocator again is cheap registry and file probing, so pre-computing results and injecting them through constructors is premature optimization. Each check reporting its own failure (HostWorkerVersionCheck already degrades to Warning) is a defensible design and needs no restructuring.

### HOSTCLI-11 — Doctor and server accept different option sets; removed-option guidance is only on the server path

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Cli/DoctorCliParser.cs:145 'Unknown doctor argument'`
  - `TiaMcpServer/Cli/DoctorCliParser.cs:32,64-68 json flag computed twice`
  - `TiaMcpServer/Program.cs:41-45 RemovedCliOptions.TryGetError (server only)`
  - `TiaMcpServer/Cli/Install/InstallCliParser.cs:46-50 --json detected mid-loop`
  - `TiaMcpServer/Cli/DoctorCommand.cs:28-89 RunAsync (synchronous, Task.FromResult)`
- **Evidence:** Doctor accepts --worker-timeout-minutes but exits 2 on --max-item-chars and --max-document-chars. Not reporting budgets is documented in docs/guides/mcp-client-configuration.md, but rejecting the flags means args copied from a client config fail. `doctor --confirm-with-user` gets 'Unknown doctor argument' instead of the migration text in RemovedCliOptions. Doctor detects --json upfront with Any(), while install detects it partway through the loop, so `install --name --json` prints a text error. DoctorCommand.RunAsync and the installer DetectAsync methods are synchronous code wrapped in Task.FromResult.
- **Proposal:** In DoctorCliParser, call RemovedCliOptions.TryGetError, and validate (and optionally report) budgets through PayloadBudgetParser.Parse; this falls out of HOSTCLI-3's shared StartupOptions. Detect --json upfront in InstallCliParser as doctor does, and remove the duplicate assignment at DoctorCliParser:64-68.
- **Risk:** If doctor starts reporting budgets, the docs line saying it does not needs updating in the same change.
- **Verifier notes:** DoctorCliParser.cs:60-146 has no branch for --max-item-chars or --max-document-chars, so both hit 'Unknown doctor argument' (145). docs/guides/mcp-client-configuration.md:153 says only that doctor does not report these settings, not that it rejects them. RemovedCliOptions.TryGetError is called only from Program.cs:41-45. Doctor's tests cover only the server and HostArgumentFilter (RemovedOptionTests, Phase1bCliModeTests.cs:57-65), not doctor. json is computed at DoctorCliParser.cs:32 and again at 64-68. InstallCliParser.cs:46-50 sets json inside the loop, so 'install --name --json' gets json=false in Invalid(). DoctorCommand.RunAsync (28-89) is synchronous code wrapped in Task.FromResult.

### HOSTCLI-12 — Publish workflow ships to NuGet without tests, without -m:1, and interpolates inputs into script text

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `.github/workflows/publish.yml:83-84 restore`
  - `.github/workflows/publish.yml:111-112 pack`
  - `.github/workflows/publish.yml:133-137 standalone publish/zip`
  - `.github/workflows/publish.yml:89-96 ${{ github.ref_name }}/${{ inputs.version }} in pwsh`
  - `.github/workflows/ci.yml:26,35,38 -m:1 + tests + coverage`
- **Corrected locations:**
  - `.github/workflows/publish.yml:31-32`
  - `.github/workflows/publish.yml:34-57`
  - `.github/workflows/publish.yml:59-60`
  - `.github/workflows/publish.yml:81-85`
  - `.github/workflows/ci.yml:6-10,26-38`
- **Evidence:** Pushing a 'v*' tag, or a workflow_dispatch, packs and pushes to nuget.org with no `dotnet test` step and no dependency on CI. Restore, pack and publish run without -m:1, which AGENTS.md says is required to avoid parallel worker build conflicts. The standalone single-file zip is never checked; verify-doctor-package.ps1 inspects only the .nupkg. Version strings are placed into the pwsh script with ${{ }} instead of being passed through env.
- **Proposal:** Add `dotnet build -m:1 /p:UseTiaPortalReferenceStubs=true` and `dotnet test` before pack, or turn ci.yml into a reusable workflow and add it as a `needs:` dependency. Add -m:1 to restore, pack and publish. Pass the tag and input through `env:` and read $env:RELEASE_TAG. Optionally assert that openness-worker/ exists in publish-bin before zipping.
- **Verifier-corrected proposal:** Before pack, add 'dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true' and 'dotnet test', or make ci.yml reusable (workflow_call) and add it as a needs: dependency. Add -m:1 to restore, pack and publish. Pass github.ref_name and inputs.version through env: and read them as $env:. Optionally assert that publish-bin/openness-worker exists before zipping.
- **Risk:** Release time goes up. Not verifiable offline; check on the next tag or with a dry-run workflow_dispatch to a test feed.
- **Verifier notes:** The substance holds, but the cited line numbers are wrong. publish.yml: restore without -m:1 at 31-32; ${{ github.ref_type }}/${{ github.ref_name }}/${{ inputs.version }} interpolated into pwsh at 38-41; pack without -m:1 and without tests at 59-60; standalone publish at 81-82; zip at 84-85. There is no dotnet test step. ci.yml:6-10 triggers only on push to main and on PRs, not on tags, so a 'v*' tag on an untested commit publishes to nuget.org. verify-doctor-package.ps1 (publish.yml:62-66) checks only the .nupkg. Injection risk is low (workflow_dispatch needs write access, and the version regex runs after interpolation), but passing values through env is still the right practice.

### HOSTCLI-13 — Worker companion-file list is kept in three places by hand

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Diagnostics/Checks/OpennessWorkerCheck.cs:7-21 RequiredCompanionFiles`
  - `scripts/verify-doctor-package.ps1:66-82 $requiredFiles`
  - `TiaMcpServer.Tests/Diagnostics/OpennessWorkerCheckTests.cs:9-23 private RequiredCompanionFiles copy`
- **Evidence:** The same 12 file names appear in the doctor check, in the package-verification script (plus the exe and pdbs), and in a private copy in the tests. OpennessWorkerCheck.RequiredCompanionFiles is already `internal static readonly`, so the test could reference it. Nothing cross-checks the script against the check, so adding a worker dependency to only one of them goes unnoticed until a live doctor run.
- **Proposal:** Have OpennessWorkerCheckTests use OpennessWorkerCheck.RequiredCompanionFiles. Add one test to DoctorPackageVerificationScriptTests that parses $requiredFiles from the script and asserts it is a superset of RequiredCompanionFiles.
- **Risk:** Test-only change.
- **Verifier notes:** The same 12 names appear in OpennessWorkerCheck.cs:7-21 (internal static readonly RequiredCompanionFiles), in a private copy in OpennessWorkerCheckTests.cs:9-23, and in scripts/verify-doctor-package.ps1:66-82 (which adds the exe and two pdbs). The tests compile Diagnostics/Checks/*.cs (TiaMcpServer.Tests.csproj:95-96), so the internal member is accessible. TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs exists as the place for a superset check.

### HOSTCLI-14 — Stale 'PLC control' wording in the read-only startup banner

- **Verdict:** confirmed · **Severity:** low · **Category:** docs-drift · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Program.cs:81-84 read-only stderr banner`
- **Evidence:** The read-only banner prints 'Project opening, compilation, writes, lifecycle operations, and PLC control are disabled.' AGENTS.md says PLC run/stop was removed, so no mode offers PLC control.
- **Proposal:** Remove ', and PLC control' (for example: 'Project opening, compilation, writes and lifecycle operations are disabled.'). Check whether any test asserts on the exact banner text.
- **Risk:** Only cosmetic stderr text changes.
- **Verifier notes:** Program.cs:83 prints '..., lifecycle operations, and PLC control are disabled.' AGENTS.md says PLC run/stop was removed. The only other 'PLC control' mentions in the repo are in historical reports. No test asserts the banner text (grep for 'are disabled' in TiaMcpServer.Tests finds nothing).

### HOSTCLI-15 — Doctor evidence formats booleans inconsistently, and two helpers only pass calls through

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Diagnostics/Checks/ProjectBindingCheck.cs:101-102 fileExists.ToString()/bool.FalseString`
  - `TiaMcpServer/Diagnostics/Checks/TiaPortalProcessCheck.cs:39 _hasConfiguredProjectBinding.ToString()`
  - `TiaMcpServer/Diagnostics/Checks/OperatingSystemCheck.cs:15 ToString().ToLowerInvariant()`
  - `TiaMcpServer/Diagnostics/Checks/Evidence.cs:5 Empty()`
  - `TiaMcpServer/Diagnostics/Checks/ProjectBindingCheck.cs:129-130 ModeLabel`
- **Evidence:** Most checks emit "true"/"false" (ToString().ToLowerInvariant()), but ProjectBindingCheck and TiaPortalProcessCheck emit "True"/"False", so the verbose JSON evidence mixes casing. Evidence.Empty() is only `new()`, and ModeLabel only forwards to McpAccessModeNames.ToName.
- **Proposal:** Replace Evidence.Empty with an `Evidence.Flag(bool) => b ? "true" : "false"` helper, use it in every check, and use `new()` directly for empty dictionaries. Inline ModeLabel.
- **Risk:** Verbose doctor evidence values change case for three keys; tests that assert "True" need updating.
- **Verifier notes:** ProjectBindingCheck.cs:101-102 writes fileExists.ToString() and bool.FalseString ('True'/'False'), and TiaPortalProcessCheck.cs:39 writes _hasConfiguredProjectBinding.ToString(). Other checks use ToString().ToLowerInvariant() (for example OperatingSystemCheck.cs:15, OpennessGroupCheck, TiaPortalInstallationCheck). Evidence.cs:5 is only '=> new()' (10 call sites), and ProjectBindingCheck.cs:129-130 ModeLabel only forwards to McpAccessModeNames.ToName. Test impact: ProjectBindingCheckTests.cs:26 asserts bool.FalseString for liveProjectMatchChecked and must change. Low value.

## WORKERCORE

The worker core does its job: the protocol check, then authorization, then the dispatch switch run in a sound order, and WithSession checks capabilities in one place. The main problems are three leftovers from removed safety features (a confirmation-flag field the worker never reads, a vestigial Confirm handshake, and about 1,900 lines of temporary probe methods that only the worker handles and the host never sends), plus drift in how the same failure is categorized: losing the TIA Portal maps to four different categories, and project validation errors come back as worker_operation_failed. Program.cs (1,644 lines) and TiaPortalSession.cs (1,007 lines) are too long, and both repeat the project-open policy, session wrappers and HMI/VCI method-name lists that shared helpers already provide.

### WORKERCORE-1 — WorkerRequest.AllowTiaConfirmations is set by the host ~25 times but never read by the worker

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56 Confirm/AllowTiaConfirmations doc comments`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945,985,1011,1039,1057,1128,1204,1275,...,1886 request.AllowTiaConfirmations = true`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:24-33 TiaPortalSession ctor`
  - `TiaMcpServer.OpennessWorker/Program.cs:32 _sharedSession = new(allowTiaConfirmations: true)`
- **Corrected locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945-1886 (≈26 sites incl. initializers 1672,1768)`
  - `TiaMcpServer.OpennessWorker/Program.cs:32`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:24-33`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleForwardingTests.cs:233,265,331`
- **Evidence:** A repo-wide search for `.AllowTiaConfirmations|AllowTiaConfirmations =` finds only the host's ~25 assignments and the TiaPortalSession ctor *parameter*. The worker decides auto-confirmation once at construction (hard-coded `true` combined with the access mode parsed from the command line) and never looks at the request field. The two adjacent doc comments also contradict each other: Confirm says 'every write EXCEPT update_block_logic, which forwards only AllowTiaConfirmations', and the next says AllowTiaConfirmations is 'set by every write operation, including update_block_logic'.
- **Proposal:** Delete WorkerRequest.AllowTiaConfirmations and every host assignment in OpennessWorkerClient. Leave the ctor parameter as is, or replace it with a passed-in access mode (see WORKERCORE-12).
- **Verifier-corrected proposal:** Delete WorkerRequest.AllowTiaConfirmations and all ~26 host assignments in OpennessWorkerClient.cs, including the initializers at 1672 and 1768 and the doc reference at 1185. Rewrite the Confirm doc comment so it no longer mentions AllowTiaConfirmations. Update the three assertions in NetworkSubnetLifecycleForwardingTests (233/265/331). Keep the TiaPortalSession ctor parameter, or fold it into WORKERCORE-12. Drop the doc-comment-contradiction claim.
- **Risk:** Wire-protocol field removal; System.Text.Json ignores unknown members, so a mismatched host/worker pair is harmless. Check FakeWorker and any request-shape snapshot tests. No live verification needed.
- **Verifier notes:** Core claim holds. search_text finds AllowTiaConfirmations only at WorkerRequest.cs:56, at ~26 host assignments in OpennessWorkerClient.cs (945…1886, including the object initializers at 1672 and 1768), and in the TiaPortalSession ctor *parameter* (TiaPortalSession.cs:24-33, 949). Program.cs:32 hard-codes allowTiaConfirmations:true, so the worker never reads the request field. FakeWorker does not use it. Two corrections: (a) the doc comments at WorkerRequest.cs:49-56 do not contradict each other. Confirm is 'every write EXCEPT update_block_logic, which forwards ONLY AllowTiaConfirmations', and AllowTiaConfirmations is 'every write, including update_block_logic'. Those statements are consistent; OpennessWorkerClient.cs:1128 and :1204 set AllowTiaConfirmations without Confirm. (b) Removing the field breaks tests that assert the serialized member: NetworkSubnetLifecycleForwardingTests.cs:233,265,331 assert root.GetProperty("allowTiaConfirmations").

### WORKERCORE-2 — Vestigial Confirm handshake: host always sends true; worker checks it in 12 handlers and skips it in the others

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:550 ProbeSubnetLifecycleMutations`
  - `TiaMcpServer.OpennessWorker/Program.cs:652 AddNetworkDevice, :672 ConfigureNetworkDevice, :707 CreateSubnet, :750 UpdateSubnet, :788 DeleteSubnet`
  - `TiaMcpServer.OpennessWorker/Program.cs:1141 CreateBlock, :1164 DeleteBlock, :1182 CreateBlockGroup, :1200 DeleteBlockGroup`
  - `TiaMcpServer.OpennessWorker/Program.cs:1370 TagMutation, :1385 ProjectLifecycle(requiresConfirm)`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:944..1885 request.Confirm = true (23 sites)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:550,652,672,707,750,788,1141,1164,1182,1200,1370,1385`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs:33-34`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-53`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:944-1885`
  - `TiaMcpServer.Tests/Safety/WriteSafetyLeaseConcurrencyTests.cs:215`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerDispatchTests.cs:39`
  - `TiaMcpServer.Tests/Network/IoSystemQualificationProbeContractTests.cs:70-72,130-132`
  - `TiaMcpServer.Tests/Multiuser/MultiuserPortalReadDispatchTests.cs:236`
- **Evidence:** Every host write path sets Confirm=true unconditionally (23 sites). The worker still rejects with 'Operation not confirmed. Set confirm=true…' in 12 handlers. update_block_logic, update_type_content, compile_check, all hmi_* writes and all vci_* writes have no such check. AGENTS.md says public confirm arguments were removed ('Reject legacy confirm'), so the error text points callers at a parameter that no longer exists. The real gates are WorkerOperationAuthorization plus host elicitation, not this flag.
- **Proposal:** Remove every `!request.Confirm` check (including the requiresConfirm parameter of ProjectLifecycle) and the WorkerRequest.Confirm field, and drop the host assignments. If a worker-level marker that a write is intentional is still wanted, derive it from OperationPolicyCatalog capability, never a caller-set boolean.
- **Verifier-corrected proposal:** Remove the 12 Program.cs checks, the requiresConfirm parameter of ProjectLifecycle, the Confirm check in IoSystemQualificationProbeValidator (or drop it together with the probe, see WORKERCORE-3), the WorkerRequest.Confirm field and the host assignments. Update IoSystemQualificationProbeContractTests, WriteSafetyLeaseConcurrencyTests (switch its fence to another field), NetworkSubnetLifecycleWorkerDispatchTests and MultiuserPortalReadDispatchTests:236 (pick another non-default field). The real host's behavior does not change.
- **Risk:** Tests asserting the 'Operation not confirmed' message will need updating. No behavior change for the real host (it always sent true). Raw direct worker invocations without Confirm would now proceed, but they still pass access-mode authorization. No live verification needed.
- **Verifier notes:** Confirmed. The host sets Confirm=true unconditionally at 21 `request.Confirm = true` sites plus initializers (OpennessWorkerClient.cs:944-1885). Program.cs checks !request.Confirm in exactly 12 places (550, 652, 672, 707, 750, 788, 1141, 1164, 1182, 1200, 1370, 1385 via ProjectLifecycle requiresConfirm). The error text 'Set confirm=true' points at a public argument AGENTS.md says was removed. The finding misses three further dependencies: (1) a 13th check in Contracts, IoSystemQualificationProbeInfo.cs:33-34 ('Qualification requires confirm=true.'), which IoSystemQualificationProbeContractTests.cs:70-72,130-132 exercise; (2) WriteSafetyLeaseConcurrencyTests.cs:215, whose scripted worker throws 'Worker mutation fence missing.' when confirm is absent, so the flag doubles as a test mutation fence; (3) NetworkSubnetLifecycleWorkerDispatchTests.cs:39, which asserts the source text 'if (!request.Confirm)'. MultiuserPortalReadDispatchTests.cs:236 also sets Confirm to prove the reflection-based selector validator rejects it.

### WORKERCORE-3 — ~1,900 lines of 'temporary' worker-only probes that the host never sends

- **Verdict:** partially-confirmed · **Severity:** high · **Category:** dead-code · **Effort:** M · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:173-175 dispatch; :418-455 ProbeIoSystemQualification; :456-546 ProbeNetworkObjectAttributes/ProbeNetworkAttribute/ProbeExceptionCategory; :548-609 ProbeSubnetLifecycleMutations`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs (713 lines)`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationProbeService.cs (595) + IoSystemQualificationEvidence.cs (346)`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs (207); TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeProbeInfo.cs`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:375-395 Probe* fields`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:108,177-178`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:55-58 StandaloneOnlyProbes, :105-106 OperationFor`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:173-175,418-609`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationProbeService.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationEvidence.cs`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:375-395`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:108,177-178`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:55-58,105-106`
  - `docs/ARCHITECTURE.md:851,896,923-940`
  - `AGENTS.md PR6 paragraph (StandaloneOnlyProbes)`
- **Evidence:** Searching TiaMcpServer/** for probe_network_object_attributes, probe_io_system_qualification and probe_subnet_lifecycle_mutations finds no host sender: OpennessWorkerClient has no method for any of them, and no .ps1 script calls them. They are referenced only by Contracts catalogs, the worker and tests, many of which assert on source text (IoSystemQualificationWorkerContractTests.ExtractMethodBody). docs/ARCHITECTURE.md:896 and :925 call them 'internal-only evidence probe' and 'temporary worker-only diagnostic'. The subnet lifecycle passed its guarded public live rerun on 2026-09-24, and the IO-system qualification matrix finished on 2026-09-26.
- **Proposal:** Delete probe_subnet_lifecycle_mutations and probe_network_object_attributes end to end: dispatch cases, Program handlers, the services, the info types, the five WorkerRequest Probe* fields, their catalog entries and their tests. For probe_io_system_qualification, ask the maintainer whether the still-open public IO-system editing work (docs/roadmap/network-operations.md:31) will reuse IoSystemQualificationProbeService. If yes, move it out of the dispatch switch into a dev-only build; if no, delete it as well. With both probes gone, StandaloneOnlyProbes in ProjectCapabilityCatalog becomes empty and can be removed.
- **Verifier-corrected proposal:** Ask the maintainer to retire the 'temporary' probes. If they agree: delete probe_subnet_lifecycle_mutations and probe_network_object_attributes end to end (dispatch, handlers, services, info types, WorkerRequest Probe* fields at :375-395, catalog entries, tests). Decide probe_io_system_qualification separately, depending on whether the unshipped update_io_system work will reuse it. Update ARCHITECTURE.md (:851, :896, :923-940), roadmap/network-operations.md:233, NETWORK_PHASE4_SUBNET_LIFECYCLE.md, and the AGENTS.md sentence about StandaloneOnlyProbes. Remove StandaloneOnlyProbes only once both qualification probes are gone. Severity medium.
- **Risk:** Loses the ability to re-run the historical qualification evidence (still in git history). Docs need updating: ARCHITECTURE.md §Phase 4/5, NETWORK_PHASE4_SUBNET_LIFECYCLE.md:260, roadmap. Many source-shape tests are deleted with it. No production behavior change.
- **Verifier notes:** No host sender exists: search_text finds the three probe method strings only in Contracts catalogs (OperationPolicyCatalog.cs:108,177-178; ProjectCapabilityCatalog.cs:57,105-106), in the worker (Program.cs:173-175; SubnetLifecycleMutationProbeService.cs:30), in IoSystemQualificationProbeInfo.cs:27 and in tests. No .ps1 or FakeWorker hits. Line counts check out: 713+595+346+207 plus the Program handlers. The finding overstates it as plain dead code, though. The probes are deliberate, documented worker-only acceptance tooling. ARCHITECTURE.md:851-853 says probe_network_object_attributes 'exists solely for the explicitly authorized Phase 3 raw-metadata acceptance mode'. ARCHITECTURE.md:896 and :923-925 and roadmap/network-operations.md:233 describe the other two as present internal probes. AGENTS.md (PR6) explicitly says 'StandaloneOnlyProbes keeps the IO-system and subnet qualification probes standalone-only'. Deleting them is therefore a maintainer decision about retiring acceptance tooling, not a defect, and severity 'high' is overstated. Tests that depend on them include LocalSessionContextContractTests.cs:412-466, IoSystemQualificationWorkerContractTests.cs:104-109 and McpToolSchemaTests.cs:188,214.

### WORKERCORE-4 — Losing the TIA Portal (NonRecoverableException) maps to four different failure categories

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1552-1557 Execute → worker_operation_failed`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciDispatch.cs:25-28,53-56; VciWriter.cs:165-169 → portal_lost`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserPortalReadDispatch.cs:39-43 Run finally → binding_conflict`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserInventoryService.cs:125-126 Connections catch(Exception) → 'incomplete inventory' worker_operation_failed`
  - `TiaMcpServer.OpennessWorker/Program.cs:450-454 ProbeIoSystemQualification catch(Exception)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1552-1557`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserInventoryService.cs:125-126,167-171`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciDispatch.cs:25-28,53-56`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2397`
  - `TiaMcpServer.OpennessWorker/Program.cs:450-454`
- **Evidence:** WorkerFailureCategories.PortalLost exists, and AGENTS.md says it 'invalidate[s] binding and require[s] explicit recovery', but only the VCI code raises it. Every other domain falls into Execute's generic `TIA Portal was closed unexpectedly … worker_operation_failed`. Multiuser inventory reports the same event as binding_conflict, or (inside Connections) as 'incomplete or unreadable inventory evidence' because its catch(Exception) also swallows NonRecoverableException. The host therefore sees the same event under different categories depending on which tool happened to be running.
- **Proposal:** In Program.Execute, map NonRecoverableException to WorkerFailureCategories.PortalLost, then delete the VciDispatch.Plan/Read try/catch wrappers that duplicate it. In MultiuserInventoryService.Connections/Read, add `catch (NonRecoverableException) { throw; }` before the generic catch, as VciReader/VciScopeResolver already do. Confirm that the host's portal_lost handling, which invalidates the binding, is right for all domains.
- **Verifier-corrected proposal:** Map NonRecoverableException to WorkerFailureCategories.PortalLost in Program.Execute so that every domain invalidates the binding through OpennessWorkerClient.cs:2397. Keep VciWriter's catch, which carries the outcome. Either keep the VciDispatch.Plan/Read wrappers for their messages and direct-test seam, or delete them and retarget VciPlannerTests/VciReaderTests to the Execute path. Add `catch (NonRecoverableException) { throw; }` before the generic catches in MultiuserInventoryService.Connections and Read. Update the tests that assert worker_operation_failed. A live Portal-kill check is advisable.
- **Risk:** Changes the error category for non-VCI tools when the Portal dies, so host binding invalidation now runs for them. Update tests asserting worker_operation_failed. A live check (kill Portal during a plc_read) is advisable.
- **Verifier notes:** Confirmed: Program.Execute (Program.cs:1552-1557) maps NonRecoverableException to WorkerOperationFailed, while VciDispatch.cs:25-28,53-56 and VciWriter.cs:165-169 map it to PortalLost. MultiuserPortalReadDispatch.Run's finally (:39-46) throws BindingConflict. MultiuserInventoryService.Connections (:125-126) and Read (:167-171) catch(Exception) and so also swallow NonRecoverableException. ProbeIoSystemQualification's catch(Exception) (Program.cs:450-454) does the same. Corrections: (a) the host invalidates the binding on BindingConflict OR PortalLost (OpennessWorkerClient.cs:2397), so the multiuser binding_conflict path already invalidates. The material gap is that every non-VCI domain returns worker_operation_failed, so a dead Portal does NOT invalidate the binding there. (b) The VciDispatch wrappers are not pure duplicates: they carry domain-specific messages and are unit-tested directly, without Execute (VciPlannerTests.cs:279-285, VciReaderTests.cs:428-440). VciWriter's catch must stay because it attaches vciWriteOutcome counts.

### WORKERCORE-5 — Lifecycle/session validation errors come back as worker_operation_failed instead of validation_error

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:452-502 RequireAbsoluteFile/RequireAbsoluteDirectory/RequireName/RequireDirectoryExists`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:298-301 invalid archive mode InvalidOperationException; :127-130 CreateProject`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:41-44 RequireStandaloneOwner InvalidOperationException with hard-coded McpAccessMode.ReadWrite`
  - `TiaMcpServer.OpennessWorker/Program.cs:1292-1295 OpenProject duplicate ProjectPath check; :1558-1565 Execute catch InvalidOperationException/IOException`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:298-301,375,378,452-502`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:41-44,402-412`
  - `TiaMcpServer.OpennessWorker/Program.cs:1292-1295,1558-1565`
- **Evidence:** The validation in Program.cs throws WorkerOperationException(ValidationError) (e.g. OpenProject 'ProjectPath is required.'). The lifecycle service's own Require* helpers throw InvalidOperationException, FileNotFoundException or DirectoryNotFoundException for the same kind of input fault ('ProjectName is required', 'must be an absolute path', 'Invalid archive mode'). Execute maps those to worker_operation_failed, so input errors on create_project/save_project_as/archive_project look like Siemens failures. ProjectLifecycleService.EnsureRequestedContext (:375,:378) and RequireStandaloneOwner also hard-code McpAccessMode.ReadWrite into the policy messages instead of the real mode.
- **Proposal:** Make the Require* helpers throw WorkerOperationException(ValidationError, …). Fold RequireDirectoryExists into RequireAbsoluteDirectory(mustExist:true) after the archive guard. Throw ValidationError for an invalid archive mode. Have RequireStandaloneOwner throw a categorized WorkerOperationException. Delete the duplicate ProjectPath check in Program.OpenProject, or the one in the service. Pass the actual access mode instead of the ReadWrite literal.
- **Verifier-corrected proposal:** Make RequireAbsoluteFile, RequireAbsoluteDirectory, RequireName and the archive-mode parse throw WorkerOperationException(ValidationError). Keep the not-found cases as a categorized failure, such as target_not_found or validation_error, as the maintainer prefers. Collapse the triple open_project path validation into one site, either the session's ValidationError check or the service's. Pass the real access mode into EnsureRequestedContext and RequireStandaloneOwner's message. Leave CreateProject :127-130 unchanged.
- **Risk:** Error category changes for lifecycle input faults; host audit/projection tests that assert worker_operation_failed need updating. Host-side validation probably catches most of these first, so user-visible impact is small. No live verification needed.
- **Verifier notes:** Confirmed for ProjectLifecycleService.cs:452-502. RequireAbsoluteFile, RequireAbsoluteDirectory and RequireName throw InvalidOperationException; RequireAbsoluteFile throws FileNotFoundException and the directory helpers DirectoryNotFoundException (both IOException subclasses). :298-301 throws InvalidOperationException for an invalid archive mode. Program.Execute (:1558-1565) maps all of these to worker_operation_failed. The inconsistency is also visible inside the open flow: TiaPortalSession.OpenProject (:402-412) throws ValidationError for the same not-rooted check that the service's RequireAbsoluteFile already rejected one step earlier with InvalidOperationException. Program.OpenProject (:1292-1295) adds a third ProjectPath check. EnsureRequestedContext (:375,:378) and RequireStandaloneOwner (TiaPortalSession.cs:44) hard-code McpAccessMode.ReadWrite. One location is wrong: CreateProject :127-130 is 'No TIA Portal session is connected', a runtime condition, not an input fault.

### WORKERCORE-6 — Three hand-written copies of the ProjectOpenPolicy.Decide gate with different outcomes

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1429-1450 EnsureRequestedProjectOpen (returns Failure, uses _accessMode)`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:367-380 EnsureRequestedContext (throws, hard-codes ReadWrite)`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:55-68 ResolveProjectForRead (handles only Refuse)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1429-1450`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:13-19,55-68,367-380`
  - `TiaMcpServer.Tests/Worker/WorkerOpenPolicySourceTests.cs`
  - `TiaMcpServer.Tests/Worker/ActiveProjectContextSourceTests.cs`
- **Evidence:** Each copy switches over ProjectOpenDecision on its own and maps to AccessDenied/BindingConflict/WorkerOperationFailed slightly differently: Program returns a response object, the lifecycle path throws, and the read path deliberately skips RequestedNotOpen. The doc comment on GetStatusReadOnly (:13-19) claims this is 'enforced identically everywhere', but there are three implementations.
- **Proposal:** Add one static helper in Openness/Project, e.g. `ProjectOpenGate.Require(TiaPortalSession, string? requestedPath, McpAccessMode mode, bool allowNotOpen)`, that throws WorkerOperationException with the canonical category. Call it from WithActiveContext (replacing EnsureRequestedProjectOpen), from EnsureRequestedContext, and from ResolveProjectForRead (allowNotOpen:true).
- **Verifier-corrected proposal:** Add one static gate, ProjectOpenGate.Require(session, requestedPath, mode, allowRequestedNotOpen, allowNoProject), that throws WorkerOperationException with the canonical categories. WithActiveContext calls it with (false, false), EnsureRequestedContext with (false, true), ResolveProjectForRead with (true, true). Pass the real mode everywhere. Update the source-shape tests that name EnsureRequestedProjectOpen.
- **Risk:** EnsureRequestedProjectOpen currently returns rather than throws, but Execute converts WorkerOperationException the same way, so the response shape is unchanged. Source-shape tests that grep for EnsureRequestedProjectOpen will need edits.
- **Verifier notes:** There are three switch-over-ProjectOpenPolicy.Decide copies: Program.EnsureRequestedProjectOpen (Program.cs:1429-1450, returns a Failure and uses _accessMode), ProjectLifecycleService.EnsureRequestedContext (:367-380, throws and hard-codes ReadWrite), and ResolveProjectForRead (:55-68, handles only Refuse). The GetStatusReadOnly doc (:13-19) claims a single rule. But the copies differ deliberately in two dimensions, not one. ResolveProjectForRead tolerates RequestedNotOpen AND 'no project open' (it returns null and reports IsOpen=false), while EnsureRequestedProjectOpen fails both. EnsureRequestedContext tolerates no project open and leaves that to RequireStandaloneOwner. A single allowNotOpen flag is not enough. Source tests reference EnsureRequestedProjectOpen (ActiveProjectContextSourceTests, WorkerOpenPolicySourceTests, NetworkSubnetLifecycleWorkerDispatchTests).

### WORKERCORE-7 — Several copies of the session/active-context wrapper; subnet and VCI writes bypass WithEngineeringRoot despite AGENTS.md

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1403-1423 WithActiveContext`
  - `TiaMcpServer.OpennessWorker/Program.cs:835-855 WithSubnetLifecycleProject`
  - `TiaMcpServer.OpennessWorker/Program.cs:425-433 ProbeIoSystemQualification, :585-598 ProbeSubnetLifecycleMutations, :618-632 SearchEquipmentCatalog (inline copies)`
  - `TiaMcpServer.OpennessWorker/Program.cs:1032-1034 WriteVci (session.TiaPortal! unchecked)`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:60-65 NonProjectMethods includes search_equipment_catalog`
  - `AGENTS.md PR6 paragraph: 'Every content handler, write and compile included, runs through WithEngineeringRoot'`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:835-855,1403-1423,585-598,618-632,1032-1034`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:60-65`
  - `AGENTS.md PR6 paragraph`
- **Evidence:** WithSubnetLifecycleProject is WithActiveContext plus one `session.TiaPortal is null` check. Three more handlers repeat EnsureRequestedProjectOpen + ValidateExpectedAfterProjectResolution + null checks inline. create/update/delete_subnet and vci_* writes do not go through WithEngineeringRoot, which contradicts AGENTS.md. SearchEquipmentCatalog is listed as a non-project method (OperationFor returns null, so there is no capability gate), yet the worker refuses it unless a project is open.
- **Proposal:** Have WithActiveContext guarantee both ActiveContext and TiaPortal; EnsureConnected already implies a Portal, so this is a single check. Delete WithSubnetLifecycleProject and the inline copies, letting those handlers call WithActiveContext. Either change the AGENTS.md sentence to 'WithEngineeringRoot or WithActiveContext (when the Portal handle is needed for ExclusiveAccess/Transaction)', or route subnet writes through WithEngineeringRoot plus a Portal accessor. Decide whether search_equipment_catalog really needs an open project; if not, run it through WithSession only.
- **Verifier-corrected proposal:** Have WithActiveContext also fail when TiaPortal is null, then delete WithSubnetLifecycleProject and route the subnet handlers, ProbeSubnetLifecycleMutations and WriteVci through it. Leave ProbeIoSystemQualification's custom remapping alone, or retire it with WORKERCORE-3. Reword the AGENTS.md PR6 sentence to 'WithEngineeringRoot, or WithActiveContext when the Portal handle is needed'; this is a doc clarification, not a defect. Raise search_equipment_catalog's open-project requirement as a maintainer question, not a change.
- **Risk:** Small behavior change only if search_equipment_catalog drops its open-project requirement (needs a host contract decision and a live check). Otherwise the change is mechanical; source-shape tests may need edits.
- **Verifier notes:** WithSubnetLifecycleProject (Program.cs:835-855) is WithActiveContext (:1403-1423) plus a TiaPortal null check and a different message; SearchEquipmentCatalog (:618-632) and ProbeSubnetLifecycleMutations (:585-598) inline the same sequence. ProbeIoSystemQualification (:425-433) is NOT a pure copy: it rewrites the failure message and wraps everything in its own catch. The claimed contradiction with AGENTS.md is mostly wording. Subnet writes pass session.EngineeringRoot (Program.cs:740-786), the same ProjectBase that WithEngineeringRoot supplies, so local-session behavior matches. VCI writes are standalone-only by design (AGENTS.md VCI bullet), so the PR6 sentence about local-session content handlers does not cover them. search_equipment_catalog is confirmed: it is in NonProjectMethods (ProjectCapabilityCatalog.cs:60-65, so OperationFor returns null) yet still requires an open project through EnsureRequestedProjectOpen. Whether that requirement is intended is undocumented.

### WORKERCORE-8 — HMI/VCI worker-method names hard-coded in up to four places even though HmiWriteMethods/VciMethods exist

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:188-200 dispatch literals for hmi_* and vci_*`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:109-128,149-157 HMI literals vs :186-190 VCI loops over VciMethods`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:43-45 HMI write literals`
  - `TiaMcpServer.Contracts/Hmi/HmiWriteMethods.cs:3-17 HmiWriteMethods.All`
  - `TiaMcpServer.Contracts/Vci/VciMethods.cs:3-25`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadDispatch.cs:27-59`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:188-200`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:109-128,149-157,186-190`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:40-52`
  - `TiaMcpServer.Contracts/Hmi/HmiWriteMethods.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadDispatch.cs:27-59`
- **Evidence:** VCI registration in OperationPolicyCatalog loops over VciMethods.Read/Write, but HMI writes are listed literally there even though HmiWriteMethods.All sits in the same assembly, and the 20 HMI read names have no shared constant in Contracts at all. Program.cs spells out all 9 HMI writes, 20 HMI reads and 9 VCI names again. Adding an HMI operation therefore means editing Program.cs, OperationPolicyCatalog, ProjectCapabilityCatalog, HmiReadDispatch and the host catalog in step. ProjectCapabilityCatalog also advertises HMI write items but no HMI read or VCI items, which is asymmetric.
- **Proposal:** Add `HmiReadMethods.All` to TiaMcpServer.Contracts/Hmi next to HmiWriteMethods. In Program.cs, replace the literal `or` chains with guards: `var m when HmiWriteMethods.All.Contains(m) => WriteHmi(request)`, `var m when HmiReadMethods.All.Contains(m) => ReadHmi(request)`, and the same for VciMethods.Read/Write. In OperationPolicyCatalog, register HMI with loops like VCI. In ProjectCapabilityCatalog, concat HmiWriteMethods.All instead of the literals. Have the host's HmiOperationCatalog test assert equality with HmiReadMethods.All.
- **Verifier-corrected proposal:** Add HmiReadMethods.All in Contracts/Hmi. Register HMI in OperationPolicyCatalog with loops, as VCI is. Replace the Program.cs `or` chains with `var m when HmiWriteMethods.All.Contains(m)`, `HmiReadMethods.All`, `VciMethods.Read/Write` guards placed after the explicit cases. In ProjectCapabilityCatalog, splice HmiWriteMethods.All in at the same position to preserve Describe order, or ask whether the raw hmi_* worker names belong in the public capability list at all. Assert HmiOperationCatalog worker methods equal HmiReadMethods.All ∪ HmiWriteMethods.All.
- **Risk:** Pure refactor; the existing catalog-completeness tests keep it honest. The switch guard order must keep the explicit cases first. No live verification needed.
- **Verifier notes:** Confirmed: Program.cs:188-200 spells out 9 HMI write, 20 HMI read and 9 VCI literals. OperationPolicyCatalog.cs:109-128 and :149-157 list HMI names literally but loop over VciMethods at :186-190. ProjectCapabilityCatalog.cs:43-45 repeats the 9 HMI write literals even though HmiWriteMethods.All lives in the same assembly and is already used at :96. HmiReadDispatch.cs:27-59 and HmiWriteDispatch.cs:15 each repeat them once more. Corrections: the claim that ProjectCapabilityCatalog advertises no VCI items is wrong; StandaloneVciOperations (:23-26) advertises vci_read/vci_write as StandaloneOnly, consistent with AGENTS.md. The real asymmetry is that HMI writes appear in Describe under both 'hmi_write' and the 9 raw worker names, while no other domain exposes worker names. The host HmiOperationCatalog has no literal duplicates (no hmi_list_* hits in TiaMcpServer/**), so 'host catalog in step' is overstated.

### WORKERCORE-9 — Program.cs (1,644 lines) carries ~850 lines of Network/PLC handler and validation code that sibling domains keep in dispatchers

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** L · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:335-855 network handlers + validators (ListNetworkObjects, ValidateListNetworkObjectsRequest, Inspect/Probe, subnet validators)`
  - `TiaMcpServer.OpennessWorker/Program.cs:880-1209 block/type/tag handlers`
  - `TiaMcpServer.OpennessWorker/Program.cs:975-1002 NormalizeTypeFormat/NormalizeBlockFormat (identical except default)`
  - `TiaMcpServer.OpennessWorker/Program.cs:1243-1274 GetBasicProjectStatus/ProbeProjectStatusForLifecycle near-identical; ProjectLifecycleService.cs:86-89 ProbeStatusForLifecycle pass-through`
  - `TiaMcpServer.OpennessWorker/Program.cs:233-236 NetworkCursorException catch in the protocol loop`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:335-855,880-1209,975-1002,1243-1274,233-236`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:86-89`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectCursorCodec.cs:18`
- **Evidence:** HMI, VCI and Multiuser already route through HmiReadDispatch, HmiWriteDispatch, VciDispatch and MultiuserPortalReadDispatch, which leaves the Program.cs handlers as one-liners. Network and PLC kept all their argument validation and orchestration in Program.cs, so the file mixes protocol loop, session plumbing and domain rules. Small duplicates: two format normalizers differing only in default; two basic-status handlers differing only in the Operation label (and ProbeStatusForLifecycle just forwards to GetBasicStatusReadOnly with the Full default); a network-specific exception type caught in the generic loop.
- **Proposal:** Extract `NetworkWorkerDispatch` (Openness/Network) and `PlcWorkerDispatch` (Openness/Plc), each with methods taking (ProjectBase or TiaPortalSession, WorkerRequest), mirroring HmiReadDispatch. Program.cs keeps only Main, HandleLine, WithSession, Execute, Stamp and the response helpers. Merge the two format normalizers into `NormalizeFormat(string? format, string defaultFormat)`. Handle both basic-status methods in one handler with `Operation = request.Method`, and delete ProbeStatusForLifecycle. Turn NetworkCursorException into a WorkerOperationException at its source.
- **Verifier-corrected proposal:** Extract NetworkWorkerDispatch and PlcWorkerDispatch, mirroring HmiReadDispatch, after WORKERCORE-2/3. Merge the format normalizers into NormalizeFormat(format, defaultFormat). For the status handlers, share one private helper that takes an explicit operation label and mode, keeping "get_project_status" for get_basic_project_status; ProbeStatusForLifecycle may be inlined. Convert NetworkCursorException to WorkerOperationException at its source. Effort L; many source-text tests need retargeting.
- **Risk:** Large mechanical move. Many tests read Program.cs source text (IoSystemQualificationWorkerContractTests, ActiveProjectContextSourceTests, NetworkIntrospectionWorkerDispatchTests), so those break and must be retargeted. Keep the protocol and authorization ordering intact. Do it after WORKERCORE-2/3 to avoid moving dead code.
- **Verifier notes:** Program.cs is 1,644 lines and holds the Network/PLC handlers and validators inline. HMI, VCI and Multiuser use dispatchers. NormalizeTypeFormat and NormalizeBlockFormat (:975-1002) are identical except for the default; each carries a doc comment explaining that default. NetworkCursorException (NetworkObjectCursorCodec.cs:18) is caught in the generic loop (:233-236). The status-merge proposal is wrong, though. GetBasicProjectStatus (:1243-1256) reports Operation="get_project_status", not its method name get_basic_project_status, and passes _accessMode. ProbeProjectStatusForLifecycle (:1262-1274) reports "probe_project_status_for_lifecycle" and uses the Full default through ProbeStatusForLifecycle. `Operation = request.Method` would change the get_basic_project_status payload, which is a contract change.

### WORKERCORE-10 — TiaPortalSession (1,007 lines): dead overload, duplicated Connect/EnsureConnected guards, a compatibility property with one production caller

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:257-258 SelectPortalProject(string) overload`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:130-143 Connect PID checks vs :697-712 EnsureConnected`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:39 Project property; ProjectLifecycleService.cs:251 only production use`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:397-556 OpenProject (160 lines), :260-372 SelectPortalProject (113 lines)`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:780-792 HasRetainingClient vs :794-812 EvaluatePortalDetach`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:39,130-143,257-258,780-812`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs:251`
  - `TiaMcpServer.Tests/Worker/ActiveProjectContextSessionTests.cs:107,109,191,194,213,232`
  - `TiaMcpServer.Tests/Multiuser/LocalSessionSelectionWorkerTests.cs`
- **Evidence:** The single-argument SelectPortalProject overload has zero references. EnsureConnected calls Connect only when !IsConnected, so Connect's IsConnected/foreign-PID branch and its PID-positive check are reachable only from tests that call the public Connect directly. `Project` is labelled a 'Standalone-only compatibility bridge', yet RequireStandaloneOwner().Project exists and production uses `Project` once. HasRetainingClient and EvaluatePortalDetach each compute 'has UI or other attached client' separately. OpenProject and SelectPortalProject are long multi-branch methods.
- **Proposal:** Delete the SelectPortalProject(string) overload. Make Connect private, delete its IsConnected branch and duplicate PID validation, and point tests at EnsureConnected. Replace `session.Project` in SaveProjectAs with `session.RequireStandaloneOwner().Project` and remove the property, or mark it internal for tests. Extract a `ReadAttachedClients(TiaPortalProcess)` helper used by both detach checks. Split OpenProject into rebind-close and open/verify steps.
- **Verifier-corrected proposal:** Delete the SelectPortalProject(string) overload and update the six test calls to pass (path, null). Make Connect private or internal, drop its unreachable IsConnected branch and duplicate PID check, and move the tests to EnsureConnected; that is a large test edit. Replace session.Project at ProjectLifecycleService.cs:251 with RequireStandaloneOwner().Project and make Project internal for tests. Extracting a ReadAttachedClients helper is optional, low value. Splitting OpenProject is a judgement call; it needs a live open/bind smoke test.
- **Risk:** Many worker tests call Connect and Session.Project directly (LocalSessionSelectionWorkerTests, ActiveProjectContextSessionTests). Session code carries heavy live-acceptance history, so keep each branch's order and run the multiuser/session test suites. A short live open/bind smoke test is advisable after splitting OpenProject.
- **Verifier notes:** The 'zero references' claim is false: SelectPortalProject(string) (TiaPortalSession.cs:257-258) is used by tests at ActiveProjectContextSessionTests.cs:107,109,191,194,213,232. It is unused in production only, since Program.cs:276 calls the two-argument overload. Connect (:130) is called in production only when !IsConnected (EnsureConnected :704-706 and OpenProject :415-416), so its IsConnected/foreign-PID branch (:137-143) is reachable only from tests, which call Connect ~20 times (LocalSessionSelectionWorkerTests, LocalSessionObservationTests, LocalSessionOpenWorkerTests; e.g. :230 Connect(f.Amc, other.Id)). Project (:39) has one production use (ProjectLifecycleService.cs:251) plus tests. HasRetainingClient (:780-792) and EvaluatePortalDetach (:794-812) overlap only on reading UI and other clients. They differ in failure semantics (one returns false silently, the other logs and counts) and in what they return, so the shared helper is small.

### WORKERCORE-11 — Multiuser inventory validates selectors twice and attaches to the Portal three times per request

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserPortalReadDispatch.cs:30-47 Run, :49-71 Validate, :27 Success<T>`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserInventoryService.cs:20,31,42,56,81 Connect(pid); :103-106 Connect; :178-187 Validate/RequireName`
  - `TiaMcpServer.OpennessWorker/Program.cs:1598-1605 Success<T>`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserPortalReadDispatch.cs:28,51-73`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserInventoryService.cs:20,31,42,56,81,103-106,178-187`
  - `TiaMcpServer.OpennessWorker/Program.cs:1598-1605`
- **Evidence:** Run calls EnsurePortalConnected before the body, each service method calls Connect, which is EnsurePortalConnected again, and Run's finally calls it a third time. Alias, group coherence and server-project-name are checked both in MultiuserPortalReadDispatch.Validate and in MultiuserInventoryService.Validate/RequireName. MultiuserPortalReadDispatch keeps a private Success<T> identical to Program.Success. Run's `finally` throws on lost attachment, which deliberately replaces any body exception; but if the trailing EnsurePortalConnected/Validate throws, it also hides the original error.
- **Proposal:** Remove MultiuserInventoryService.Connect and its five calls, since Run owns attachment. Keep the selector validation in only one layer (the dispatch Validate is the stricter one) and delete the service's Validate/RequireName, or keep them only as Debug.Assert. Move the Success<T> helper to a shared internal WorkerResponses class used by both. Move the post-body revalidation out of `finally` into a catch-and-rethrow wrapper, so that a body exception is kept unless the attachment was actually lost.
- **Verifier-corrected proposal:** Remove the redundant service-level Validate/RequireName and the private Connect (keep the session pre-connected in the direct service tests), and share Success<T> through one internal helper. Leave the post-body revalidation in `finally` unchanged unless the maintainer wants body exceptions kept over identity re-check failures. Low priority.
- **Risk:** The service is also called directly by MultiuserInventoryWorkerTests, which may rely on the service-level validation; adjust those tests. PR3 live acceptance covered this path, so keep the attach/validate ordering seen at the wire.
- **Verifier notes:** Duplicate validation confirmed: MultiuserPortalReadDispatch.Validate (:51-73) checks alias, group coherence and project name, and MultiuserInventoryService.Validate/RequireName (:178-187) check them again. Success<T> (:28) duplicates Program.Success (Program.cs:1598-1605). Overstated: the request does not 'attach three times'. EnsurePortalConnected (TiaPortalSession.cs:203-226) attaches only when !IsConnected; afterwards it only re-checks the PID and calls ValidatePortalProcess. So it is one attach plus two liveness checks. The post-body check in `finally` is commented as deliberate ('Validate failures too, but never auto-attach after loss during the read', :41). Moving it out of finally reverses an intentional PR3 choice that was accepted live. The service is constructed directly in tests (LocalSessionObservationTests.cs:71,107; MultiuserPortalReadDispatchTests.cs:30), and they pre-connect the session.

### WORKERCORE-12 — Small hygiene issues: access mode parsed twice, identical ReadWrite/Full presets, an unhandled exception reported twice, a non-English comment

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:35-36 _accessMode and TiaPortalSession.cs:30 ParseAccessMode(Environment.GetCommandLineArgs())`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:41-50 IsCapabilityAllowed ReadWrite and Full arms`
  - `TiaMcpServer.OpennessWorker/Program.cs:241-247 HandleLine catch(Exception) + :71-92 stderr capture`
  - `TiaMcpServer.OpennessWorker/Program.cs:30-31 Korean comment`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:30-36`
  - `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs:24-33`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:41-50`
- **Evidence:** The access mode is parsed from the command line in two places: Program's static field and the TiaPortalSession ctor. The ReadWrite and Full arms of IsCapabilityAllowed are character-for-character identical (AGENTS.md: full 'adds no tools or operations'). HandleLine's catch-all writes 'Unhandled worker exception: …' to Console.Error, which HandleLineWithCapturedStderr merges into the same response's Warnings, so the error text appears both in Error and in Warnings. Program.cs:30-31 is the only Korean comment in a codebase otherwise written in English.
- **Proposal:** Pass McpAccessMode into the TiaPortalSession ctor (`new(_accessMode)`) and compute _allowTiaConfirmations from it. Collapse the two arms to `McpAccessMode.ReadWrite or McpAccessMode.Full => …`. In HandleLine's catch-all, drop the Console.Error write (the Failure already carries type and message), or write only after the capture window ends. Translate the comment to English.
- **Verifier-corrected proposal:** Pass McpAccessMode into the TiaPortalSession ctor, or have it read Program's parsed value, keeping a test-friendly default. Collapse the arms to `McpAccessMode.ReadWrite or McpAccessMode.Full => …`. Translate the comment to English. Leave the HandleLine stderr warning unless the maintainer wants it removed.
- **Risk:** Negligible. One test may assert the duplicated warning line; update it.
- **Verifier notes:** Three of the four items are confirmed. The access mode is parsed twice (Program.cs:35-36 and TiaPortalSession.cs:30). The ReadWrite and Full arms of IsCapabilityAllowed are identical (OperationPolicyCatalog.cs:41-50), consistent with AGENTS.md 'full adds no tools or operations'. The Korean comment at Program.cs:30-31 is real. The duplicated unhandled-exception report (Program.cs:241-247) is deliberate: the adjacent comment says the stderr line 'is captured into the response's Warnings by HandleLineWithCapturedStderr'. Dropping it is a judgement call, not a defect, and no test asserts 'Unhandled worker exception'. Passing the mode into the TiaPortalSession ctor changes a constructor that many tests call as `new TiaPortalSession()`.

## WORKERNET

The Network worker domain works, but it carries a lot of copy-paste. The same guarded Openness reads (optional string/enum, typed identity string/int, exact string attribute, SubnetId) are written five or six times with different exception filters and different completeness rules. Selector resolution and dependency lookup therefore disagree on whether an unreadable candidate counts as not found, ambiguous or evidence mismatch. About 1,900 lines (three worker-only probes plus their contracts and tests) have no caller in the repo; ARCHITECTURE.md labels them temporary or internal-only. Two readers are over 800 lines, mostly because of these duplicated helpers. Node selectors in read_hardware_config are computed and then always overwritten. list_network_objects cursors are unsigned and built in the worker, while the sibling hardware-page, project-tree and HMI cursors are sealed by the host.

### WORKERNET-1 — Guarded Openness read helpers duplicated 5-6x with drifting exception filters and negative-number rules

- **Verdict:** partially-confirmed · **Severity:** high · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:701-820 ReadTypedIdentityString/ReadTypedIdentityInt/ReadExactStringIdentityAttribute/ReadOptionalString/ReadOptionalEnumName/ReadExactStringAttribute/CombineDiagnostics`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectIndexReader.cs:399-476 ReadTypedString/ReadTypedInt/ReadInterfaceName/ReadSubnetId/CombineDiagnostics`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:701-808 ReadOptionalStringAttribute/TryReadStringAttribute/ReadOptionalString/ReadOptionalBool/ReadOptionalEnumName/TryReadEnumName/AddReadMessage`
  - `TiaMcpServer.OpennessWorker/Openness/Network/CommunicationConnectionReader.cs:186-200 ReadOptionalString`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareIoMapReader.cs:349-403 ReadOptionalNonNegativeInt/ReadOptionalInt/ReadOptionalEnumName`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:701-820`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectIndexReader.cs:399-476`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:701-808`
  - `TiaMcpServer.OpennessWorker/Openness/Network/CommunicationConnectionReader.cs:186-200`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareIoMapReader.cs:349-403`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectDiscoveryEvidence.cs:59-85`
- **Evidence:** Same shape, different filters. HardwareConfigReader.ReadOptionalString (751) catches only EngineeringException. CommunicationConnectionReader.ReadOptionalString (186) and NetworkObjectSelectorResolver.ReadOptionalString (734) catch Exception. HardwareConfigReader.ReadOptionalEnumName (767) and HardwareIoMapReader.ReadOptionalEnumName (388) are byte-identical. CombineDiagnostics is identical in HardwareConfigReader:814 and NetworkObjectIndexReader:470. ReadTypedIdentityInt (HardwareConfigReader:715-732) rejects negative values as unusable, but NetworkObjectIndexReader.ReadTypedInt (413-425) does not. So an IO system with a negative Number gets a selector from list_network_objects (NetworkObjectIndexReader:315-326) but not from read_hardware_config (HardwareConfigReader:568-586). NetworkDeviceConfigurator.FindExactlyOneIoSystem (256) treats it as unreadable.
- **Proposal:** Add one internal static NetworkOpennessRead helper in Openness/Network, or extend Infrastructure/OpennessReflection. It holds OptionalString/OptionalEnumName/OptionalBool/OptionalInt (Func<T>, description, List<string>?), TypedIdentityString/TypedIdentityInt (rejectNegative), ExactStringAttribute/ExactStringIdentityAttribute and CombineDiagnostics, with a single exception filter (see WORKERNET-7). Delete the private copies in the five files. Choose one negative-number rule for IO system Number in both discovery paths.
- **Verifier-corrected proposal:** Extract one internal static helper in Openness/Network (or Infrastructure) for OptionalString/OptionalEnumName/OptionalBool/OptionalInt, TypedIdentityString/TypedIdentityInt, ExactString(Identity)Attribute and CombineDiagnostics. Use a single exception filter (see WORKERNET-7) and IsNullOrWhiteSpace for diagnostics. Delete the private copies. Delete the redundant negative check in ReadTypedIdentityInt, because ReadInt already rejects negatives. No IO-system selectability change is needed or expected. Risk: some diagnostic text changes; source-text tests (e.g. HardwareDeviceSelectionTests pins 'ReadTypedIdentityString(() => device.Name, "Device name")') must be updated.
- **Risk:** Diagnostic message text may change slightly ('Could not read X: ...'), and so can the selectability of negative-number IO systems in list_network_objects. Worker-only change; existing determinism/selector tests should catch drift. A short live read regression on one project is advisable.
- **Verifier notes:** The duplication is real. HardwareConfigReader.ReadOptionalString (HardwareConfigReader.cs:751-765) catches EngineeringException, while CommunicationConnectionReader.ReadOptionalString (186-200) and NetworkObjectSelectorResolver.ReadOptionalString (734-748) catch Exception. ReadOptionalEnumName in HardwareConfigReader.cs:767-782 and HardwareIoMapReader.cs:388-403 is byte-identical. The negative-number drift claim is WRONG: NetworkObjectDiscoveryEvidence.ReadInt (NetworkObjectDiscoveryEvidence.cs:75-80) already returns Unusable('negative') for number < 0, so NetworkObjectIndexReader.ReadTypedInt (413-425) rejects negative IO-system Numbers too, and list_network_objects and read_hardware_config agree. The extra negative branch in HardwareConfigReader.ReadTypedIdentityInt (722-726) is unreachable duplicate logic. The two CombineDiagnostics copies are not identical: HardwareConfigReader.cs:818 filters IsNullOrWhiteSpace and NetworkObjectIndexReader.cs:474 filters IsNullOrEmpty.

### WORKERNET-2 — Subnet-by-SubnetId lookup implemented six ways with conflicting unreadable-candidate semantics

- **Verdict:** partially-confirmed · **Severity:** high · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:588-611 MatchSubnet`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs:277-310 FindMatches/ReadSubnetId`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:183-216 FindExactlyOneSubnet`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:94-101 VerifySubnet`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:504-509 RequireSubnetIdentity`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectIndexReader.cs:442-454 ReadSubnetId`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateSourceFactory.cs:120-123`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs:612-625 FindById/ReadSubnetId`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:588-611`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs:277-310`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:183-216`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:94-101`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs:612-625`
- **Evidence:** There are three read styles. (a) Strict typed string through ReadExactStringIdentityAttribute/RequireSubnetIdentity. (b) GetAttribute("SubnetId")?.ToString() catching EngineeringException (SubnetLifecycleService:304). (c) OpennessReflection.ReadPropertyOrAttribute, a CLR-property reflection probe then ToString() (NetworkDeviceConfigurator:190, probe:625). Completeness handling also differs. MatchSubnet calls TryReadStringAttribute(..., out _) and skips unreadable candidates, so an unreadable duplicate gives a unique match or target_not_found. SubnetLifecycleService counts unreadables and fails worker_operation_failed through ClassifySelection. NetworkDeviceConfigurator sets an unreadable flag that becomes ClassifyDependencySelection. NetworkMutationVerifier throws, which gives unverified. MatchDevice in the same resolver (561-572) returns target_evidence_mismatch on any unreadable device name, so subnet and device namespaces are treated differently.
- **Proposal:** Add one SubnetIdentity helper with Read(Subnet) returning NetworkObjectDiscoveryEvidenceValue<string> (the strict typed path) and FindExactlyOne(ProjectBase, id) returning (List<Subnet> matches, bool complete). Use it from all callers and keep per-caller category mapping (resolver: evidence_mismatch when incomplete, matching MatchDevice; lifecycle: ClassifySelection; configurator: ClassifyDependencySelection). Drop the ReadPropertyOrAttribute/ToString variants.
- **Verifier-corrected proposal:** Add one SubnetIdentity helper. Read(Subnet) returns the strict NetworkObjectDiscoveryEvidenceValue<string>, reusing ReadExactStringIdentityAttribute. FindAll(ProjectBase, id) returns (matches, complete) and guards enumeration. Use it from MatchSubnet, SubnetLifecycleService.FindMatches, NetworkDeviceConfigurator.FindExactlyOneSubnet, NetworkMutationVerifier.VerifySubnet and the probe's FindById, keeping each caller's category mapping. In MatchSubnet, return EvidenceMismatch when the result is incomplete, consistent with MatchDevice. Drop the ToString/ReflectionPropertyOrAttribute variants. This is a behavior change for inspect_network_object and write resolution when a SubnetId is unreadable. It needs resolver tests and a live subnet regression.
- **Risk:** Behavior change: inspect_network_object and the write planner's worker resolution would report target_evidence_mismatch instead of a false unique match or target_not_found when some SubnetId is unreadable. A non-string SubnetId would no longer match via ToString(). Needs updated resolver tests and a live network_read/network_write subnet regression.
- **Verifier notes:** The core inconsistency is real. NetworkObjectSelectorResolver.MatchSubnet (588-611) uses TryReadStringAttribute(..., out _), skips unreadable candidates and enumerates project.Subnets without a guard, so it can return a false unique match or target_not_found. MatchDevice (561-572) returns EvidenceMismatch on any blank name. SubnetLifecycleService.FindMatches/ReadSubnetId (277-310) uses GetAttribute(...)?.ToString() and counts unreadables. NetworkDeviceConfigurator.FindExactlyOneSubnet (183-216) uses OpennessReflection.ReadPropertyOrAttribute with an unreadable flag and ClassifyDependencySelection. NetworkMutationVerifier.VerifySubnet (94-101) throws, which gives unverified. Two cited locations are overcounted: HardwarePageCandidateSourceFactory.cs:120-123 and NetworkObjectIndexReader.ReadSubnetId (442-454) are per-subnet identity reads that use the same strict typed semantics as ReadExactStringIdentityAttribute. They are not id lookups and do not conflict.

### WORKERNET-3 — Worker-only probes (~1,900 lines) have no in-repo caller and are documented as temporary

- **Verdict:** partially-confirmed · **Severity:** high · **Category:** dead-code · **Effort:** M · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs:1-713`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationProbeService.cs:1-595`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationEvidence.cs:1-346`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeProbeInfo.cs:1-25`
  - `TiaMcpServer.OpennessWorker/Program.cs:173-175,418-609 ProbeNetworkObjectAttributes/ProbeIoSystemQualification/ProbeSubnetLifecycleMutations`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs:1-207`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:375-394 Probe* fields`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:55-57,105-106 StandaloneOnlyProbes`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:108,177-178`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:16-23 ResolveQualificationDeviceItem`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:16-17 Read(ProjectBase) overload`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationProbeService.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/IoSystemQualificationEvidence.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeProbeInfo.cs`
  - `TiaMcpServer.OpennessWorker/Program.cs:173-175`
  - `TiaMcpServer.Contracts/Network/IoSystemQualificationProbeInfo.cs`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:16-23`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:16-17`
  - `docs/ARCHITECTURE.md:851,923-945`
- **Evidence:** A repo-wide grep (.cs/.ps1, excluding the ignored .superpowers/) finds probe_io_system_qualification, probe_subnet_lifecycle_mutations and probe_network_object_attributes only in Contracts catalogs, the worker switch and tests. The host never sends them. docs/ARCHITECTURE.md:851,895,925 calls them 'worker-only', 'internal-only evidence probe' and 'temporary worker-only diagnostic'. Their live evidence runs are dated 2026-09-24/26. SubnetLifecycleMutationProbeService.SaveProject (435-464) calls project.Save() outside the lifecycle pipeline and audit. Remaining callers: ResolveQualificationDeviceItem is used only by IoSystemQualificationProbeService, and HardwareConfigReader.Read(ProjectBase) only by SubnetLifecycleMutationProbeService (37, 294, 338). The probes are kept only by tests, mostly source-text assertions (IoSystemQualificationWorkerContractTests, ActiveProjectContextSourceTests:32).
- **Proposal:** Ask the maintainer to retire the probes. If they agree, delete the three probe services, IoSystemQualificationEvidence, NetworkAttributeProbeInfo, the Contracts DTO/validator, the WorkerRequest Probe*/IoSystemQualification fields, the catalog entries, the StandaloneOnlyProbes list (ProjectCapabilityCatalog then needs no probe exception), the Program.cs handlers, ResolveQualificationDeviceItem, the single-arg HardwareConfigReader.Read overload and the related tests. Update ARCHITECTURE.md §Phase 4/5 and AGENTS.md line 50.
- **Verifier-corrected proposal:** Do not delete as dead code. Record a retirement follow-up, tied to the documented trigger (public update_io_system acceptance and final live-harness cleanup), in docs/IMPROVEMENT_LOG.md. When the maintainer retires them, remove the listed services and DTOs, WorkerRequest Probe* fields, catalog entries, StandaloneOnlyProbes, the Program.cs handlers, ResolveQualificationDeviceItem, the single-arg Read overload and the related tests, in one change. In the same change, update ARCHITECTURE.md (§Phase 3/4/5 probe sections, ~851, 885-898, 923-945), AGENTS.md (PR6 paragraph) and NETWORK_PHASE4_SUBNET_LIFECYCLE.md. Keep NetworkAttributeMetadataProcessor/EngineeringAttributeInspector.
- **Risk:** The maintainer may still run these through a private direct-stdio harness for future qualification; confirm before deleting. No public MCP contract changes. AGENTS.md/ARCHITECTURE.md text must be updated in the same change. Tests that assert probe source text must be removed.
- **Verifier notes:** Facts are correct. The probe methods are dispatched only at Program.cs:173-175. The host never sends them; the only other references are Contracts catalogs and tests. ResolveQualificationDeviceItem is used only by IoSystemQualificationProbeService (202, 308, 451). The single-arg HardwareConfigReader.Read(ProjectBase) is used only by SubnetLifecycleMutationProbeService (37, 294, 338). This is a documented, deliberate temporary surface, not accidental dead code. docs/ARCHITECTURE.md:944 says 'Retire the probe with the final live-harness cleanup after the later public acceptance work' (update_io_system is not shipped yet). AGENTS.md documents StandaloneOnlyProbes as a design decision. The probes are still driven by local stdio harness scripts in the git-ignored artifacts/live-network-phase5-pr2/*.py (.gitignore:29), which a repo-wide .cs/.ps1 grep misses. NetworkAttributeMetadataProcessor must stay because EngineeringAttributeInspector.cs:11 uses it.

### WORKERNET-4 — read_hardware_config node selectors computed in ReadNode are always overwritten, and the per-device Apply pass is redundant

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:372-375,399-400,415-418 ReadNode`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:177 ReadDevice`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:50 Read`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:74 ReadDevicePageCandidate`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkNodeReadSelectorBuilder.cs:27-64 ApplyInventory/Apply`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkSelectorFactory.cs:77-109 Node`
- **Evidence:** ReadNode sets nodeInfo.Selectable, SelectorDiagnostics and Selector = NetworkSelectorFactory.Node(deviceName, nodeId). NetworkNodeReadSelectorBuilder.Apply then unconditionally reassigns Selectable/Selector/SelectorDiagnostics for every node of the device. Apply runs twice on each path: ReadDevice:177, then ApplyInventory (Read:50) or ApplyPage (ReadDevicePageCandidate:74), and the outer call always wins. HardwareConfigReader:417 is the only production caller of NetworkSelectorFactory.Node. Its itemPath/nodeIndex parameters are used only by NetworkSelectorFactoryTests:18-19,190, and the legacy item-path node form is only consumed (ResolveIndexedNode), never produced.
- **Proposal:** Remove the selector/diagnostic assignment in ReadNode (keep NodeId/Name evidence), remove the Apply call at ReadDevice:177, and let the two outer calls own certification. Then either delete NetworkSelectorFactory.Node or reduce it to the parameters tests need, keeping ResolveIndexedNode for legacy input.
- **Risk:** None at runtime because the output is already overwritten. NetworkHardwareConfigDeterminismTests:139 builds a Node(...) selector fixture and would need QualifiedNode. Check that no test asserts the intermediate per-device diagnostic text.
- **Verifier notes:** ReadNode (HardwareConfigReader.cs:372-375, 399-400, 415-418) sets Selectable/SelectorDiagnostics/Selector via NetworkSelectorFactory.Node. NetworkNodeReadSelectorBuilder.Apply (NetworkNodeReadSelectorBuilder.cs:34-60) unconditionally reassigns all three for every node of every interface. ReadDevice:177 calls Apply, and then the outer ApplyInventory (Read:50, iterating all inventory.Devices) or ApplyPage (ReadDevicePageCandidate:74 => Apply(device, verified && complete)) overwrites the result again. The only production caller of NetworkSelectorFactory.Node is HardwareConfigReader.cs:417. The other callers are NetworkHardwareConfigDeterminismTests.cs:139 and NetworkSelectorFactoryTests.cs:18-19, 190, 204, 212.

### WORKERNET-5 — list_network_objects cursors are unsigned and worker-encoded, unlike every sibling paged read

- **Verdict:** confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectCursorCodec.cs:94-159 Encode/Decode`
  - `TiaMcpServer.OpennessWorker/Program.cs:342-361`
  - `TiaMcpServer/Network/HardwarePageCursorCodec.cs:20-50`
  - `TiaMcpServer/Hmi/HmiPageCursorCodec.cs:30`
  - `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs`
- **Evidence:** NetworkObjectCursorCodec.Encode emits base64url(JSON{offset, queryHash, snapshotHash}) with no MAC. Decode trusts a client-edited offset as long as both hashes match. read_hardware_config pages on the same network_read tool use the host's HardwarePageCursorCodec over AuthenticatedCursorProtector. HMI (HmiPageCursorCodec) and project tree use host-sealed codecs as well. Category names match (invalid_cursor, cursor_filter_mismatch, cursor_snapshot_mismatch, cursor_out_of_range), but tamper resistance, process scoping and binding-revision checks do not.
- **Proposal:** Move list_network_objects pagination to the host pattern. The worker returns offset/total/snapshotHash and the host seals the cursor with AuthenticatedCursorProtector and a 'network-objects' purpose, as HardwarePaginationCoordinator does. Then delete NetworkObjectCursorCodec's encoding/validation and NetworkCursorException. At minimum, document the deliberate difference in NETWORK_OPERATIONS_SUMMARY.md.
- **Risk:** The cursor format changes, so old cursors become invalid_cursor (acceptable, since they are opaque). Host/worker payload contract (NetworkPayloadContract list decode) and its tests change. Needs a FakeWorker update.
- **Verifier notes:** Program.cs:349-361 decodes the cursor in the worker via NetworkObjectCursorCodec.Decode (NetworkObjectCursorCodec.cs:107-159). The cursor is plain base64url JSON {version, offset, queryHash, snapshotHash} with no MAC. The host forwards operation.Cursor unchanged (NetworkWorkerInvoker.cs:26). read_hardware_config pages use the host HardwarePageCursorCodec over AuthenticatedCursorProtector (HardwarePageCursorCodec.cs:25-34), and HMI does the same (HmiPageCursorCodec.cs:36-38). NETWORK_OPERATIONS_SUMMARY.md:178-187 documents authentication and cursor_binding_mismatch only for hardware pagination. The list section (82-85) only says 'opaque', so the difference is undocumented. Impact is limited: a forged offset only selects another page of the same read-only snapshot. There is no binding-revision check, though.

### WORKERNET-6 — Selector resolver and dependency lookups disagree on unreadable IO-system candidates; resolver silently skips them

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:276-316 ResolveIoSystem`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:248-294 FindExactlyOneIoSystem/ReadIntPropertyOrAttribute`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkPostconditionChecks.cs:10-15 ClassifySelection/ClassifyDependencySelection`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:276-316`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:47,218-294`
- **Evidence:** ResolveIoSystem catches EngineeringException per candidate and drops it ('An unreadable identity can never satisfy the selector', 302-305). One readable match plus one unreadable duplicate is therefore reported as a unique success, and all-unreadable becomes target_not_found. NetworkDeviceConfigurator.FindExactlyOneIoSystem treats any unreadable or negative Number as incomplete and fails worker_operation_failed. It also reads Number by reflection plus string parsing (ReadIntPropertyOrAttribute, 283-294), although typed IoSystem.Number is available and used at NetworkObjectSelectorResolver:288 and HardwareConfigReader:569. The NetworkPostconditionChecks doc comment says 'A partial discovery cannot prove either uniqueness or absence', which the resolver violates.
- **Proposal:** In ResolveIoSystem, count unreadable candidates and return EvidenceMismatch when any exist, matching MatchDevice. In NetworkDeviceConfigurator, read candidate.Number directly (typed, guarded by the WORKERNET-1 TypedIdentityInt) and delete ReadIntPropertyOrAttribute. Type the ioSystem local/parameters as IoSystem instead of object (47, 220, 248).
- **Verifier-corrected proposal:** In ResolveIoSystem, count unreadable candidates among those not excluded by the IoSystemIndex constraint, and return EvidenceMismatch if any exist (consistent with MatchDevice/MatchNode). In FindExactlyOneIoSystem, read candidate.Number directly inside a guarded typed read (shared helper from WORKERNET-1) and delete ReadIntPropertyOrAttribute. Change `object ioSystem` to IoSystem together with WORKERNET-9's typed ConnectToIoSystem call. Needs resolver unit coverage and a live IO-system inspect/configure regression.
- **Risk:** inspect_network_object and the write-target resolution of IO systems may now fail with target_evidence_mismatch where they used to succeed. Needs resolver unit coverage and a live IO-system read/configure regression.
- **Verifier notes:** Confirmed: ResolveIoSystem (NetworkObjectSelectorResolver.cs:276-306) swallows EngineeringException per candidate. One readable match plus one unreadable duplicate becomes a unique Ok, and all-unreadable becomes NotFound. NetworkDeviceConfigurator.FindExactlyOneIoSystem (248-281) treats unreadable or negative numbers as incomplete and fails worker_operation_failed. It reads Number through reflection plus int.TryParse (ReadIntPropertyOrAttribute 283-294), although candidate is already typed IoSystem in the foreach (253) and typed .Number is used at resolver:288. The proposal overreaches in one detail. When target.IoSystemIndex is set, other indices are skipped before any read (283-286), so the index pins the candidate. Unreadable siblings should only count when no index constraint applies. Also, `object` typing (47, 220, 248) exists because InvokeFirstAvailable takes object, so typing it depends on WORKERNET-9.

### WORKERNET-7 — Broad catch (Exception) in network reads absorbs NonRecoverableException (Portal loss), unlike CrossReferences/VCI

- **Verdict:** confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:167,192,196,204,569,741 (ReadOptionalString),790`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkNodeReadSelectorBuilder.cs:89 MatchNode`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareDiscoveryEvidenceCapture.cs:33-42 Traverse`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:57,163`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeMetadataProcessor.cs:55-318`
  - `TiaMcpServer.OpennessWorker/Openness/Network/CommunicationConnectionReader.cs:186-200`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:30`
- **Evidence:** In the stubs, NonRecoverableException derives from System.Exception, not EngineeringException (reference-stubs/Siemens.Engineering.Base/Core.cs:164). Network catch (Exception) blocks therefore turn Portal loss into target_evidence_mismatch ('namespace could not be read completely'), a diagnostics entry, or an 'unverified' check, and execution continues against a dead session. CrossReferenceTargetResolver.cs:235-236 (IsRecoverable) and VciReader.cs:88 (catch (NonRecoverableException) { throw; }) rethrow explicitly, and AGENTS.md describes portal_lost invalidating binding for VCI. Elsewhere in Network the filters are EngineeringException-only, so the policy is mixed.
- **Proposal:** Define one recoverable-exception predicate (for example OpennessExceptions.IsRecoverable(ex) => ex is not NonRecoverableException, in Openness/Infrastructure). Make the shared helper from WORKERNET-1, Traverse, MatchNode and the verifier Observe use `catch (Exception ex) when (OpennessExceptions.IsRecoverable(ex))`, so Portal loss reaches Program.Execute (Program.cs:1552).
- **Risk:** Portal loss mid-read becomes a hard worker failure instead of a degraded success. That is the intended direction, but the change is user-visible. Simulated-exception unit tests are feasible; live crash verification is optional.
- **Verifier notes:** reference-stubs/Siemens.Engineering.Base/Core.cs:164 declares NonRecoverableException : System.Exception. Worker Program.cs:1548/1552 catches EngineeringException before NonRecoverableException, which would be CS0160 against real DLLs if it were a subclass, so it is not one. Network catch (Exception) blocks therefore absorb Portal loss: HardwareDiscoveryEvidenceCapture.Traverse (34, 40), NetworkNodeReadSelectorBuilder.MatchNode (89), NetworkObjectSelectorResolver (167, 192, 196, 569, 726, 743, 759, 794), NetworkMutationVerifier (60, 163), NetworkConnectionEvidenceCapture (58-120), CommunicationConnectionReader (76, 124, 195, 208) and HardwareConfigReader (30, 777). Other domains rethrow explicitly: CrossReferenceTargetResolver.cs:236 IsRecoverable, CrossReferenceReader.cs:195, CompileChecker.cs:48/200, HmiReadLog.IsRecoverable (108), and VciReader/VciScopeResolver/VciWriter `catch (NonRecoverableException) { throw; }`. A shared predicate partly exists already (HmiReadLog.IsRecoverable, CrossReferenceTargetResolver.IsRecoverable), so the network change should reuse or hoist one of them rather than add a third.

### WORKERNET-8 — Device-filter selection and subnet enumeration duplicated between unpaged and paged hardware reads, with different guarding

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:112-145 SelectDevices`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:46-48 Read (subnet Traverse)`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateSourceFactory.cs:53-131 Create`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:88-145`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateSourceFactory.cs:53-104`
  - `TiaMcpServer.Tests/Network/HardwareDeviceSelectionTests.cs:39-115`
- **Evidence:** Both paths re-implement the same filter: enumerate devices, read the name with ReadTypedIdentityString, take the ordinal-ignore-case exact-one match, otherwise emit the identical strings 'No device named ... no devices are reported.' and 'More than one device matches ... ambiguous.' The unpaged path wraps enumeration in capture.Traverse and records failures. The paged path calls ProjectDeviceEnumerator.EnumerateWithLocations and `foreach (Subnet subnet in project.Subnets)` (117) without guarding, so an EngineeringException during enumeration fails the whole page while the unpaged read degrades. Namespace verification also differs: capture.Complete && all usable (125) versus per-name DeviceNameIsUnique (113).
- **Proposal:** Extract a single static SelectDeviceCandidates(project, deviceName, messages) returning candidates plus a namespaceVerified flag, and a shared subnet descriptor enumeration. Call both from HardwareConfigReader.Read and HardwarePageCandidateSourceFactory. Use capture.Traverse-style guarding (or an explicit decision) on the paged path. Also inline the pass-through ResolvePageTagIndex (88-91).
- **Verifier-corrected proposal:** Extract only the shared name filter: a static FilterByDeviceName(candidates, deviceName, out string? failureMessage) used by both SelectDevices and the page factory. Keep the paged path's fail-closed enumeration and state that this is deliberate in a comment. Optionally replace the ResolvePageTagIndex pass-through by making ResolveTagIndex internal. Update the HardwareDeviceSelectionTests source-text assertions in the same change.
- **Risk:** Paged read behavior under enumeration faults changes (degrade instead of fail) unless the current behavior is deliberate; confirm against the HARDWARE paging contract docs. Pagination tests cover ordering and should stay green.
- **Verifier notes:** The duplication is real. HardwareConfigReader.SelectDevices (112-145) and HardwarePageCandidateSourceFactory.Create (53-104) repeat the same ordinal-ignore-case filter and the two identical message strings. The paged path enumerates ProjectDeviceEnumerator.EnumerateWithLocations and project.Subnets (117) without Traverse guarding. The proposal to make the paged path 'degrade instead of fail' is questionable. The factory doc comment (HardwarePageCandidateSourceFactory.cs:12-16) says 'Descriptor traversal stays complete', and the descriptor set feeds the cursor snapshot hash. Degrading would allow cursors over an incomplete snapshot, so failing closed is plausibly deliberate. The namespace-verified semantics also differ by design: per-name DeviceNameIsUnique over all names versus complete && all-usable, followed by ApplyInventory's own uniqueness check. Source-text tests pin the current code: HardwareDeviceSelectionTests asserts the SelectDevices body text, the message strings and 'private static IoTagIndex? ResolveTagIndex(' inside HardwareConfigReader.cs, so extracting or inlining breaks them.

### WORKERNET-9 — Reflection-based 'UNVERIFIED SDK CALL' connect helpers in a live-accepted write path

- **Verdict:** confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:155-176 ConnectSubnet`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:218-242 ConnectIoSystem`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:306-332 InvokeFirstAvailable`
  - `reference-stubs/Siemens.Engineering.Base/Hardware.cs:129,186 IoConnector/Node stubs`
- **Evidence:** network_write configure_network_device still connects nodes and IO connectors through InvokeFirstAvailable(target, ["ConnectToSubnet","Connect"], arg), using reflection plus TargetInvocationException unwrapping. The comments read 'UNVERIFIED SDK CALL ... may be ... or equivalent', although the path has been live-accepted (network_write live defects PR). The 'Connect' fallback name is speculative. Reflection hides signature drift until runtime and needs three catch clauses per call. The reference stubs declare Node and IoConnector without ConnectToSubnet/ConnectToIoSystem, which is probably why typed calls were avoided.
- **Proposal:** Add `ConnectToSubnet(Subnet)` and `ConnectToIoSystem(IoSystem)` to the reference stubs. Replace InvokeFirstAvailable with typed calls, delete InvokeFirstAvailable and the TargetInvocationException catches, and update the comments to cite the verified V21 API.
- **Risk:** Confirm the exact V21 signatures against the real Siemens.Engineering.dll or the Openness docs before the stub change. Needs a live configure_network_device regression (subnet plus IO-system attach).
- **Verifier notes:** NetworkDeviceConfigurator.ConnectSubnet (155-176) and ConnectIoSystem (218-242) call InvokeFirstAvailable (306-332) with the speculative names ["ConnectToSubnet","Connect"]/["ConnectToIoSystem","Connect"]. Each call has three catch clauses, including TargetInvocationException unwrapping, under 'UNVERIFIED SDK CALL' comments. A grep of reference-stubs finds no ConnectTo* members; Node is declared at reference-stubs/Siemens.Engineering.Base/Hardware.cs:186 and IoConnector at 129. No test pins InvokeFirstAvailable source text. As the finding says, the real V21 signatures must be confirmed against the Openness docs or DLL before the stubs are changed.

### WORKERNET-10 — Verification status and network setting-key literals duplicated across worker and host with no shared constant

- **Verdict:** confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkPostconditionChecks.cs:32-51 Compare/Complete`
  - `TiaMcpServer/Network/NetworkPayloadContract.cs:286-296`
  - `TiaMcpServer/Network/NetworkWriteVerifier.cs:175,258`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:68-73,86,159,228`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:60-84,102,128`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs:20-21`
  - `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs:16-17`
- **Evidence:** "passed"/"failed"/"unverified"/"not_required" are hand-typed in worker NetworkPostconditionChecks and in host NetworkPayloadContract/NetworkWriteVerifier/NetworkWritePayloadBudget. The worker Complete() roll-up (45-48) is re-implemented host-side (NetworkPayloadContract:294-296). The sparse setting keys Address/SubnetMask/PnDeviceName/PnDeviceNameAutoGeneration/Subnet/IoSystem, which AGENTS.md documents as a contract, are string literals in NetworkDeviceConfigurator, NetworkMutationVerifier, NetworkWritePlanner and NetworkPayloadContract. "System:Subnet.Ethernet"/"System:Subnet.Profibus" are declared twice as private consts, and NetworkMutationVerifier:102 builds "System:Subnet." + type a third way. SubnetLifecycleContract (Contracts) already holds Ethernet/Profibus.
- **Proposal:** Add NetworkVerificationStatuses and NetworkSettingKeys const classes to TiaMcpServer.Contracts/Network, and SubnetLifecycleContract.EthernetTypeIdentifier/ProfibusTypeIdentifier plus TypeIdentifierFor(networkType). Replace the literals on both sides. Optionally move the status roll-up into a Contracts helper used by both NetworkPostconditionChecks.Complete and NetworkPayloadContract.
- **Risk:** Pure refactor with no wire change. Tests compile against the same strings.
- **Verifier notes:** Status literals are hand-typed in the worker: NetworkPostconditionChecks.Compare/Complete (32-51). They are hand-typed again on the host: NetworkPayloadContract.cs:286-296, where the same failed>unverified>not_required>passed roll-up is re-derived. That roll-up is a trust check, but it can share a Contracts helper. "System:Subnet.Ethernet"/"System:Subnet.Profibus" are private consts in both SubnetLifecycleService.cs:20-21 and SubnetLifecycleMutationProbeService.cs:16-17. "System:Subnet." + type is built in NetworkMutationVerifier.cs:112, NetworkPayloadContract.cs:254 and NetworkWritePlanner.cs:162. The setting-key literals Address/SubnetMask/PnDeviceName/PnDeviceNameAutoGeneration/Subnet/IoSystem appear in NetworkDeviceConfigurator.cs:68-73, 86, 159, 228 and in NetworkMutationVerifier.cs:63-80.

### WORKERNET-11 — HardwareConfigReader (821 lines) and NetworkObjectSelectorResolver (885 lines) exceed size budget; identity helpers live in a reader

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:504-749 RequireSubnetIdentity/ReadConnectedNodeIdentity/ReadIoSystemIdentity/FindParentDeviceName/ReadTypedIdentityString/ReadExactStringIdentityAttribute`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:341-497 ResolveCommunicationConnection (157 lines)`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:80,95-100,124-126,133`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateSourceFactory.cs:57,120`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareConfigReader.cs:504-749`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:341-497`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs:80,97,100,133`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateSourceFactory.cs:57,120`
- **Evidence:** NetworkMutationVerifier, HardwarePageCandidateSourceFactory and NetworkConnectionEvidenceCapture callers reach into HardwareConfigReader for identity primitives (RequireSubnetIdentity, ReadIoSystemIdentity, ReadConnectedNodeIdentity, ReadTypedIdentityString, ReadExactStringIdentityAttribute), so the write verifier depends on the read projection class. ReadConnectedNodeIdentity (511-542) calls back into NetworkObjectSelectorResolver.ResolveNode, a reader-to-resolver cycle. ResolveCommunicationConnection is 157 lines of sequential evidence checks.
- **Proposal:** After WORKERNET-1/2, move the identity primitives into a NetworkIdentityReader static class, which brings HardwareConfigReader to about 550 lines. Split ResolveCommunicationConnection into VerifyConnectionIdentity(connection, target) returning a failure or the (type, name, id) tuple, plus evidence building.
- **Verifier-corrected proposal:** After WORKERNET-1/2, move RequireSubnetIdentity, ReadConnectedNodeIdentity, ReadIoSystemIdentity, FindParentDeviceName, ReadTypedIdentityString and ReadExactStringIdentityAttribute into a NetworkIdentityReader static class, and update the callers (NetworkMutationVerifier, HardwarePageCandidateSourceFactory, HardwareIoMapReader, probe service) and the HardwareDeviceSelectionTests source-text assertions. Optionally split ResolveCommunicationConnection into an identity-verification step and an evidence-building step. Low priority, mechanical.
- **Risk:** Mechanical move; source-text tests that grep for 'HardwareConfigReader.RequireSubnetIdentity' etc. would need updating.
- **Verifier notes:** The sizes are real: HardwareConfigReader.cs is 821 lines and NetworkObjectSelectorResolver.cs is 885 lines. ResolveCommunicationConnection spans roughly 341-497. NetworkMutationVerifier (80, 97, 100, 133) and HardwarePageCandidateSourceFactory (57, 120) do reach into HardwareConfigReader for identity primitives. Two claims are wrong. There is no reader-to-resolver cycle: NetworkObjectSelectorResolver has no reference to HardwareConfigReader, so HardwareConfigReader.ReadConnectedNodeIdentity:538 → ResolveNode is one-way. NetworkConnectionEvidenceCapture is not a caller of HardwareConfigReader; the actual callers are HardwareIoMapReader, HardwarePageCandidateSourceFactory, NetworkMutationVerifier, SubnetLifecycleMutationProbeService and Program.cs. HardwareDeviceSelectionTests also reads HardwareConfigReader.cs source text, so moving code affects those tests.

### WORKERNET-12 — Unused NodeAt and hand-rolled ItemAt/ConnectionAt index walkers

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:634-653 NodeAt`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:613-632 ItemAt`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:655-674 ConnectionAt`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectSelectorResolver.cs:613-674`
  - `TiaMcpServer.Tests/Network/NetworkIntrospectionWorkerDispatchTests.cs:88`
- **Evidence:** A whole-repo word count shows NodeAt occurring once, at its definition. It is a leftover of the legacy index-based node selection that ResolveIndexedNode now routes through ResolveQualifiedNode. ItemAt and ConnectionAt are 20-line copies of the same loop.
- **Proposal:** Delete NodeAt. Replace ItemAt/ConnectionAt with `index < 0 ? null : items.Cast<DeviceItem>().ElementAtOrDefault(index)` (and the same for Connection), or one generic private ElementAtOrNull<T>(IEnumerable, int).
- **Verifier-corrected proposal:** Delete NodeAt. Optionally collapse ItemAt/ConnectionAt into a generic `ElementAtOrNull<T>(IEnumerable, int)` while keeping the `ItemAt(siblings, requestedSegment.Index)` call site (as a thin wrapper or rename) or updating NetworkIntrospectionWorkerDispatchTests.cs:88 in the same change.
- **Risk:** None; enumeration-order and exception behavior are identical.
- **Verifier notes:** NodeAt (NetworkObjectSelectorResolver.cs:634-653) has no caller: a repo-wide search shows only its definition. ItemAt is used at 518 and ConnectionAt at 371, and they are duplicated loops. However, TiaMcpServer.Tests/Network/NetworkIntrospectionWorkerDispatchTests.cs:88 asserts the source text "ItemAt(siblings, requestedSegment.Index)". Replacing the call with an inline ElementAtOrDefault expression breaks that test, so the 'None' risk is understated.

### WORKERNET-13 — Three guarded-enumeration implementations with different MoveNext-failure semantics

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareDiscoveryEvidenceCapture.cs:24-43 Traverse`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeMetadataProcessor.cs:245-322 (guarded enumerator loop with MaxConsecutiveEnumerationFailures)`
  - `TiaMcpServer.OpennessWorker/Openness/Infrastructure/OpennessReflection.cs:117-168 Enumerate`
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkConnectionEvidenceCapture.cs`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Network/NetworkAttributeMetadataProcessor.cs:243-322`
  - `TiaMcpServer.OpennessWorker/Openness/Infrastructure/OpennessReflection.cs:117-168`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareDiscoveryEvidenceCapture.cs:24-43`
- **Evidence:** Traverse stops at the first enumeration failure and records evidence. NetworkAttributeMetadataProcessor retries MoveNext up to a consecutive-failure cap and guards Current/Dispose separately. OpennessReflection.Enumerate yields until an EngineeringException and logs to stderr. All three answer the same question (enumerate an Openness collection without losing siblings) in one worker with different completeness outcomes.
- **Proposal:** Pick Traverse's semantics as the Network default. Have NetworkAttributeMetadataProcessor use Traverse (or an overload taking a diagnostics sink) unless the retry-on-MoveNext behavior is a documented Siemens quirk. If it is, add a one-line comment naming why it differs.
- **Verifier-corrected proposal:** Do not unify. At most, add a one-line comment on ForEachFaultIsolated and on OpennessReflection.Enumerate stating why their semantics differ from Traverse (fault-isolated attribute inspection, and best-effort catalog search). Also consider having OpennessReflection.Enumerate not swallow NonRecoverableException (see WORKERNET-7). Effort S, negligible value.
- **Risk:** Attribute-metadata diagnostics under a flaky MoveNext could change. Low impact and covered by NetworkAttributeMetadataProcessor tests.
- **Verifier notes:** Three implementations exist. HardwareDiscoveryEvidenceCapture.Traverse (24-43) stops at the first enumeration failure and records evidence. NetworkAttributeMetadataProcessor.ForEachFaultIsolated (243-322) retries MoveNext up to MaxConsecutiveEnumerationFailures=3 (line 39) and guards Current and Dispose separately. OpennessReflection.Enumerate (117-168) stops on EngineeringException and logs to stderr. They do not answer the same question. OpennessReflection.Enumerate is used only for best-effort equipment-catalog walking (EquipmentCatalogSearcher.cs:112, 165), not hardware. The metadata processor serves per-attribute fault isolation for inspect attributes (via EngineeringAttributeInspector.cs:11), where skipping a bad entry and continuing is the documented intent ('An unknown or failed attribute does not fail the inspection', NETWORK_OPERATIONS_SUMMARY.md:120-121). Traverse feeds completeness evidence. Forcing Traverse semantics on the metadata processor would drop later attributes after one bad MoveNext and contradict the documented behavior.

## WORKERDOMAINS

No file is over 800 lines. The main problems are copy-paste between sibling domains: resolvers for blocks and PLC data types, PLC/HMI/VCI code that walks device items to find software, tag folder-path helpers, temp-directory cleanup, block node-type mapping, and HMI create-with-cleanup. The most serious defect is a lookup mismatch. Every write resolves its PLC through PlcSoftwareLocator.FindUnique, which searches all devices including grouped ones. CompileChecker (used by compile_check and by every block/type write postcondition) uses FindAll instead, which only sees root devices and takes the first match. There are also dead parts left over from the removed safety snapshot (ExportForSafety, requireAuthoritativeXml), test-only legacy overloads, a public isSafety parameter that always fails, and different error categories for the same situation: InvalidOperationException ends up as worker_operation_failed, session loss is portal_lost only in VCI, and HMI writes lose the original error category when they re-wrap it.

### WORKERDOMAINS-1 — CompileChecker resolves PLCs with root-only, first-match FindAll; writes use the all-devices, ambiguity-checked FindUnique

- **Verdict:** partially-confirmed · **Severity:** high · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:98 CompileBlock (FindAll(...).FirstOrDefault())`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:115 'compiled using the first PLC found'`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:146 CompilePlcSoftware (FindAll)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:90-93 FindAll => Discover(project.Devices)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:21-49 FindUnique (ProjectDeviceEnumerator.Enumerate)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:74 VerifyCreatedBlockPostconditions -> CompileChecker.Compile`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs:48 BuildEvidence -> CompileChecker.Compile(plcName, null)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs:258 ObservePostconditions -> CompileChecker.CompileObserved`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareTagIndexResolver.cs:21 FindAll(project, null)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:98-116,146,161`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:90-93`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareTagIndexResolver.cs:21-52`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:74`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs:48`
  - `TiaMcpServer.Tests/Block/BlockImporterRouteSourceTests.cs:59-60`
  - `docs/IMPROVEMENT_LOG.md:146,225-226`
- **Evidence:** FindAll walks only project.Devices. FindUnique and FindEveryPlc use ProjectDeviceEnumerator.Enumerate, which also covers device groups and ungrouped devices. So create_block, update_block_logic or a PLC type update on a PLC inside a device group gets resolved and mutated through FindUnique. Its compile postcondition then goes through FindAll, finds nothing, and throws InvalidOperationException('No matching PLC software...'), which becomes failed evidence or postcondition_failed after the mutation already happened. compile_check on a grouped PLC fails the same way. When there are several PLCs and plcName is null, CompileBlock silently picks the first one instead of returning target_ambiguous, which is what every write does. PlcTypePostconditionVerifier with a null PlcName compiles every root PLC, not just the target.
- **Proposal:** In CompileBlock, use PlcSoftwareLocator.FindUnique(project, address.PlcName). In CompilePlcSoftware, use FindUnique when plcName is set, and a strict all-devices enumeration (FindEveryPlc, or the shared walker from WORKERDOMAINS-2) when it is null. Drop the 'first PLC found' note. Switch HardwareTagIndexResolver the same way. Then delete PlcSoftwareLocator.FindAll and remove the source-text assertions in TiaMcpServer.Tests/Block/BlockImporterRouteSourceTests.cs:59-60 that pin the FindAll call.
- **Verifier-corrected proposal:** In CompileChecker.CompileBlock, use PlcSoftwareLocator.FindUnique(project, address.PlcName). That gives target_not_found/target_ambiguous and lets you drop the 'first PLC found' note. In CompilePlcSoftware, use FindEveryPlc(project, plcName). Pass a failure sink or strict enumeration so unreadable device items are not hidden, and keep the 'compile every PLC when plcName is null' scope. In HardwareTagIndexResolver.Resolve, replace FindAll with FindEveryPlc and keep its message-not-throw selection. Then delete FindAll, update BlockImporterRouteSourceTests.cs:59-60, and close IMPROVEMENT_LOG M5 plus the Network line 146 entry. Needs live verification with a PLC in a device group, covering compile_check, one block write and one type write.
- **Risk:** Behavior change: an unqualified compile/write against a multi-PLC project now fails with target_ambiguous instead of compiling the first PLC. compile_check results change for grouped PLCs (they start working). Needs a live check against a project with a PLC in a device group, covering compile_check plus one block write and one type write.
- **Verifier notes:** Defect is real. PlcSoftwareLocator.cs:90-93 FindAll walks only project.Devices. FindUnique (21-49) and FindEveryPlc (100-106) use ProjectDeviceEnumerator.Enumerate. CompileChecker.cs:98 uses FindAll(...).FirstOrDefault() and line 146 uses FindAll. The 'first PLC found' note is at line 115. BlockMutationService.cs:74 and PlcTypePostconditionVerifier.cs:48 compile through CompileChecker.Compile after a FindUnique-resolved mutation. The bug is not new: it is already logged as open follow-up M5 in docs/IMPROVEMENT_LOG.md:225-226, and the Network side at IMPROVEMENT_LOG.md:146. The source-pin tests at TiaMcpServer.Tests/Block/BlockImporterRouteSourceTests.cs:59-60 exist as cited. Part of the proposal is wrong. HardwareTagIndexResolver.cs:21-52 deliberately does its own selection: it uses Ordinal matching, and when there are zero or several PLCs it adds a message and returns null instead of failing network_read. Switching it to FindUnique would throw target_not_found/target_ambiguous and fail the whole read. It should move to FindEveryPlc and keep its message-based selection.

### WORKERDOMAINS-2 — Five separate walkers find software in device items, each with its own coverage and failure policy

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:54-87 EnumerateStrict/FindInDeviceItemsStrict`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:135-164 FindInDeviceItems (Console.Error + optional sink)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:130-133 FindInDevice (failures only to stderr)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiSoftwareLocator.cs:209-280 Walk/WalkItems`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciScopeResolver.cs:187-196 SoftwareIn (comment: shared locators swallow or wrap failures)`
  - `TiaMcpServer.OpennessWorker/Openness/CrossReferences/CrossReferenceTargetResolver.cs:151 and CrossReferenceReader.cs:56 use lenient FindInDevice`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs:73-87,130-164`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiSoftwareLocator.cs:209-290`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciScopeResolver.cs:186-196`
  - `TiaMcpServer.OpennessWorker/Openness/CrossReferences/CrossReferenceReader.cs:56,183-200`
  - `TiaMcpServer.OpennessWorker/Openness/CrossReferences/CrossReferenceTargetResolver.cs:151`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectTreeSnapshotWalker.cs:73`
- **Evidence:** The same recursion over DeviceItem.GetService<SoftwareContainer>().Software appears five times. They differ only in which software type they keep (Plc, Hmi, Plc+Hmi) and how they handle failures: strict throw, lenient stderr-only, lenient plus messages, or fatal propagation. VCI wrote its own copy because the shared one 'swallows'. Cross-reference and project-tree walking drop unreadable device items, but the failure only reaches stderr; it is never recorded on the report.
- **Proposal:** Add one internal `SoftwareDiscovery.Walk(DeviceItemComposition, Func<object?, T?> select, DiscoveryFailurePolicy policy, ICollection<string>? sink)` next to ProjectDeviceEnumerator, with policies Strict (throw worker_operation_failed), Collect (sink plus continue) and Propagate. Rebuild PlcSoftwareLocator.FindUnique/FindEveryPlc/FindInDevice, HmiSoftwareLocator.WalkItems and VciScopeResolver.SoftwareIn on it. Delete FindAll (see WORKERDOMAINS-1).
- **Verifier-corrected proposal:** Do the targeted fix first (S): give FindInDevice a failure-sink overload, or a strict variant. Make CrossReferenceReader mark the report incomplete when a device-item read fails, and decide the same for ProjectTreeSnapshotWalker. Consolidating the walkers into one SoftwareDiscovery.Walk with explicit Strict/Collect/Propagate policies is optional (M). If done, it must keep HMI's guarded MoveNext/device-name handling and VCI's fatal propagation exactly.
- **Risk:** The failure semantics of every caller must be kept exactly. VCI needs Propagate, and the HMI strict mode throws through Skip. Existing locator unit tests cover most branches. No contract change.
- **Verifier notes:** The duplication is real. There are recursions in PlcSoftwareLocator.cs:73-87 (strict), PlcSoftwareLocator.cs:135-164 (lenient, stderr plus optional sink), HmiSoftwareLocator.cs:231-280 (strict/collect via Skip, and it also guards MoveNext and device.Name) and VciScopeResolver.cs:187-196 (propagate, with an explicit comment at 186). The copies differ on purpose in which exceptions they guard, so one generic walker is M effort and carries risk. The most valuable part was understated. FindInDevice (PlcSoftwareLocator.cs:130-133) passes a null sink, so it swallows EngineeringException internally. CrossReferenceReader.FanOut (CrossReferenceReader.cs:56) wraps it in Visit/TryRead (183-200), and only an escaping exception marks the report incomplete, so a skipped device item leaves report.IsComplete true. That is a real completeness bug in read_cross_references. CrossReferenceTargetResolver.cs:151 and ProjectTreeSnapshotWalker.cs:73 also use the lenient FindInDevice.

### WORKERDOMAINS-3 — BlockTargetResolver and PlcTypeTargetResolver are near copies; the group-path walk is written three more times

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockTargetResolver.cs:116-133 FindSoftwareUnit`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeTargetResolver.cs:103-120 FindSoftwareUnit (byte-identical)`
  - `BlockTargetResolver.cs:35-89 ResolveForExport/ResolveForImport vs PlcTypeTargetResolver.cs:30-85`
  - `BlockTargetResolver.cs:182-208 EnumerateOwners vs PlcTypeTargetResolver.cs:160-174`
  - `BlockTargetResolver.cs:166-176,210-226 FindLegacyMatches/CollectMatches vs PlcTypeTargetResolver.cs:144-154,176-192`
  - `BlockTargetResolver.cs:140-164 ResolveBlockGroupPath + ResolvedBlockGroupPath (229-242): ResolvedFolderNames never read`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:193-220 FindUserGroupByPath (comment cites a nonexistent 'FindGroupByPath')`
  - `PlcTypeTargetResolver.cs:122-142 FindTypeGroup`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockTargetResolver.cs:116-164,229-242`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeTargetResolver.cs:103-142`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:140-163,193-220`
- **Evidence:** FindSoftwareUnit is the same 18 lines in both files. ResolveForExport and ResolveForImport have identical control flow: deterministic owner, then folder walk, then Find, otherwise legacy matches with 0/1/many outcomes. Only the noun in the message and the Blocks/Types composition differ. ResolvedBlockGroupPath.ResolvedFolderNames is filled but its only consumer, FindBlockGroup, reads `.Group` and nothing else. FindUserGroupByPath repeats the same case-insensitive walk with a different element type.
- **Proposal:** Move FindSoftwareUnit into one shared PLC-unit helper, for example PlcSoftwareLocator.FindUnit(PlcSoftware, name), and call it from both resolvers. Remove ResolvedBlockGroupPath and turn ResolveBlockGroupPath into FindBlockGroup returning the group. In BlockMutationService.DeleteBlockGroup, resolve the parent with BlockTargetResolver.FindBlockGroup, then `parent.Groups.Find(name) as PlcBlockUserGroup`, and delete FindUserGroupByPath. Make the legacy-match/ambiguity flow a small generic helper over (owners, group selector, child selector) only if it stays readable. The unit and group helpers are the low-risk part.
- **Verifier-corrected proposal:** Extract the shared FindSoftwareUnit (for example an internal PlcUnits.Find(PlcSoftware, name)) and call it from both resolvers. Delete ResolvedBlockGroupPath and ResolveBlockGroupPath, keeping FindBlockGroup as the direct walk. Fix the stale comment. For DeleteBlockGroup, either keep FindUserGroupByPath or replace it with FindBlockGroup(parent) followed by an OrdinalIgnoreCase scan of parent.Groups. Keep the existing 'Block group ... not found at ...' message for a missing parent too, catching and rethrowing if needed. Leave the resolver mirror as is unless a generic helper stays readable.
- **Risk:** The ambiguity and not-found messages must stay as they are (tests assert them). Case-insensitive matching is preserved. Groups.Find may be case-sensitive, so keep the explicit OrdinalIgnoreCase scan in any shared walk. No live run is needed if offline resolver tests pass.
- **Verifier notes:** FindSoftwareUnit is byte-identical at BlockTargetResolver.cs:116-133 and PlcTypeTargetResolver.cs:103-120. ResolveForExport/ResolveForImport have the same control flow in both files. ResolvedBlockGroupPath.ResolvedFolderNames (BlockTargetResolver.cs:229-242) is written but never read: search shows its only consumer is FindBlockGroup's `.Group` at line 138. The comment at BlockMutationService.cs:193 cites a nonexistent FindGroupByPath. PlcTypeTargetResolver's own docstring (lines 16-17) says it is a deliberate mirror, so a full generic merge is optional. One correction to the DeleteBlockGroup proposal: resolving the parent through FindBlockGroup changes the missing-parent error from "Block group 'X' was not found at '...'" (BlockMutationService.cs:149-151) to "Block folder '<segment>' not found." (BlockTargetResolver.cs:159), and the proposed Groups.Find may be case-sensitive.

### WORKERDOMAINS-4 — Dead safety-snapshot export: ExportForSafety is unreferenced, so requireAuthoritativeXml=true is reachable only from tests

- **Verdict:** confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:84-89 ExportForSafety`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:91,124 ExportXml(requireAuthoritativeXml)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporterXmlBundle.cs:9-41 ExportXmlBundle strict branches ('project-tree safety snapshot')`
  - `TiaMcpServer.Tests/Block/BlockExporterSafetyTests.cs:28,44,53,75`
- **Evidence:** check_references(ExportForSafety) returns no references anywhere. It is the only production caller that passes requireAuthoritativeXml: true. The error text 'Authoritative Simatic ML XML could not be exported for a project-tree safety snapshot' is left over from the safety-token/SafetyRead path that AGENTS.md says was removed. The strict-mode tests in BlockExporterSafetyTests exercise code no tool can reach.
- **Proposal:** Delete ExportForSafety. Remove the requireAuthoritativeXml parameter from ExportXml and ExportXmlBundle, along with both `if (requireAuthoritativeXml)` blocks and the XDocument.Parse check. Update the header comment to drop the 'stricter safety read' mention. Delete the strict test cases and keep the best-effort cases.
- **Risk:** None at runtime. Test-only removal.
- **Verifier notes:** search_text finds ExportForSafety only at its declaration (BlockExporter.cs:84). It is the only production path that passes requireAuthoritativeXml:true; Export at line 81 passes false. The strict branches in BlockExporterXmlBundle.cs:24-28 and 36-41, with the message about a 'project-tree safety snapshot', plus the header comment at line 9, are leftovers of the removed SafetyRead path. Strict mode is exercised only in BlockExporterSafetyTests.cs:26-28,41-44,50-53,65-75. The best-effort tests also call ExportXmlBundle, so they need the signature updated.

### WORKERDOMAINS-5 — Production overloads used only by tests: legacy BlockImportCoordinator.Execute and BlockPostconditionVerifier.Verify(evidence)

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportCoordinator.cs:9-34 Execute(Action<DirectoryInfo,ParsedBlockImportBundle>, Func<BlockPostconditionEvidence>)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportCoordinator.cs:307-321 UpgradeLegacyEvidence`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockPostconditionVerifier.cs:13-16 Verify(evidence) -> 'update'`
  - `TiaMcpServer.Tests/Block/BlockImportCoordinatorTests.cs:130,158,190,... (two-arg lambdas)`
  - `TiaMcpServer.Tests/Block/BlockPostconditionVerifierTests.cs:95-148`
- **Evidence:** BlockImporter.cs:54 is the only production caller of the coordinator, and it uses the boundary overload (the three-argument lambda plus Func<bool,...>). The legacy overload builds a synthetic boundary and fabricates a CompileCheckReport through UpgradeLegacyEvidence, purely for tests. The one-argument Verify is called only from tests. Production calls Verify(evidence, "create") through BlockCreationCoordinator, and imports call VerifyImport.
- **Proposal:** Move the tests to the boundary overload, wrapping their lambdas the way the legacy shim does today, then delete the legacy Execute, UpgradeLegacyEvidence and the one-argument Verify.
- **Risk:** Test churn only. Make sure the converted tests still cover the cleanup and verification-failure paths they cover today.
- **Verifier notes:** In production, BlockImporter.cs:54 calls BlockImportCoordinator.Execute through the boundary overload (three-argument lambda, lines 57+). Lines 26 and 126 use ExecuteWithPreTargetOutcome, and line 173 uses ExecuteSource. The legacy overload at BlockImportCoordinator.cs:9-34 and UpgradeLegacyEvidence (307-321) are reached only by tests (BlockImportCoordinatorTests, BlockImportDiagnosticSanitizerTests). The one-argument BlockPostconditionVerifier.Verify(evidence) at BlockPostconditionVerifier.cs:13-16 is called only from BlockPostconditionVerifierTests.cs:95-148; production uses Verify(evidence, "create") through BlockCreationCoordinator.cs:13.

### WORKERDOMAINS-6 — Temp-directory create/cleanup is copied at least six times with four different cleanup-failure policies

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:126-130 ExportXml finally (unguarded Directory.Delete)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:175-179 ExportSource finally (unguarded)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeExporter.cs:50-56 Export finally (unguarded)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/ExternalSourceScope.cs:131-145 TryDeleteDirectory (silent)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeImporter.cs:157-172 TryDeleteDirectory (stderr)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportCoordinator.cs:355-374 TryCleanupStaging (sanitized warning, `_ = exception;`)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporterVerification.cs:64-67 DeleteVerificationDirectory`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:93-94,126-130,151-152,175-179`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeExporter.cs:40-56`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/ExternalSourceScope.cs:131-145`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeImporter.cs:157-172`
- **Evidence:** ExternalSourceScope's comment says a cleanup failure 'must never replace' the exception already unwinding. The three exporters still call Directory.Delete(tempDir, true) unguarded in finally. An IOException there (a file lock from AV or an indexer) turns a successful export into a worker_operation_failed (Program.Execute maps IOException) or hides the real Siemens failure. The two TryDeleteDirectory copies have the same name and different behavior. The GUID-named temp-path construction is also repeated in each file.
- **Proposal:** Add one internal `WorkerTempDirectory : IDisposable` (Create(prefix), Path, Dispose that tries to delete and records one sanitized warning on stderr) under Openness/Infrastructure, and use it in all seven places. Keep BlockImportCoordinator's injectable cleanup seam by having it call the same TryDelete core. Remove `_ = exception;`.
- **Verifier-corrected proposal:** Add one small internal TryDeleteTempDirectory(path) helper. It should catch Exception and write one stderr warning, the same as PlcTypeImporter's policy, because stderr becomes response warnings. Use it in the three unguarded exporter finally blocks and in place of the two TryDeleteDirectory copies; ExternalSourceScope may keep silent mode if a warning there is unwanted. Leave the BlockExporterVerification and BlockImportCoordinator cleanup seams as they are. An IDisposable wrapper type is optional.
- **Risk:** Low. Cleanup failures now show up as warnings instead of failing the call. Tests that inject cleanup failures (BlockExporterVerificationTests, BlockImportCoordinatorTests) must keep their seams.
- **Verifier notes:** Three exporters call Directory.Delete unguarded in finally: BlockExporter.cs:126-130 (ExportXml), BlockExporter.cs:175-179 (ExportSource) and PlcTypeExporter.cs:50-56. An IOException there turns a successful export into worker_operation_failed (Program.cs:1562-1565), or replaces the real exception. There are two TryDeleteDirectory copies with different policies: silent in ExternalSourceScope.cs:131-145, stderr in PlcTypeImporter.cs:157-172. The proposal overreaches in one place. BlockExporterVerification is already guarded: its call site at lines 41-51 wraps DeleteVerificationDirectory in try/catch and emits a warning. BlockImportCoordinator.TryCleanupStaging deliberately uses sanitized warnings, and tests inject failures through its seam. `_ = exception;` (line 371) is only a cosmetic discard.

### WORKERDOMAINS-7 — Caller-facing validation and not-found errors are thrown as InvalidOperationException and reported as worker_operation_failed

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagTargetResolver.cs:65-71 RequireName`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs:272-278 RequireName`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs:83 IsSafety rejection`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:99,104,161 PLC/block not found`
  - `TiaMcpServer.OpennessWorker/Program.cs:1558-1561 Execute maps InvalidOperationException -> WorkerOperationFailed`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadDispatch.cs:64-65 MissingField (ValidationError) for contrast`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:98-105,158-162`
  - `TiaMcpServer.OpennessWorker/Program.cs:1558-1561`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:52,230-241`
- **Evidence:** HMI, VCI and the block/type resolvers throw WorkerOperationException with validation_error or target_not_found for the same situations. The PLC tag helpers and CompileChecker throw a bare InvalidOperationException, which Program.Execute turns into worker_operation_failed. So a missing table name, or a PLC that cannot be found for compile_check, is reported as a server failure instead of a caller error. Also, CrossReferenceReader.TryRead (line 195) treats an exact InvalidOperationException as a recoverable gap, so the exception type carries meaning that differs between domains.
- **Proposal:** Replace these with WorkerOperationException(ValidationError, '<field> is required.'), (TargetNotFound, ...) in CompileChecker, and (UnsupportedCapability or ValidationError) for IsSafety (see WORKERDOMAINS-9). Keep InvalidOperationException only for internal invariant breaches (the BlockImportInvocationBoundary/BlockSourceArtifactTracker markers are fine).
- **Verifier-corrected proposal:** Throw WorkerOperationException(TargetNotFound, ...) for the missing-PLC cases in CompileChecker; using FindUnique/FindEveryPlc from WORKERDOMAINS-1 does this naturally. Delete the unreachable CompileChecker.cs:102-105 branch. Optionally switch the worker RequireName helpers to ValidationError for consistency; it has low value because the host validates first. Handle isSafety under WORKERDOMAINS-9.
- **Risk:** Error-category contract change for these paths. Host error mapping or tests asserting worker_operation_failed may need updating. No live run is needed.
- **Verifier notes:** Program.Execute maps InvalidOperationException to WorkerOperationFailed (Program.cs:1558-1561). Three of the cited paths have no practical effect. (1) The host already rejects blank required string fields before the worker (PlcOperationCatalog.cs:52 comment, 230-241), so the RequireName throws in TagTargetResolver.cs:65-71 and TagMutationService.cs:272-278 are defense-in-depth. (2) CompileChecker.cs:102-105 is unreachable, because BlockTargetResolver.ResolveForExport (BlockTargetResolver.cs:35-61) never returns a null Block. (3) The IsSafety throw is better handled by WORKERDOMAINS-9. The impact that remains is real: CompileChecker.cs:99 and 161 report a missing PLC for compile_check, or for postcondition compiles, as worker_operation_failed instead of target_not_found. The CrossReferenceReader.TryRead exact-type check exists (CrossReferenceReader.cs:195-196), as does CompileChecker.IsExpectedCompileFailure (198-201), so do not reclassify an internal InvalidOperationException that those catches rely on.

### WORKERDOMAINS-8 — TagMutationService keeps private copies of TagTargetResolver's folder/name helpers

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs:217-278 ResolveTable/ResolveGroup/NotFound/SplitFolderPath/NormalizeFolderPath/RequireName`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagTargetResolver.cs:18-71 Resolve/NotFound/NormalizeFolderPath/SplitFolderPath/RequireName`
- **Evidence:** SplitFolderPath, NormalizeFolderPath, RequireName and NotFound are identical in both files. The group walk in TagMutationService.ResolveGroup is the same loop as TagTargetResolver.Resolve's folder walk. UpdateTag already uses TagTargetResolver.Resolve, while Delete/Create/UserConstant go through the private copy, so two paths resolve the same target.
- **Proposal:** Expose `TagTargetResolver.ResolveGroup(project, plcName, folderPath)` and `ResolveTable(...)` (internal), make Resolve build on them, and delete the five private copies in TagMutationService. Use NormalizeFolderPath from one place.
- **Risk:** None if messages stay byte-identical. Existing PlcWritePreconditions and tag mutation tests cover this.
- **Verifier notes:** These helpers are identical in both files: TagMutationService.cs:248-278 (NotFound, SplitFolderPath, NormalizeFolderPath, RequireName) and TagTargetResolver.cs:45-71. The group/table walk in TagMutationService.ResolveGroup/ResolveTable (217-246) matches TagTargetResolver.Resolve:28-38, and the messages are byte-identical. UpdateTag (line 86) already uses TagTargetResolver.Resolve, while the other operations use the private copies. NormalizeFolderPath is already internal in TagTargetResolver.

### WORKERDOMAINS-9 — Public update_tag isSafety parameter always fails in the worker

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs:76,80-84`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:41,82`
  - `TiaMcpServer/Plc/PlcOperationRequest.cs:73`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:179`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1337,1355`
  - `TiaMcpServer/Plc/PlcWorkingState.cs:276`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs:76,80-84`
  - `TiaMcpServer.OpennessWorker/Program.cs:1076`
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs:41,82`
  - `TiaMcpServer/Plc/PlcOperationRequest.cs:73`
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs:56`
  - `TiaMcpServer/Plc/PlcWorkingState.cs:276`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:1337,1355`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:179`
  - `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md:174,262`
  - `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs:386`
- **Evidence:** isSafety is advertised in the update_tag optional fields and carried all the way through the host, client and WorkerRequest. The worker unconditionally throws 'Updating IsSafety is not supported by the available TIA Openness API.' whenever it has a value. No path can succeed with it.
- **Proposal:** Remove isSafety from PlcOperationCatalog/PlcOperationRequest/WorkerRequest/client/TagMutationService.UpdateTag, so the existing unknown-field validation rejects it. Alternatively, keep it in the schema and reject it at host validation with validation_error before binding. Either way, delete the worker parameter.
- **Verifier-corrected proposal:** Remove isSafety from the update_tag spec, PlcOperationRequest, PlcWorkingState, the worker client, WorkerRequest, Program.cs:1076 and TagMutationService.UpdateTag, so the host's unknown-field validation rejects it. Alternatively, reject it at host validation with validation_error. Also fix PLC_OPERATIONS_SUMMARY.md:174/262, the IMPROVEMENT_LOG M4 entry and PlcOperationCatalogTests.cs:386.
- **Risk:** Public input-schema change (removes a field). Update PlcOperationCatalogTests.cs:386 and the tool description. The worker already rejects it today, so no caller loses a working capability.
- **Verifier notes:** isSafety is declared optional for update_tag (PlcOperationCatalog.cs:41,82), carried through PlcOperationRequest.cs:73, PlcWorkerInvoker.cs:56, OpennessWorkerClient.cs:1337,1355, WorkerRequest.cs:179 and Program.cs:1076, and shown as a planned change in dry runs (PlcWorkingState.cs:276). The worker throws unconditionally whenever it has a value (TagMutationService.cs:80-84). So a dry run previews success and the actual call then fails as worker_operation_failed. The docs contradict the code: docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md:174 lists isSafety as optional and line 262 says 'isSafety is not verified after the write', which implies it can be written. docs/IMPROVEMENT_LOG.md M4 makes the same implication.

### WORKERDOMAINS-10 — TagTableReader keeps a second, lossy legacy reader (ReadAll) used only by Network's hardware tag index

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagTableReader.cs:18-25 ReadAll`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagTableReader.cs:237-335 CollectTablesFromGroup/ReadTagTable/ReadTags/ReadUserConstants`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagTableReader.cs:40-87 ReadInventory (messages, capped)`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareTagIndexResolver.cs:55`
- **Evidence:** ReadInventory reports every unreadable item into capped messages. The parallel ReadAll path (about 100 lines) drops unreadable tables, tags and constants and only writes to Console.Error. Its only caller is HardwareTagIndexResolver, which also selects the PLC through root-only FindAll.
- **Proposal:** Have HardwareTagIndexResolver call ReadInventory for the selected PLC (or a ReadInventory(PlcSoftware) overload), carry its messages into the hardware result's warnings, and delete ReadAll and its four private helpers.
- **Risk:** The hardware I/O map would now report incompleteness instead of silently skipping. Needs one network_read hardware I/O check (live or FakeWorker) and the Network owner's agreement, since the caller is out of scope.
- **Verifier notes:** TagTableReader.ReadAll (TagTableReader.cs:18-25) and its helpers (CollectTablesFromGroup at 237, ReadTagTable at 273, and the others) have exactly one caller, HardwareTagIndexResolver.cs:55. ReadInventory is the path used by Program.cs:1006 and the tests, and it takes a messages sink (FindEveryPlc at line 48). HardwareTagIndexResolver already returns a messages list, so carrying the inventory messages over is straightforward. The caller belongs to the Network domain, so its owner needs to agree.

### WORKERDOMAINS-11 — HMI writers: duplicated create-with-cleanup, error category lost on re-wrap, and parse/overflow exceptions escape without hmiWriteOutcome

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagWriter.cs:10-41 Create (cleanup copy)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiAlarmWriter.cs:18-49 Create<T> (same cleanup, already generic)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagWriter.cs:78-79 Failure (always WorkerOperationFailed)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTextMapper.cs:56 throws TargetNotFound inside Apply`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiAlarmWriter.cs:99,104,109 Enum.Parse / checked((byte|uint)) inside Apply`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagTableWriter.cs:45 catches only EngineeringException`
  - `'notNeeded'/'deleted'/'failed' literals in HmiTagWriter.cs:24, HmiAlarmWriter.cs:32, HmiTagTableWriter.cs:45`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagWriter.cs:10-41,78-79`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiAlarmWriter.cs:18-49`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTextMapper.cs:55-56`
  - `TiaMcpServer/Hmi/HmiWriteCatalog.cs:100-112`
- **Evidence:** The tag and alarm Create methods have the same find/identity-check/delete cleanup block. The alarm version is already generic over Func<T> create/find/delete. Failure() wraps every caught WorkerOperationException as worker_operation_failed, so a TargetNotFound for an unknown text culture loses its category. Enum.Parse (ArgumentException) and checked casts (OverflowException) are not in the `when` filter. If they fire after some properties were applied in Update, the error leaves without the hmiWriteOutcome recording what was applied, which breaks the 'failed updates retain applied properties' guarantee in AGENTS.md (unless the host validates every enum and range first). The tag-table writer uses a different filter from the other two.
- **Proposal:** Run HmiTagWriter.Create through HmiAlarmWriter's generic Create, moved to a shared HmiWriteRun helper. In Failure, rethrow an existing WorkerOperationException with its category and attach the outcome. Widen the filter to ArgumentException and OverflowException, or validate enum and range before Set. Use one `when` filter for all three writers. Add an HmiCleanupStates constant set in Contracts/Hmi.
- **Verifier-corrected proposal:** Move the generic Create-with-cleanup into a shared helper and route HmiTagWriter.Create through it. In Failure, keep the category when the caught exception is a WorkerOperationException, and attach the outcome. Add constants for the cleanup states. Widening the filter to ArgumentException/OverflowException is optional defense-in-depth, because host validation covers triggerMode, priority and bitNumber. Verify that condition is validated the same way before relying on that.
- **Risk:** Category change for culture-not-found (worker_operation_failed becomes target_not_found). Check host validation of triggerMode, condition, priority and bitNumber first. If the host already validates them, that part becomes defense-in-depth. HmiTagWriterTests and HmiAlarmWriterTests cover the cleanup paths.
- **Verifier notes:** The duplicated cleanup is real: HmiTagWriter.cs:10-41 versus the generic HmiAlarmWriter.cs:18-49. The category is really lost. HmiTagWriter.Failure (78-79) always wraps as WorkerOperationFailed, so a TargetNotFound from HmiTextMapper.Write (HmiTextMapper.cs:55-56, unknown culture) comes back as worker_operation_failed. The 'notNeeded'/'deleted'/'failed' literals are repeated. The parse/overflow part is largely refuted, because the host validates first: HmiWriteCatalog.cs:108 checks triggerMode is OnRisingEdge or OnFallingEdge, :111 checks priority is 0-255 and :112 checks bitNumber is nonnegative, so the Enum.Parse and checked casts are defense-in-depth. The HmiTagTableWriter filter difference (line 45, EngineeringException only) has little impact. A WorkerOperationException that escapes there comes from Tables() or Find() before any mutation, so it carries its correct category and no outcome is needed.

### WORKERDOMAINS-12 — hmi_write tag-table update/delete ignores groupPath when choosing the table, and HMI group matching differs between read and write

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagTableWriter.cs:10-21 Tables (group via composition Find)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagTableWriter.cs:25,27 Update/Delete lambdas ignore `tables` and call Find(software, name)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagTableWriter.cs:28-40 Find/AllTables (all groups, OrdinalIgnoreCase)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagReader.cs:371-384 ResolveGroup (Ordinal)`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiScreenReader.cs:236-251 ResolveGroup (Ordinal, copy)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagTableWriter.cs:10-27,41-46`
  - `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagReader.cs:371-384`
  - `TiaMcpServer/Hmi/HmiWriteCatalog.cs:19-21`
- **Evidence:** Run resolves Tables(software, write.GroupPath), but the Update and Delete actions never use that composition. They search every table in every group by case-insensitive name. So a groupPath only validates that the group exists, and two same-named tables in different groups give target_ambiguous even when groupPath picks one of them. The read side resolves group segments with StringComparison.Ordinal through HmiReadLog.Guard. The write side uses composition.Find(segment). Table names are matched OrdinalIgnoreCase. HmiTagReader and HmiScreenReader each have their own identical ResolveGroup.
- **Proposal:** When GroupPath is set, resolve the table inside the resolved group (`tables.Find` or a case-insensitive scan of `tables`), and use the all-groups search only when GroupPath is null. Extract one generic HMI group-path resolver used by HmiTagReader, HmiScreenReader and HmiTagTableWriter, with one documented comparison rule.
- **Verifier-corrected proposal:** No behavior fix is needed for update/delete. Optionally simplify HmiTagTableWriter.Update/Delete so they do not resolve or pass an unused composition. Align create_tag_table group resolution with the read side's Ordinal, guarded walk, or document which comparison rule composition Find uses. A generic HMI group-path helper is a low-value refactor.
- **Risk:** Behavior change for hmi_write update/delete_tag_table with groupPath. Needs a scoped live check on a Unified HMI with nested tag-table groups. The host-side guard planning (cross-reference queries) must resolve the same table.
- **Verifier notes:** The headline defect is refuted. update_tag_table and delete_tag_table do not accept groupPath: HmiWriteCatalog.cs:20-21 declares Op("tableName newName") and Op("tableName"), and docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md:170-171 lists no optional fields. GroupPath is therefore always null for update/delete, Tables(software, null) is the root, and the all-groups OrdinalIgnoreCase Find is the documented name-only lookup. Only create_tag_table takes groupPath (HmiWriteCatalog.cs:19). Two smaller points remain. (1) Create resolves group segments with composition Find (HmiTagTableWriter.cs:14-19), while the read side uses an Ordinal scan guarded by HmiReadLog (HmiTagReader.cs:371-384), so the comparison rule may differ. (2) The Update/Delete lambdas ignore their `tables` argument. HmiTagReader.ResolveGroup and HmiScreenReader.ResolveGroup (236-251) are structurally similar but not identical: different element types and messages.

### WORKERDOMAINS-13 — Block node-type mapping copied three times; two separate selector walkers mirror ProjectTreeSnapshotWalker

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/CrossReferences/CrossReferenceTargetResolver.cs:218-227 BlockNodeType`
  - `TiaMcpServer.OpennessWorker/Openness/Project/ProjectTreeSnapshotWalker.cs:171-178 BuildBlockNode switch`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciScopeResolver.cs:198-221 Segment (OB/FB/FC/GlobalDB/InstanceDB/ArrayDB)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporter.cs:216-225 BlockKindName (different strings)`
  - `CrossReferenceTargetResolver.cs:145-196 Children vs VciScopeResolver.cs:147-184 Children`
- **Evidence:** The OB→Ob, FB→Fb, ..., _→Block switch is written out three times against ProjectTreeNodeTypes. A new block kind would have to be added in all three. The cross-reference and VCI resolvers each re-implement 'walk a browse_project_tree selector to a live object'. Their child sets already differ: cross-reference has SoftwareUnit and SystemBlockFolder but no technology objects, VCI has technology objects and HMI scripts but no units. Name failures are also handled differently (swallowed versus propagated).
- **Proposal:** Add one worker helper `ProjectTreeBlockKinds.NodeTypeOf(PlcBlock)` and use it in all three places (small, low risk). Leave the two selector walkers separate for now, because their scope rules differ on purpose. Add a cross-reference comment noting the VCI divergence so a future selector change touches both.
- **Risk:** None for the mapping helper. Do not merge the walkers without live VCI and cross-reference verification.
- **Verifier notes:** The same OB/FB/FC/GlobalDB/InstanceDB/ArrayDB-to-ProjectTreeNodeTypes switch appears in CrossReferenceTargetResolver.cs:218-227, ProjectTreeSnapshotWalker.cs:170-179 and VciScopeResolver.cs:209-215. BlockExporter.BlockKindName (216-225) maps to different strings and is a separate concern. The child-set divergence between CrossReferenceTargetResolver.Children (145-196: units and system blocks, no technology objects) and VciScopeResolver.Children (147-184: technology objects and HMI scripts, no units) matches the code. The proposal correctly keeps the two walkers separate.

### WORKERDOMAINS-14 — Constants and serializer options duplicated between CompileChecker and CompileReportProjection, and between the two postcondition verifiers

- **Verdict:** confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:39 literal 60_000`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileReportProjection.cs:14 private const MaxSerializedCharacters = 60000`
  - `CompileChecker.cs:14-17 ObservationJson vs CompileReportProjection.cs:19-22 ReportJson (identical CamelCase options)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockPostconditionVerifier.cs:9-11 PublicFailureDetail/UncertainStateWarning`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs:21-24 same constants`
- **Evidence:** The fixed 60,000 compile-report limit is deliberate (per AGENTS.md), but it appears both as a private const and as a separate magic literal used to compute detailsOmitted, so the two can drift apart. Two identical JsonSerializerOptions instances serialize the same report. The two verifiers declare the same two message constants.
- **Proposal:** Make CompileReportProjection.MaxSerializedCharacters internal, expose a `SerializedLength(report)` helper that uses ReportJson, and call it from CompileObserved. Delete ObservationJson. Move the two postcondition message constants into BlockPostconditionVerifier as internal and reference them from PlcTypePostconditionVerifier.
- **Risk:** None.
- **Verifier notes:** CompileChecker.cs:39 uses a literal 60_000 with ObservationJson (14-17). CompileReportProjection.cs:14 has a private MaxSerializedCharacters = 60000 and an identical ReportJson (19-22), used at line 145 with the same >= comparison. PublicFailureDetail/UncertainStateWarning are duplicated in BlockPostconditionVerifier.cs:9-11 and PlcTypePostconditionVerifier.cs:21-24. The fixed 60,000 value is deliberate (AGENTS.md); only the duplication is the finding.

### ~~WORKERDOMAINS-15 — Block postcondition failure drops evidence warnings; the PLC type verifier surfaces them~~ (REFUTED)

- **Verdict:** refuted · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockPostconditionVerifier.cs:18-32 Verify(evidence, operation)`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs:102-122 Verify`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:55-96 VerifyCreatedBlockPostconditions`
- **Evidence:** PlcTypePostconditionVerifier.Verify writes evidence.Warnings to stderr on success and attaches them plus UncertainStateWarning on failure. BlockPostconditionVerifier.Verify ignores evidence.Warnings and diagnosticMessage completely and attaches only UncertainStateWarning. The PLC type docstring says it 'mirrors' the block verifier, but the warning handling differs. create_block's evidence diagnostic ('Created block could not be resolved: ...') is built and then thrown away.
- **Proposal:** Have BlockPostconditionVerifier.Verify use the same warning policy as the PLC type verifier (pass warnings through on both paths), and have PlcTypePostconditionVerifier.Verify call into it with its own message prefix. That leaves one implementation.
- **Risk:** create_block responses gain warnings. Keep diagnostic text sanitized (BlockImportDiagnosticSanitizer) before exposing exception messages.
- **Verifier notes:** There is no observable difference. BlockPostconditionVerifier.Verify(evidence, "create") is reached in production only through BlockCreationCoordinator, with evidence from VerifyCreatedBlockPostconditions (BlockMutationService.cs:55-96). That evidence never sets warnings, so there is nothing to drop. Neither verifier exposes diagnosticMessage: PlcTypePostconditionVerifier.Verify (102-122) also throws a fixed PublicFailureDetail and discards diagnosticMessage. Withholding raw exception text is intentional (PublicFailureDetail; VerifyImport sanitizes warnings at BlockPostconditionVerifier.cs:52-53). The claim that 'create_block responses gain warnings' is therefore wrong. The only real overlap is the duplicated constants, which WORKERDOMAINS-14 already covers.

### WORKERDOMAINS-16 — Session loss is handled differently across domains, and broad catch(Exception) blocks in Block code also swallow NonRecoverableException

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciDispatch.cs:25-29,53-57 and VciWriter.cs:165-172 NonRecoverable -> PortalLost`
  - `TiaMcpServer.OpennessWorker/Program.cs:1552-1557 Execute NonRecoverable -> WorkerOperationFailed (all other domains)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporterXmlBundle.cs:34,54 catch (Exception)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:64,84 catch (Exception) -> evidence`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:48-55 handles NonRecoverableException explicitly (contrast)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Program.cs:1552-1557`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciDispatch.cs:25-29,53-57`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciWriter.cs:165-172`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:2397`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockExporterXmlBundle.cs:34,54`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:64,84`
  - `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs:59,81`
- **Evidence:** VCI turns a lost Portal into portal_lost, which per AGENTS.md invalidates the binding and needs explicit recovery. PLC, block, HMI and cross-reference session loss falls through to Program.Execute and comes back as worker_operation_failed with 'TIA Portal was closed unexpectedly'. Inside the block domain, ExportXmlBundle's catch (Exception) around exportXml treats a dead Portal as 'companion-only fallback', and VerifyCreatedBlockPostconditions catches Exception, so a dead session becomes postcondition evidence. CompileChecker.CompileObserved and CrossReferenceReader/CrossReferenceTargetResolver exclude NonRecoverableException on purpose.
- **Proposal:** Add `when (ex is not NonRecoverableException)` to the four Block catch blocks. Decide whether portal_lost is the worker-wide category for NonRecoverableException. If it is, map it once in Program.Execute (and remove the VCI-local wrappers). If not, document in AGENTS.md why it is VCI-only.
- **Verifier-corrected proposal:** Make the main decision first: either map NonRecoverableException to portal_lost once in Program.Execute, then remove the VCI-local wrappers (keeping VciWriter's outcome/lastAttempted attachment), or document in AGENTS.md why portal_lost is VCI-only. Adding `when (ex is not NonRecoverableException)` to the Block/PLC-type exporter catches only matters once portal_lost becomes worker-wide. Postcondition catches that run after a mutation may deliberately keep the postcondition_failed result. Both changes need host review and a worker-loss recovery test.
- **Risk:** Mapping change in Program.Execute affects host binding invalidation for every domain, so it needs host review and a worker-loss recovery test. The catch filters alone are low risk.
- **Verifier notes:** The cross-domain inconsistency is real. VCI maps NonRecoverableException to portal_lost (VciDispatch.cs:25-29,53-57; VciWriter.cs:165-172), and the host invalidates the binding on portal_lost (OpennessWorkerClient.cs:2397). Every other domain gets worker_operation_failed from Program.cs:1552-1557. AGENTS.md documents portal_lost only for VCI and gives no reason it is VCI-only. The Block catch claims are overstated. In BlockExporterXmlBundle.cs:34, a dead Portal makes the companion export fail too, the catch at line 54 hits BlockDocumentPackagePolicy.Decide with hasPrimaryXmlDocument=false (fatal), and the result is worker_operation_failed, the same category Program.Execute gives. In BlockMutationService.cs:64,84, session loss after a completed create becomes postcondition_failed with the 'project state may have changed' warning, which is arguably the right outcome after a mutation. PlcTypePostconditionVerifier.cs:59,81 and BlockExporter.cs:46 follow the same pattern.

### WORKERDOMAINS-17 — VciWriter.Write is one 190-line method with string-literal operations and repeated redundant catch pairs

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciWriter.cs:14-206 Write`
  - `VciWriter.cs:19,20,28,47,58,62,66,89,96,105,141 literal operation names`
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciPlanner.cs:46 literal operation names`
  - `VciWriter.cs:100-102,121-123,153-155 catch (NonRecoverableException) throw; catch (EngineeringException) Failed++; catch (Exception) Failed++;`
  - `TiaMcpServer.Contracts/Vci/VciMethods.cs:14-24 (method names only, no operation constants)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Vci/VciWriter.cs:14-206,100-102,121-123,153-155`
  - `TiaMcpServer.Contracts/Vci/VciMethods.cs:24`
- **Evidence:** Write branches on write.Operation string literals ('create_workspace', 'delete_workspace', 'export', 'sync_to_project', 'sync_to_workspace', 'delete_mapping') down to about seven levels of nesting, with two local functions that capture mutable outcome state. The EngineeringException catch does the same thing as the Exception catch right after it, three times. The worker receives both `method` and `write.Operation` and has to check that they agree (line 23).
- **Proposal:** Add VciOperations constants in Contracts/Vci and derive VciMethods.Write from them. Split Write into CreateWorkspace, DeleteWorkspace, ExportLeaves, CollectCandidates, ImportCandidates and SyncToWorkspace private methods sharing an outcome/attempt context object. Collapse each catch pair to `catch (Exception ex) when (ex is not NonRecoverableException)`.
- **Verifier-corrected proposal:** Collapse each catch triple to `catch (NonRecoverableException) { throw; } catch (Exception) { ... }`, or use a single `when (ex is not NonRecoverableException)` filter. Optionally split Write into per-operation private methods with a shared context object, as a pure refactor under VciWriterTests plus a scoped live export/sync smoke check. Operation-name constants are optional, and VciMethods.ForOperation already exists.
- **Risk:** VCI is fragile (export can crash Portal) and the breadcrumb/lastAttempted ordering matters. This needs to be a pure refactor under the existing VciWriterTests, plus one scoped live export/sync smoke check.
- **Verifier notes:** Write spans VciWriter.cs:14-206 in a 233-line file, branches on string literals, and captures mutable state in local functions (ShouldSynchronize, Attempt at 182-205). The redundant EngineeringException/Exception catch pairs after NonRecoverable are at lines 100-102, 121-123 and 153-155. One evidence claim is wrong: VciMethods (Contracts/Vci/VciMethods.cs:24) already has ForOperation(operation) => "vci_" + operation, and the method-versus-operation agreement check at line 23 uses it. That check is a deliberate defense, not a smell. Adding operation constants is optional.

### WORKERDOMAINS-18 — Unreachable null-block branches, a duplicate PLC lookup, and a one-use pass-through coordinator in the block domain

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:101-109 DeleteBlock (FindUnique + ResolveForExport, `target.Block is null`)`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:102-105 `target.Block == null``
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockTargetResolver.cs:35-61 ResolveForExport always throws or returns a block`
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockCreationCoordinator.cs:3-16 (single caller BlockMutationService.cs:35)`
- **Corrected locations:**
  - `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs:101-109`
  - `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs:102-105`
- **Evidence:** ResolveForExport throws TargetNotFound on the deterministic miss and on zero legacy matches, so Block is never null when it returns. The null checks after it are unreachable. DeleteBlock also resolves the PLC twice, once directly with FindUnique and again inside ResolveForExport(project, ...). BlockCreationCoordinator is a 16-line static class with one caller that runs import() and then Verify(...).
- **Proposal:** Delete the two unreachable branches. In DeleteBlock, use `ResolveForExport(plcSoftware, address)` with the already-resolved software. Inline BlockCreationCoordinator.Execute into CreateBlock as two statements and delete the file, along with its <Compile Include> line in TiaMcpServer.Tests.csproj if it is linked.
- **Verifier-corrected proposal:** Delete the two unreachable null-block branches. In DeleteBlock, pass the already-resolved plcSoftware to ResolveForExport(plcSoftware, address). Keep BlockCreationCoordinator as the offline-testable seam.
- **Risk:** None. Update the tests if any reference BlockCreationCoordinator directly.
- **Verifier notes:** The unreachable branches are confirmed. ResolveForExport (BlockTargetResolver.cs:35-61) throws on a miss or returns a non-null block, so BlockMutationService.cs:104-109 and CompileChecker.cs:102-105 are dead. DeleteBlock resolves the PLC twice (BlockMutationService.cs:101-102); it could call ResolveForExport(plcSoftware, address). The proposal to inline BlockCreationCoordinator is refuted. It is an offline test seam: TiaMcpServer.Tests.csproj:290-291 links it, and BlockMutationPostconditionTests.cs:16,57,81,101 exercise it directly, while BlockMutationService itself depends on Siemens types and cannot be tested offline the same way. Inlining it would lose those tests.

## CONTRACTS

Contracts is mostly lean, but the Legacy null-omission mechanism is still there even though no production contract uses it. The worker never reads two WorkerRequest wire fields (AllowTiaConfirmations, ForceRebind). The HMI write method list is hand-copied in three Contracts catalogs, while VCI shares one list, and the 'never ran' failure-category set is copied into three verifiers. The assembly also holds host-only mutable session state (ProjectSessionBinding) and worker-only algorithms, despite AGENTS.md calling it 'shared DTOs'. FakeWorker/Program.cs is a 4,201-line top-level file with a ~172-case switch, four ways of building responses and several scenarios no test reaches. Tests copy repo-root, CreateClient and canonical-document helpers into dozens of files. TiaMcpServer.Tests.csproj Compile Include entries all resolve (191 entries, no missing files; only host Program.cs is not linked), and tests that still name removed features are only negative regression checks.

### CONTRACTS-1 — WorkerRequest.AllowTiaConfirmations and WorkerRequest.ForceRebind are dead wire fields

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56 Confirm/AllowTiaConfirmations doc + property`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:420 ForceRebind`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945,985,1011,1039,1057,1128,1204 request.AllowTiaConfirmations = true`
  - `TiaMcpServer.OpennessWorker/Program.cs:32 new WorkerTiaPortalSession(allowTiaConfirmations: true)`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleForwardingTests.cs (only test mentioning allowTiaConfirmations)`
- **Corrected locations:**
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:49-56`
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:420`
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs:945-1886 (~30 AllowTiaConfirmations assignments incl. initializers 1672,1768; doc 1185)`
  - `TiaMcpServer.OpennessWorker/Program.cs:32`
  - `TiaMcpServer.OpennessWorker/Openness/Multiuser/MultiuserPortalReadDispatch.cs:65-67 (reflective non-default check)`
  - `TiaMcpServer.Tests/Multiuser/MultiuserPortalReadDispatchTests.cs:224,237`
  - `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleForwardingTests.cs:233,265,331`
- **Evidence:** A repo-wide search finds no read of request.AllowTiaConfirmations in TiaMcpServer.OpennessWorker or FakeWorker. The worker hardcodes allowTiaConfirmations:true on its shared session (Program.cs:32), yet the host sets the flag at 7 call sites. WorkerRequest.ForceRebind is never assigned by the host (the host's rebind logic uses LifecycleWriteItem.ForceRebind and the forceRebind parameter on OpennessWorkerClient methods) and never read by either worker. The doc comment at WorkerRequest.cs:49-51 ('update_block_logic … forwards only AllowTiaConfirmations') describes a protocol the worker does not honour.
- **Proposal:** Delete both properties and the 7 host assignments. Fix the Confirm doc comment so it no longer mentions AllowTiaConfirmations. Update the forwarding test that checks the serialized request field list.
- **Verifier-corrected proposal:** Delete WorkerRequest.AllowTiaConfirmations and WorkerRequest.ForceRebind, and all ~30 host assignments in OpennessWorkerClient.cs (object initializers at 1672 and 1768 included). Fix the doc comments at WorkerRequest.cs:49-51 and OpennessWorkerClient.cs:1185. Remove the three allowTiaConfirmations asserts in NetworkSubnetLifecycleForwardingTests. Drop the [InlineData("forceRebind")] case from MultiuserPortalReadDispatchTests (or replace it with another non-default selector). MultiuserPortalReadDispatch.Validate needs no change because it is reflective. Confirm that LifecycleBindingStrategyTests.cs:199 'ForceRebind = force' targets LifecycleWriteItem and not WorkerRequest. Effort S, no behaviour change, no live verification needed.
- **Risk:** The request JSON loses two always-default members. The worker ignores unknown and missing members, so behaviour does not change. Forwarding/field-list tests need updating. No live verification needed.
- **Verifier notes:** Both fields are dead on the wire. No worker or FakeWorker code reads request.AllowTiaConfirmations: TiaPortalSession takes the flag only from its constructor (TiaPortalSession.cs:24-32), and Program.cs:32 hardcodes it to true. WorkerRequest.ForceRebind has zero hits under TiaMcpServer.OpennessWorker/** (search_text absence). Two parts of the finding are wrong. (a) The host sets AllowTiaConfirmations at about 30 sites, not 7: OpennessWorkerClient.cs:945,985,1011,1039,1057,1128,1204,1275,1295,1321,1357,1379,1405,1431,1453,1475,1489,1503,1517,1672,1768,1802,1844,1872,1886, plus the doc comment at 1185. (b) Both properties are read by reflection. MultiuserPortalReadDispatch.Validate (MultiuserPortalReadDispatch.cs:65-67) loops over typeof(WorkerRequest).GetProperties() and rejects any non-default member. MultiuserPortalReadDispatchTests.cs:224,237 sets request.ForceRebind=true to test that rejection. Deleting the properties stays safe because the allow-list check simply loses a member. NetworkSubnetLifecycleForwardingTests.cs:233,265,331 assert the serialized allowTiaConfirmations.

### CONTRACTS-2 — LegacyNullOmission mechanism has no remaining production users

- **Verdict:** partially-confirmed · **Severity:** medium · **Category:** dead-code · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Json/LegacyNullOmissionAttribute.cs:4-21 LegacyNullOmissionReason/LegacyNullOmissionAttribute`
  - `TiaMcpServer.Contracts/Json/WorkerJson.cs:27-66 ExplicitNullPayload, LegacyNullOmittingPayload, PayloadOptionsFor, OmitsNullMembers, ContractType`
  - `TiaMcpServer/Json/CanonicalJson.cs:138-147 RefuseLegacyNullOmittingPayload`
  - `TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs:18-19 empty Expected register`
  - `TiaMcpServer.Tests/Json/CanonicalJsonWorkerPayloadTests.cs:48,54; TiaMcpServer.Tests/Worker/WorkerJsonTests.cs:81 (only [LegacyNullOmission] uses: test fixtures)`
  - `AGENTS.md 'Worker payload JSON' bullet`
- **Corrected locations:**
  - `TiaMcpServer.Contracts/Json/LegacyNullOmissionAttribute.cs:4-21`
  - `TiaMcpServer.Contracts/Json/WorkerJson.cs:24-66`
  - `TiaMcpServer/Json/CanonicalJson.cs:55-61,138-147`
  - `TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs:18-25`
  - `TiaMcpServer.Tests/Json/CanonicalJsonWorkerPayloadTests.cs:48,54,152,167,246,260`
  - `TiaMcpServer.Tests/Worker/WorkerJsonTests.cs:24,81`
  - `TiaMcpServer.Tests/Block/CompileCheckInfoTests.cs:11 and ~8 other PayloadOptionsFor test call sites`
  - `TiaMcpServer.Tests/Worker/PortalSelectionPayloadContractsTests.cs:29-31; Network/NetworkIntrospectionWorkerDispatchTests.cs:33; Vci/VciContractDecodeTests.cs:33; Project/LifecycleGuardedContractTests.cs:37`
  - `AGENTS.md 'Worker payload JSON' bullet`
- **Evidence:** No type in Contracts, the host or the worker carries [LegacyNullOmission]. The only uses are three test-local fixture classes. The null-policy register is an empty dictionary. The only enum reason, ToolMigration ('tool still on the legacy text contract'), no longer applies: AGENTS.md says every tool is structured and the legacy register is empty. Several test comments still say 'until Task 3 removes it' (CanonicalJsonWorkerPayloadTests.cs:152,167,246; CatalogEntryInfoTests.cs:56; HardwareConfigInfoTests.cs:175). WorkerJson.Request and WorkerJson.ExplicitNullPayload are also identical Create(JsonIgnoreCondition.Never) instances.
- **Proposal:** Make SerializePayload always use ExplicitNullPayload (or merge it with Request). Delete LegacyNullOmissionAttribute/Reason, LegacyNullOmittingPayload, OmitsNullMembers/ContractType, CanonicalJson.RefuseLegacyNullOmittingPayload and the register test, plus the fixture classes and stale 'Task 3' comments. Replace the AGENTS.md rule with 'payloads always write null members'. If the maintainer wants to keep the escape hatch, at least add a comment on the enum saying it currently has zero users.
- **Verifier-corrected proposal:** Treat this as a maintainer decision. Either (a) retire the mechanism: SerializePayload always uses ExplicitNullPayload; delete LegacyNullOmittingPayload, OmitsNullMembers, ContractType, the attribute/enum, CanonicalJson.RefuseLegacyNullOmittingPayload, the register test and the legacy fixtures; replace the ~9 PayloadOptionsFor(typeof(X)) test call sites with WorkerJson.ExplicitNullPayload; delete or rewrite the ~4 OmitsNullMembers assertions; edit AGENTS.md and docs/roadmap/json-contract.md. Or (b) keep it and update the stale ToolMigration doc text and 'until Task 3' comments. Effort M; no wire change.
- **Risk:** No wire change, because no production type is marked. Tests that exercise the legacy branch are deleted. AGENTS.md and docs/roadmap/json-contract.md need edits. This reverses a documented convention, so the maintainer should decide.
- **Verifier notes:** The facts check out. LegacyNullOmissionAttribute.cs:4-8 has one reason, ToolMigration ('tool still on the legacy text contract'). AGENTS.md says every tool is now structured. The only [LegacyNullOmission] uses are test fixtures (CanonicalJsonWorkerPayloadTests.cs:48,54; WorkerJsonTests.cs:81). The register is empty (WorkerPayloadNullPolicyRegisterTests.cs:18-19). WorkerJson.Request and ExplicitNullPayload are identical Create(Never) instances (WorkerJson.cs:24,27). Two parts of the finding need correcting. (1) The orchestrator brief lists [LegacyNullOmission] as a deliberate design decision, so this is a maintainer decision to retire an obsolete escape hatch, not a defect. (2) Test impact is understated. WorkerJson.PayloadOptionsFor is called by about 9 test files (CompileCheckInfoTests.cs:11, CrossReferenceInfoTests.cs:11, AddDeviceResultInfoTests.cs:10, CatalogEntryInfoTests.cs:10, ConfigureNetworkDeviceResultInfoTests.cs:10, HardwareConfigInfoTests.cs:10, NetworkSparseConfigurationTests.cs:34,81, ProjectMetadataTests.cs:20). OmitsNullMembers is asserted in NetworkIntrospectionWorkerDispatchTests.cs:33, VciContractDecodeTests.cs:33, PortalSelectionPayloadContractsTests.cs:29-31 and LifecycleGuardedContractTests.cs:37.

### CONTRACTS-3 — HMI write method names hand-copied in three Contracts catalogs, while VCI uses one shared list

- **Verdict:** confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Hmi/HmiWriteMethods.cs:5-16 HmiWriteMethods.All`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:42-45 ProjectContentOperations`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:149-157 BuildClassifications`
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs:186-190 (VCI loops over VciMethods.Read/Write)`
  - `TiaMcpServer.OpennessWorker/Program.cs:188 hmi_* write arm`
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs:93-108 OperationFor`
- **Evidence:** The nine hmi_create/update/delete_* strings are written out literally in HmiWriteMethods.All, ProjectCapabilityCatalog.ProjectContentOperations and OperationPolicyCatalog. OperationPolicyCatalog builds VCI entries with foreach over VciMethods.Read/Write. HmiWriteMethods.All is referenced only by ProjectCapabilityCatalog.OperationFor and tests. OperationFor also maps HMI/VCI worker methods to their public tool (hmi_write/vci_write) but returns PLC/Network worker methods unchanged ('_ => method'). As a result, ProjectContentOperations lists update_block_logic, create_tag, add_network_device and so on next to the public tools, so the capability vocabulary mixes public tools with worker methods for PLC/Network.
- **Proposal:** In OperationPolicyCatalog, add `foreach (var m in HmiWriteMethods.All) dict[m] = ProjectMutation` (same pattern as VCI). In ProjectCapabilityCatalog, drop the nine HMI literals from ProjectContentOperations, since OperationFor already maps them to hmi_write. As a follow-up, decide whether PLC/Network worker methods should map to plc_write/network_write in OperationFor the way HMI/VCI do, so Describe() lists only public tools.
- **Risk:** Mapping the HMI list through a loop changes no behaviour. Removing the HMI literals from ProjectContentOperations drops them from Describe() output, which changes the conditional `context.capabilities` list. Check that against Multiuser docs and the capability tests. The PLC/Network remap is a contract change and needs a test update.
- **Verifier notes:** The nine HMI write literals appear in HmiWriteMethods.cs:7-15, ProjectCapabilityCatalog.cs:43-45 and OperationPolicyCatalog.cs:149-157. Worker Program.cs:188 has a further arm, and HmiWriteDispatch.cs:15 is the real dispatch. OperationPolicyCatalog.cs:186-190 builds the VCI entries by looping over VciMethods. ProjectCapabilityCatalog.OperationFor:96 maps HMI write methods to hmi_write before the content lists are consulted, so the HMI literals in ProjectContentOperations only add rows to Describe(). PLC/Network worker methods fall through to '_ => method' (line 107), so update_block_logic, create_tag and similar sit next to public tools in ProjectContentOperations. The only test pinning an HMI literal is ReadOnlyModeTests.cs:244 (an InlineData for policy).

### CONTRACTS-4 — 'Never ran' failure-category set copied into three verifiers (PLC uses constants, HMI/VCI use raw strings)

- **Verdict:** confirmed · **Severity:** medium · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer/Plc/PlcWriteVerifier.cs:30-35 NeverRan (WorkerFailureCategories constants)`
  - `TiaMcpServer/Hmi/HmiWriteVerifier.cs:15-16 NeverRan (string literals)`
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:11-12 NeverRan (string literals)`
  - `TiaMcpServer/Vci/VciWriteVerifier.cs:17 "portal_lost" or "worker_timeout" or "worker_crashed"`
  - `TiaMcpServer/Hmi/HmiWritePlanner.cs:178-179,286-287,296,329,345; TiaMcpServer/Hmi/HmiWorkingState.cs:47,66,119,121 raw category literals`
  - `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs:10-125`
- **Evidence:** All three verifiers define the same 7-element set {validation_error, target_not_found, target_ambiguous, state_changed, guard_blocked, binding_conflict, access_denied}. PLC builds it from WorkerFailureCategories constants; HMI and VCI hard-code the strings. A scan finds 30+ raw category literals in host Hmi/Vci files that duplicate WorkerFailureCategories constants, while VciWriteDomain.cs:66 uses the constants for the same timeout/crash check. A rename or new precondition category would drift silently.
- **Proposal:** Add `public static IReadOnlyCollection<string> PreconditionRefusals` (and optionally `TransportLoss` = PortalLost/WorkerTimeout/WorkerCrashed) to WorkerFailureCategories in Contracts. Make the three verifiers use it, and replace the raw literals in Hmi*/Vci* with the constants.
- **Risk:** No behaviour change. Mechanical edits in files owned by the host-area reviewers, so coordinate with them. Existing verifier tests cover it.
- **Verifier notes:** PlcWriteVerifier.cs:30-35 builds NeverRan from WorkerFailureCategories constants. HmiWriteVerifier.cs:15-16 and VciWriteVerifier.cs:11-12 hard-code the same 7 strings. VciWriteVerifier.cs:17 uses raw "portal_lost"/"worker_timeout"/"worker_crashed". More raw literals: HmiWorkingState.cs:47,66,119,121; HmiWritePlanner.cs:178,286; HmiWriteVerifier.cs:272. The '30+' count was not fully re-verified, but the pattern is clearly present.

### CONTRACTS-5 — IoTagMatcher is production-dead; the worker reimplements its checks inline

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Network/IoTagMatcher.cs:15-39 IoTagMatcher.MatchesChannel`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareIoMapReader.cs:232-246 inline area/start/width validation + IoTagIndex.FindMatches`
  - `TiaMcpServer.Tests/Network/IoTagMatcherTests.cs (only caller)`
  - `TiaMcpServer.Contracts/Network/IoChannelControllerResolver.cs:42,59 IoChannelControllerResult.RelevantAddress (assigned, never read)`
- **Corrected locations:**
  - `TiaMcpServer.Contracts/Network/IoTagMatcher.cs:15-39`
  - `TiaMcpServer.OpennessWorker/Openness/Network/HardwareIoMapReader.cs:232-246`
  - `TiaMcpServer.Tests/Network/IoTagMatcherTests.cs`
  - `TiaMcpServer.Contracts/Network/IoChannelControllerResolver.cs:42,54,59`
  - `/.github/labeler.yml:95`
- **Evidence:** IoTagMatcher appears only in its own file and in IoTagMatcherTests (15 refs). HardwareIoMapReader repeats the same guard (NormalizeArea null, ChannelAddressBits/ChannelWidthBits null or <0) and then calls IoTagIndex.FindMatches. IoChannelControllerResult.RelevantAddress is set in the constructor, but nothing in the worker or tests reads it.
- **Proposal:** Delete IoTagMatcher and IoTagMatcherTests, or have HardwareIoMapReader call a shared `IoLogicalAddressFormatter.TryChannelAddress(ioType,start,width, out IoAbsoluteIoAddress)` so the guard has one home. Delete RelevantAddress.
- **Verifier-corrected proposal:** As proposed. Also update the example in the .github/labeler.yml:95 comment if IoTagMatcher is deleted.
- **Risk:** Tests only. The worker change, if made, alters no behaviour; HardwareIoMapReader tests cover it.
- **Verifier notes:** IoTagMatcher is referenced only in IoTagMatcherTests.cs and in a comment at .github/labeler.yml:95 ('Contracts is included because its logic (for example IoTagMatcher) executes in the worker'), which the finding missed. HardwareIoMapReader.cs:232-246 inlines the same NormalizeArea/null/negative guard and then calls IoTagIndex.FindMatches. IoChannelControllerResult.RelevantAddress (IoChannelControllerResolver.cs:42,59) is assigned in the constructor and never read; the test-name hits are only words in method names.

### CONTRACTS-6 — ProjectSessionBinding is host-only mutable state in Contracts, with test-only public members

- **Verdict:** confirmed · **Severity:** medium · **Category:** good-practice · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:10-667 (lock-guarded mutable class)`
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:32 BoundProjectPath (49 test refs, 0 host)`
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:43 BindingState (20 test refs, 0 host)`
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:113 TryResolve (14 test refs; host uses only TryResolveWithSnapshot)`
  - `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs:408 IsBoundTo (1 test ref + FakeWorker comment)`
- **Evidence:** The 667-line ProjectSessionBinding holds `_gate`, `_state` and `_revision`, the process-local binding state machine. TiaMcpServer.OpennessWorker never references it; FakeWorker mentions it only in a comment (Program.cs:576). It sits in the netstandard2.0 'shared DTOs' assembly. Four public members are used only by tests and duplicate CaptureSnapshot().ProjectPath/.State. The TryResolveWithSnapshot doc says callers must use the snapshot variant to avoid a capture/resolve race, yet TryResolve stays public.
- **Proposal:** Delete BoundProjectPath, BindingState, TryResolve and IsBoundTo, and change the tests to use CaptureSnapshot().ProjectPath/State and TryResolveWithSnapshot. Optionally move ProjectSessionBinding (with ProjectBindingSnapshot) into TiaMcpServer/Worker and add it to the test csproj Compile Include list.
- **Risk:** About 85 test edits; no production behaviour change. Moving the class changes namespaces for host and tests and needs Compile Include wiring per AGENTS.md conventions.
- **Verifier notes:** A regex search for BoundProjectPath, BindingState, IsBoundTo and TryResolve( finds hits only in the definitions (ProjectSessionBinding.cs:32,43,113,408), in tests (about 85 hits across ProjectSessionBindingTests, OpennessWorkerClientIntegrationTests, ProjectLifecyclePreviewSafetyTests and others) and in a FakeWorker comment (Program.cs:576). It finds no host and no worker usage. ProjectSessionBinding has zero references in TiaMcpServer.OpennessWorker/** (absence evidence). The moving part is optional and correctly flagged as needing Compile Include wiring.

### CONTRACTS-7 — AGENTS.md calls Contracts 'shared DTOs', but it also holds worker-only algorithms and file IO

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** docs-drift · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `AGENTS.md Solution structure table: 'TiaMcpServer.Contracts | netstandard2.0 | Shared DTOs'`
  - `TiaMcpServer.Contracts/Network/IoChannelControllerResolver.cs, IoLogicalAddressFormatter.cs, IoTagIndex.cs, NetworkInterfacePathMatcher.cs (worker + tests only)`
  - `TiaMcpServer.Contracts/Vci/VciFormats.cs, VciMirroredPath.cs, VciStatusFilter.cs, VciPathPolicy.cs (worker + tests only)`
  - `TiaMcpServer.Contracts/Vci/VciBreadcrumb.cs:32-72 VciBreadcrumbFile (filesystem IO, bare catch at 47, 53, 69, own JsonSerializerOptions)`
  - `TiaMcpServer.Contracts/Project/ProjectOpenPolicy.cs, ProjectTreeFilter.cs (worker + tests only)`
- **Corrected locations:**
  - `AGENTS.md:70`
  - `TiaMcpServer.Contracts/Network/IoChannelControllerResolver.cs, IoLogicalAddressFormatter.cs, IoTagIndex.cs (worker + tests)`
  - `TiaMcpServer.Contracts/Vci/VciBreadcrumb.cs:33-72 (shared host+worker; optional catch narrowing at 49,55,69)`
- **Evidence:** A usage scan of Contracts types shows many pure-logic classes the host never references; they exist so tests can reach worker logic without Siemens DLLs. Tests already link 85 worker source files directly (including the Openness/Hmi/*.cs and Openness/Vci/*.cs globs), so two strategies for testing worker logic coexist. VciBreadcrumbFile does real filesystem IO with blanket `catch { }` and private camelCase options instead of WorkerJson, and it also manually re-checks members that are already marked [JsonRequired].
- **Proposal:** Low-cost fix: update the AGENTS.md row to 'shared DTOs, catalogs and Siemens-free pure logic used by host and/or worker'. Then pick one rule for new worker-only pure logic: Contracts, or a worker file linked into tests. No code moves are required. Optionally narrow VciBreadcrumbFile's catches to IOException/UnauthorizedAccessException/JsonException.
- **Verifier-corrected proposal:** Docs-only: change the AGENTS.md:70 row to 'shared DTOs, catalogs and Siemens-free pure logic used by host and/or worker'. Do not cite NetworkInterfacePathMatcher, VciPathPolicy or VciBreadcrumbFile as worker-only. Optionally narrow the VciBreadcrumbFile catches to IOException/UnauthorizedAccessException/JsonException/NotSupportedException; this is low value. Pick a rule for where new worker-only pure logic goes. Effort S.
- **Risk:** Docs only, unless code is moved, which would be L and is not recommended.
- **Verifier notes:** The AGENTS.md:70 row 'Shared DTOs (WorkerRequest, WorkerResponse, all info/result types)' does understate the contents: Contracts also holds catalogs (OperationPolicyCatalog, ProjectCapabilityCatalog) and pure logic. However, several of the listed 'worker + tests only' types are used by the host: NetworkInterfacePathMatcher (TiaMcpServer/Network/NetworkIdentityResolver.cs:329,360,386), VciPathPolicy (TiaMcpServer/Vci/VciWriteCatalog.cs:57,60) and VciBreadcrumbFile (TiaMcpServer/Vci/VciWriteDomain.cs:65 Take, VciBreadcrumbLocation.cs:7 DefaultPath). The worker writes VciBreadcrumbFile and the host reads it, so it is legitimately shared, and its best-effort catch-all is consistent with a crash breadcrumb that must never fail the call. Its private camelCase options are for a local file, not the worker wire, so reusing WorkerJson is not required.

### CONTRACTS-8 — FakeWorker/Program.cs is a 4,201-line top-level file: a ~172-case switch and ~25 mutable globals

- **Verdict:** confirmed · **Severity:** medium · **Category:** simplification · **Effort:** L · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.FakeWorker/Program.cs:25-128 top-level mutable state (multiHomed*, guardedNetwork*, subnetLifecycle*, lifecycle* counters)`
  - `TiaMcpServer.FakeWorker/Program.cs:500-1644 switch (scenario)`
  - `TiaMcpServer.FakeWorker/Program.cs:1649-4201 78 local functions (HandleGuardedNetwork 3630 ~160 lines, Respond 1798 ~136, InspectNetworkObjectFixture 3338 ~128, ListNetworkObjectsFixture 3214 ~124)`
  - `TiaMcpServer.FakeWorker/Plc/PlcWriteRoundtripScenario.cs, Hmi/HmiWriteRoundtripScenario.cs, Vci/*Scenario.cs (existing extraction pattern)`
- **Evidence:** PLC, HMI and VCI scenarios have already been moved into per-domain scenario classes. The Network (guarded, qualified, subnet lifecycle, multi-homed, IO map, hardware pagination), lifecycle/recovery and block-outcome scenarios are still inline, sharing closure-captured locals. Six branches repeat the same bootstrap `if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null) Respond("{...isOpen:true}")` (lines 614, 636, 690, 714, 754, 1291).
- **Proposal:** Following the existing PlcWriteRoundtripScenario pattern, extract Network/NetworkScenarios.cs (subnet lifecycle state, multi-homed, guarded/qualified state, HandleGuardedNetwork and fixtures), Lifecycle/LifecycleScenarios.cs and Block/BlockOutcomeScenarios.cs. Each owns its mutable state and gets a `bool TryHandle(scenario, method, line, out string response)`. Pull the repeated bootstrap status check into one helper called before dispatch.
- **Risk:** Test infrastructure only. Risk is changing scenario state sharing, such as subnet identity being shared across subnet-lifecycle keys (comment at lines 74-83). Run the full test suite after each extraction. No live verification needed.
- **Verifier notes:** TiaMcpServer.FakeWorker/Program.cs is 4,201 lines (wc -l). Only the Plc/, Hmi/ and Vci/ scenario classes have been extracted (PlcWriteRoundtripScenario.cs, HmiWriteRoundtripScenario.cs, VciRead/VciWriteRoundtripScenario.cs). The bootstrap check `ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null` repeats at lines 614, 636, 690, 714, 754 and 1291.

### CONTRACTS-9 — FakeWorker builds responses four ways; ToCamelCaseJson is a misnamed pass-through

- **Verdict:** confirmed · **Severity:** low · **Category:** inconsistency · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.FakeWorker/Program.cs:2492 string ToCamelCaseJson<T>(T value) => WorkerJson.SerializePayload(value) (66 uses vs 19 direct SerializePayload)`
  - `TiaMcpServer.FakeWorker/Program.cs:203,1090 JsonSerializer.Serialize(WorkerResponse) with default PascalCase options`
  - `TiaMcpServer.FakeWorker/Program.cs:795,2187,2651,2657 anonymous-object envelopes`
  - `TiaMcpServer.FakeWorker/Program.cs (93 raw """{"success"...}""" literals)`
  - `TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj comment: 'instead of hand-maintained escaped JSON fragments'`
- **Evidence:** The csproj says Contracts is referenced so scenarios serialize contract-shaped objects instead of escaped JSON fragments, yet 93 raw JSON literal envelopes remain. ToCamelCaseJson only forwards to WorkerJson.SerializePayload, and its name hides the null-member policy it actually applies. Lines 203 and 1090 serialize WorkerResponse without WorkerJson.Envelope, producing PascalCase keys, which only works because the host reads case-insensitively.
- **Proposal:** Inline ToCamelCaseJson as WorkerJson.SerializePayload (rename-all). Send every WorkerResponse through one `Respond(WorkerResponse)` overload that uses WorkerJson.Envelope. Leave the intentional malformed or raw literals (protocol-error scenarios) and convert ordinary success/failure literals as part of the CONTRACTS-8 extraction.
- **Risk:** Small risk that some test depends on exact raw-literal shape (for example omitted members); convert only non-malformed scenarios. Test-only.
- **Verifier notes:** Program.cs:2492 `string ToCamelCaseJson<T>(T value) => WorkerJson.SerializePayload(value);` is a pass-through; the comment at 2489-2491 explains that it uses the payload policy, which the name hides. Program.cs:203 `JsonSerializer.Serialize(identityFailure)` and Program.cs:1090 `JsonSerializer.Serialize(BindingConflict(...))` use default options (PascalCase), not WorkerJson.Envelope. That works only because the host reads case-insensitively.

### CONTRACTS-10 — FakeWorker scenario keys that no test references

- **Verdict:** partially-confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.FakeWorker/Program.cs:635 case "block-outcome-audit-failure"`
  - `TiaMcpServer.FakeWorker/Program.cs:710-713 block-outcome-transport-hang/crash/malformed/null-response`
  - `TiaMcpServer.FakeWorker/Program.cs:736,739 block-outcome-status-failure, block-outcome-status-failure-with-stale-outcome`
  - `TiaMcpServer.FakeWorker/Program.cs:1475-1477 network-guarded-incomplete-node/root/selector`
  - `TiaMcpServer.FakeWorker/Program.cs:1048,2821 project-tree-v3-counted`
- **Corrected locations:**
  - `TiaMcpServer.FakeWorker/Program.cs:635`
  - `TiaMcpServer.FakeWorker/Program.cs:710-713`
  - `TiaMcpServer.FakeWorker/Program.cs:736,739`
  - `TiaMcpServer.FakeWorker/Program.cs:1048,2821`
- **Evidence:** I extracted 172 case labels from the scenario switch and searched TiaMcpServer.Tests for each one, including composed keys ("network-guarded-identity-" + kind, "network-qualified-late-" + kind, "network-qualified-final-" + kind + "-drift", "network-guarded-late-io-" + kind; those are used). For the listed keys I found no literal, prefix concatenation or interpolation. Tests use only block-outcome-success/postcondition/partial/preimport-*, network-guarded-incomplete (exact), project-tree-v3-small and project-tree-v3-one-shot.
- **Proposal:** Before deleting, confirm with a temporary throw-on-hit build or a coverage run, then remove the unused case arms and their helpers (for example the observation counter branch in ProjectTreeV3ScenarioResponse).
- **Verifier-corrected proposal:** Do not touch the network-guarded-incomplete-node/root/selector arms. For block-outcome-audit-failure, the block-outcome-transport-* and block-outcome-status-failure* keys, and project-tree-v3-counted (with the counter branch at Program.cs:2821), confirm them unused with a throw-on-hit or coverage run, then delete them. Effort S, test-only.
- **Risk:** Medium confidence: a key could be built in a way the text search missed (for example from a file name), which is why runtime confirmation comes first. Test-only.
- **Verifier notes:** The network-guarded-incomplete-node/root/selector part is refuted. NetworkGuardedWriteDomainTests.cs:59 iterates kinds {"subnet","node","root","selector"} and line 67 composes "network-guarded-incomplete" + "-" + kind, so all three keys are used. The other keys have no hits in TiaMcpServer.Tests/** for literals or composed forms: block-outcome-audit-failure (Program.cs:635), block-outcome-transport-hang/crash/malformed/null-response (710-713), block-outcome-status-failure and -with-stale-outcome (736, 739), and project-tree-v3-counted (1048, 2821). Note that tests may also derive the scenario from a project path (for example LocalSessionCapabilityTests.cs:55 'project-tree-v3-small.ap21'), so runtime confirmation is still needed before deleting.

### CONTRACTS-11 — Repository-root/source-file helpers copied into ~16 test files using 3 different strategies

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** S · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Tests/Network/HardwareDeviceSelectionTests.cs:9,27 FindRepositoryFile/ReadRepositorySource (walk up for file)`
  - `TiaMcpServer.Tests/Network/NetworkIntrospectionWorkerDispatchTests.cs:285; NetworkSubnetLifecycleWorkerDispatchTests.cs:173; NetworkSubnetLifecycleWorkerServiceContractTests.cs:386; Project/ProjectMetadataWorkerContractTests.cs:225 FindRepositoryFile (walk up for TiaMcpServer.slnx)`
  - `TiaMcpServer.Tests/Worker/LifecycleSecondIdentityValidationContractTests.cs:198 FindRepositoryFile`
  - `TiaMcpServer.Tests/Block/BlockImporterRouteSourceTests.cs:160; Network/HardwarePaginationWorkerSourceContractTests.cs:84; Network/NetworkDeviceCreatorWorkerContractTests.cs:132; Project/ProjectTraversalSourceContractTests.cs:82 ReadRepositorySource`
  - `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs:401; CoverageThresholdScriptTests.cs:120; DoctorPackageVerificationScriptTests.cs:786 GetRepositoryRoot (fixed ../../../..); Diagnostics/ReferenceStubArtifactTests.cs:100 RepositoryRoot`
  - `CountOccurrences copies: NetworkIntrospectionWorkerDispatchTests.cs:271, NetworkSubnetLifecycleWorkerDispatchTests.cs:122, NetworkSubnetLifecycleWorkerServiceContractTests.cs:372, ProjectMetadataWorkerContractTests.cs:211`
- **Corrected locations:**
  - `TiaMcpServer.Tests/Block/BlockImporterRouteSourceTests.cs:160`
  - `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs:401`
  - `TiaMcpServer.Tests/Diagnostics/CoverageThresholdScriptTests.cs:120`
  - `TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs:786`
  - `TiaMcpServer.Tests/Diagnostics/ReferenceStubArtifactTests.cs:100`
  - `TiaMcpServer.Tests/Cli/StartupProcess.cs:9`
  - `TiaMcpServer.Tests/Diagnostics/OpennessWorkerCheckTests.cs:164`
  - `TiaMcpServer.Tests/Network/HardwareDeviceSelectionTests.cs:9,27 and the other FindRepositoryFile/CountOccurrences copies listed`
- **Evidence:** Identical-body detection found byte-identical FindRepositoryFile (4 copies), ReadRepositorySource (3 copies) and CountOccurrences (4 copies), plus variants. The fixed '../../../..' approach breaks if the output path depth changes (for example a RID-specific output directory), while the slnx walk does not.
- **Proposal:** Add TestSupport/RepositoryFiles.cs with `Root` (walk up to TiaMcpServer.slnx), `Path(params string[])`, `ReadSource(params string[])` (normalizes CRLF) and `CountOccurrences`. Replace all copies.
- **Verifier-corrected proposal:** As proposed. Also fold in Cli/StartupProcess.cs:9 and Diagnostics/OpennessWorkerCheckTests.cs:164, and make ReferenceStubArtifactTests.RepositoryRoot delegate to the shared helper, since ReferenceStubVerificationScriptTests.cs:122,125 already reuses it.
- **Risk:** Test-only, mechanical. Each source-contract test keeps its assertions.
- **Verifier notes:** Search confirms the copies: ReadRepositorySource at BlockImporterRouteSourceTests.cs:160, and GetRepositoryRoot at CiWorkflowTests.cs:401, CoverageThresholdScriptTests.cs:120 and DoctorPackageVerificationScriptTests.cs:786. ReferenceStubArtifactTests.cs:100 uses a fixed ../../../.. RepositoryRoot. There are more fixed-depth copies than listed: Cli/StartupProcess.cs:9 and Diagnostics/OpennessWorkerCheckTests.cs:164.

### CONTRACTS-12 — ~15 near-duplicate 'read canonical tool document' helpers that check different things, plus 17 CreateClient copies

- **Verdict:** confirmed · **Severity:** low · **Category:** simplification · **Effort:** M · **Reviewer confidence:** high
- **Locations:**
  - `TiaMcpServer.Tests/Network/NetworkGuardedWriteMcpTests.cs:138 Document (checks text == CanonicalJson.Serialize(structured))`
  - `TiaMcpServer.Tests/Vci/VciGuardedWriteMcpTests.cs:16 Document => NetworkGuardedWriteMcpTests.Document (cross-domain coupling)`
  - `TiaMcpServer.Tests/TestUtilities/LifecycleTestCalls.cs:17 Document (checks raw text equality)`
  - `TiaMcpServer.Tests/Tools/BindProjectToolProtocolTests.cs:44 Document (StructuredContractInspector.FindViolations)`
  - `TiaMcpServer.Tests/Tools/LifecycleMcpProtocolTests.cs:439; Safety/Pipeline/WriteConfirmationPipelineTests.cs:321 Document (parses text only, no structured check)`
  - `TiaMcpServer.Tests/Network/NetworkIoMapFakeWorkerTests.cs:336; NetworkStructuredProtocolTests.cs:515; NetworkSubnetLifecycleFakeWorkerTests.cs:127; Project/ProjectTreeStructuredProtocolTests.cs:185 AssertOneCanonicalDocument`
  - `TiaMcpServer.Tests/CrossReferences/CrossReferenceReadToolsTests.cs:34; Plc/PlcReadToolsTests.cs:33; Network/NetworkOperationFakeWorkerTests.cs:656; Safety/Pipeline/WriteExecutionFakeWorkerTests.cs:162 Structured (no text check)`
  - `CreateClient copies, e.g. CrossReferences/CrossReferenceReadToolsTests.cs:18, Hmi/HmiReadFakeWorkerTests.cs:18, Plc/PlcReadToolsTests.cs:18 (17 files; 67 FakeWorkerLocator.Locate() call sites in 40 files)`
- **Evidence:** AGENTS.md requires text and structuredContent to come from one CanonicalJson.Serialize call. Some helpers check that and some don't (LifecycleMcpProtocolTests and WriteConfirmationPipelineTests parse text only; the 'Structured' helpers read structured only), so per-test assertion strength drifts. StructuredContractInspector already exists in TestSupport, but only a few helpers call it. CreateClient bodies differ only in access mode and binding.
- **Proposal:** Add TestSupport/CanonicalToolDocument.Read(CallToolResult) that asserts FindViolations is empty, checks text == CanonicalJson.Serialize(structured) and returns the root. Replace the ~15 helpers (VCI then no longer depends on the Network test class). Add TestSupport/FakeWorkerClients.Create(McpAccessMode mode = ReadOnly, ProjectSessionBinding? binding = null) and use it in place of the 17 CreateClient copies.
- **Risk:** Stricter shared assertions may expose real text/structured mismatches in tools whose tests currently check only one side. That would be useful but may need fixes. Test-only.
- **Verifier notes:** Search finds 16 helpers named Document/Structured/AssertOneCanonicalDocument with differing strength. LifecycleMcpProtocolTests.cs:439-443 parses text only. CrossReferenceReadToolsTests.cs:34 and PlcReadToolsTests.cs:33 read StructuredContent only. VciGuardedWriteMcpTests.cs:16 delegates to NetworkGuardedWriteMcpTests.Document. NetworkSubnetLifecycleFakeWorkerTests has both Structured (119) and AssertOneCanonicalDocument (127). ToolOutputContractConformanceTests partly mitigates this with per-tool probes, but per-test assertion strength still drifts.

### CONTRACTS-13 — TestUtilities vs TestSupport split is muddled; FakeWorkerLocator prefers a stale Debug build

- **Verdict:** confirmed · **Severity:** low · **Category:** good-practice · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.Tests/TestUtilities/LifecycleTestCalls.cs:9 (only file in namespace TiaMcpServer.Tests.TestUtilities)`
  - `TiaMcpServer.Tests/TestUtilities/*SiemensDoubles.cs (all in Siemens.Engineering.* namespaces)`
  - `TiaMcpServer.Tests/TestSupport/*.cs (10 files, namespace TiaMcpServer.Tests.TestSupport)`
  - `TiaMcpServer.Tests/TestSupport/FakeWorkerLocator.cs:14-22 Debug checked before Release in each directory`
- **Evidence:** Apart from LifecycleTestCalls, TestUtilities contains only Siemens API test doubles. LifecycleTestCalls is a harness helper like LifecycleProtocolFixture in TestSupport. FakeWorkerLocator checks bin/Debug before bin/Release at each directory level, so `dotnet test -c Release` silently runs an older Debug FakeWorker.exe if one exists.
- **Proposal:** Move LifecycleTestCalls into TestSupport and rename TestUtilities to SiemensDoubles. In FakeWorkerLocator, check the configuration of the running test assembly first (derive it from AppContext.BaseDirectory's bin/<Config> segment), then fall back.
- **Risk:** Namespace and using updates in tests only. The locator change can only make runs more correct.
- **Verifier notes:** In FakeWorkerLocator.cs:14, `foreach (var configuration in new[] { "Debug", "Release" })` checks Debug first at each level. The test csproj:85 ProjectReferences FakeWorker, so `-c Release` builds a fresh Release exe, yet a stale Debug exe would be picked if one exists; both bin/Debug and bin/Release exist in this checkout. In TestUtilities, every file except LifecycleTestCalls.cs (namespace TiaMcpServer.Tests.TestUtilities) declares Siemens.Engineering* namespaces.

### CONTRACTS-14 — Reserved vocabulary values that nothing produces

- **Verdict:** confirmed · **Severity:** low · **Category:** dead-code · **Effort:** S · **Reviewer confidence:** medium
- **Locations:**
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityApplicabilities.cs:9 ServerProjectOnly`
  - `TiaMcpServer.Contracts/Project/ProjectServerConnectionObservationSources.cs:12-13 OperationPreflight, PostFailure`
  - `TiaMcpServer/Worker/ProjectContextPayloadContract.cs:48 accepts any value in ProjectServerConnectionObservationSources.All`
- **Evidence:** ProjectCapabilityCatalog.Build never emits ServerProjectOnly (server rows are NotYetDelivered or Unsupported). No worker or host code produces operationPreflight or postFailure; PostFailure appears in one test. They come from the Multiuser PR1 spec (docs/superpowers/specs/2026-09-28-multiuser-engineering-design.md:515). Because the host validator accepts any member of All, it admits observation sources no producer emits.
- **Proposal:** Either drop them from the constants and the All lists until a producer exists, or mark them with a `/// Reserved: no producer yet` comment and exclude them from the host payload validator's accepted set.
- **Risk:** Changes the accepted inbound vocabulary; MultiuserFoundationContractTests asserts the ordered All lists. Spec-reserved values, so the maintainer may prefer to keep them.
- **Verifier notes:** ProjectCapabilityApplicabilities.ServerProjectOnly (line 9) and ProjectServerConnectionObservationSources.OperationPreflight/PostFailure (lines 12-13) appear only in their declaring files and in MultiuserFoundationContractTests.cs:34,51,145,153. They have no producer in the host or the worker, and current docs (outside docs/superpowers) do not mention them. The host validator ProjectContextPayloadContract.cs:47-48 accepts any member of ProjectServerConnectionObservationSources.All, so it admits operationPreflight/postFailure from a worker that never emits them. Low severity; maintainer may keep them as spec-reserved.


