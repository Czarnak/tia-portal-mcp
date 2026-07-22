# Acceptance Test Report

**Branch:** `1be9f18549cbdb364ce0408296737a86ba9d3250`  
**AC Document:** `C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\docs\superpowers\acceptance\2026-07-21-round4-binding-and-contract-integrity.md`  
**Date:** 2026-07-22 01:01:11 +02:00  
**Report:** `C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\docs\superpowers\acceptance\reports\2026-07-22-01-01-round4-binding-and-contract-integrity.md`

---

## Commit and dependency checks

- `git rev-parse HEAD` returned `1be9f18549cbdb364ce0408296737a86ba9d3250` before testing and again before report creation.
- `git status --short` returned no tracked or untracked changes before report creation (Git emitted only a permission warning for the user-global ignore file).
- All criteria are Logic. No Playwright skill or UI infrastructure is required.
- No criterion precondition depends on another acceptance criterion. No dependency blocks were recorded.

## Results

| ID | Description | Test Type | Result | Evidence |
|----|-------------|-----------|--------|----------|
| AC-001 | Safety-token presentation JSON is immutable after startup. | Logic | PASS | Focused `TiaJsonTests`: 3 passed, 0 failed, 0 skipped. |
| AC-002 | Worker-resolved path is adopted only after a successful response. | Logic | PASS | Five focused FakeWorker/client integration tests: 5 passed, 0 failed, 0 skipped. |
| AC-003 | Session-path resolution is non-mutating before worker success. | Logic | PASS | `ProjectSessionBindingTests`: 21 passed, 0 failed, 0 skipped. |
| AC-004 | Read-side project-open policy makes deterministic use/open/refuse decisions. | Logic | PASS | `ProjectOpenPolicyTests`: 8 passed, 0 failed, 0 skipped. |
| AC-005 | Write tools use injected safety service and hide it from MCP schemas. | Logic | PASS | `AuditIsolationTests` plus `McpToolSchemaTests`: 11 passed; exact indexed-source search for `WriteSafetyService.Shared`: 0 results. |
| AC-006 | Batch catalog declares the exact supported required/optional field surface. | Logic | PASS | Eight focused catalog-contract tests: 8 passed, 0 failed, 0 skipped. |
| AC-007 | Batch validation rejects fields the selected operation would discard. | Logic | PASS | Seven focused validation tests: 7 passed, 0 failed, 0 skipped. |
| AC-008 | Every declared batch field survives the host-to-worker request path. | Logic | PASS | FakeWorker build: 0 warnings, 0 errors; `BatchFieldForwardingTests`: 26 passed, 0 failed, 0 skipped. |
| AC-009 | Documentation accurately describes delivered behavior and hardware-gated deferrals. | Logic | PASS | Deterministic documentation harness: 8/8 assertions passed. |
| AC-010 | `get_project_status(projectPath)` is explicitly carried as the human-approved Round 5 exception. | Logic | **FAIL** | Deterministic lifecycle/documentation harness: 5/6 assertions passed. Code path, exception reason, no-switch guidance, and improvement-plan Round 5 disclosure passed; README Round 5 disclosure failed. |
| AC-011 | Deferred scope remains excluded from Round 4 behavior. | Logic | PASS | Deterministic code/change-set/documentation harness: 6/6 assertions passed; targeted changed paths were empty. |

## Focused commands and observed output

### AC-001

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~TiaMcpServer.Tests.TiaJsonTests" --logger "console;verbosity=minimal"
```

Observed: exit 0; 3 passed, 0 failed, 0 skipped, 3 total.

### AC-002

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~FailedFirstWorkerResponse_DoesNotBindTheSession|FullyQualifiedName~SuccessfulFirstRequest_BindsToTheResolvedPathAndRejectsADifferentProjectAfterward|FullyQualifiedName~UnboundSession_BindsToTheWorkerReportedPathAfterSuccess|FullyQualifiedName~FailedCall_LeavesTheSessionUnbound|FullyQualifiedName~SuccessfulCallWithoutAResolvedPath_LeavesTheSessionUnbound" --logger "console;verbosity=minimal"
```

Observed: exit 0; 5 passed, 0 failed, 0 skipped, 5 total.

### AC-003

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~TiaMcpServer.Tests.ProjectSessionBindingTests" --logger "console;verbosity=minimal"
```

Observed: exit 0; 21 passed, 0 failed, 0 skipped, 21 total.

### AC-004

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~TiaMcpServer.Tests.ProjectOpenPolicyTests" --logger "console;verbosity=minimal"
```

Observed: exit 0; 8 passed, 0 failed, 0 skipped, 8 total.

### AC-005

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~TiaMcpServer.Tests.AuditIsolationTests|FullyQualifiedName~TiaMcpServer.Tests.McpToolSchemaTests" --logger "console;verbosity=minimal"
```

Observed: exit 0; 11 passed, 0 failed, 0 skipped, 11 total.

Static assertion command:

```text
jcodemunch order(action="search_text", args={repo:"Czarnak/tia-portal-mcp", query:"WriteSafetyService.Shared", context_lines:0, max_results:100})
```

Observed: `result_count: 0`, `results: []` on the index refreshed from the tested HEAD.

### AC-006

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~All_ExposesEverySpec|FullyQualifiedName~All_MatchesTheAuthoritativeOperationFieldContract|FullyQualifiedName~ConfigureNetworkDevice_DoesNotDeclareDeviceItemName|FullyQualifiedName~AddNetworkDevice_DeclaresDeviceItemName|FullyQualifiedName~CreateTag_DoesNotDeclareTheExternalAttributes|FullyQualifiedName~UpdateTag_DeclaresTheExternalAttributes|FullyQualifiedName~NoSpecDeclaresAUniversalFieldAsOptional|FullyQualifiedName~RequiredAndOptionalFieldsNeverOverlap" --logger "console;verbosity=minimal"
```

Observed: exit 0; 8 passed, 0 failed, 0 skipped, 8 total.

### AC-007

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~DeviceItemNameOnConfigureNetworkDevice_IsRejected|FullyQualifiedName~ExternalAttributesOnCreateTag_AreRejected|FullyQualifiedName~ExternalAttributesOnUpdateTag_AreAccepted|FullyQualifiedName~InapplicableFieldErrors_AggregateWithOtherErrors|FullyQualifiedName~DepthOnANonTreeOperation_IsStillRejected|FullyQualifiedName~DepthBelowOne_IsStillRejected|FullyQualifiedName~Validate_RejectsOutOfRangeBounds" --logger "console;verbosity=minimal"
```

Observed: exit 0; 7 passed, 0 failed, 0 skipped, 7 total.

### AC-008

```powershell
dotnet build TiaMcpServer.FakeWorker\TiaMcpServer.FakeWorker.csproj --no-restore -m:1 --verbosity minimal
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~TiaMcpServer.Tests.BatchFieldForwardingTests" --logger "console;verbosity=minimal"
```

Observed: build exit 0 with 0 warnings and 0 errors; test exit 0 with 26 passed, 0 failed, 0 skipped, 26 total.

### AC-009 through AC-011

The exact static-assertion command was `ctx_execute(language="javascript", cwd=REPO_ROOT, timeout=30000, code=<below>)`:

```javascript
const fs=require('fs'),path=require('path'),cp=require('child_process');
const root='C:\\Users\\LCZ\\Desktop\\RnD\\TIA-Portal\\tia-portal-mcp',read=r=>fs.readFileSync(path.join(root,r),'utf8'),norm=s=>s.replace(/\s+/g,' ').replace(/[`*]/g,'');
const R=norm(read('README.md')),P=norm(read('docs/IMPROVEMENT_PLAN.md')),raw=read('README.md')+'\n'+read('docs/IMPROVEMENT_PLAN.md');
const I=read('TiaMcpServer/Batch/BatchWorkerInvoker.cs'),T=read('TiaMcpServer/Tools/ProjectLifecycleTools.cs'),C=read('TiaMcpServer/Worker/OpennessWorkerClient.cs'),L=read('TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs');
const create=I.split(/\r?\n/).find(x=>x.includes('"create_tag" =>'))||'',changed=cp.execSync('git diff --name-only c467a97..HEAD -- TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs TiaMcpServer/Batch/BatchPayloadBudget.cs',{cwd:root,encoding:'utf8'}).trim();
const groups={
'AC-009':[
['immutable JSON',P.includes('TiaJson.Presentation.MakeReadOnly(). DONE (Round 4): presentation serialization options are frozen')],
['DI audit isolation',P.includes('now receives WriteSafetyService through DI, and tests inject a temporary audit directory')],
['validation errors',P.includes('catalog validation now rejects deviceItemName')&&P.includes('create_tag now errors when any of these attributes is supplied')],
['forwarding proof',P.includes('forwarding test now covers every declared field')],
['hardware-gated create_tag',P.includes('remains a hardware-gated decision')&&P.includes('Next round (needs TIA Portal hardware)')],
['open_project deliberate switch',R.includes('Use open_project for deliberate session switching.')],
['no stale Shared singleton',!raw.includes('WriteSafetyService.Shared')],
['no stale blanket status-policy claim',!P.includes('All user-facing read paths, including get_project_status, now use the read-side open policy.')]],
'AC-010':[
['tool forwards projectPath',/GetProjectStatusAsync\(projectPath\)/.test(T)],
['client uses lifecycle request route',/SendBoundProjectRequestAsync\([\s\S]{0,120}"get_project_status",[\s\S]{0,80}projectPath/.test(C)],
['lifecycle path can open supplied path',/if \(!string\.IsNullOrWhiteSpace\(projectPath\)\)[\s\S]{0,100}session\.OpenProject\(projectPath!\)/.test(L)],
['README exception/reason/no-switch',R.includes('get_project_status(projectPath) is currently excluded from that policy because it shares a lifecycle RPC with guarded write-state probes; do not use it to switch projects.')],
['README Round 5 disclosure',/get_project_status\(projectPath\)[\s\S]{0,500}Round 5/i.test(R)],
['improvement-plan Round 5 exception/reason/no-switch',/get_project_status\(projectPath\)[\s\S]{0,250}known exception deferred to Round 5[\s\S]{0,250}guarded write-state probes[\s\S]{0,250}do not use it to switch projects/i.test(P)]],
'AC-011':[
['create_tag attributes unforwarded',!/ExternalAccessible|ExternalVisible|ExternalWritable|IsSafety/.test(create)],
['network configurator and payload budget unchanged',changed===''],
['double dispatch remains',I.includes('op.Operation switch')&&(I.match(/=> client\./g)||[]).length>=25],
['double-dispatch collapse deferred',/Collapse the double dispatch:[\s\S]{0,300}DEFERRED 2026-07-20/i.test(P)],
['payload-budget collapse remains undone',P.includes('deliberately left undone')&&P.includes('Collapse BatchPayloadBudget.ReadBatchResponseLength into BatchResultFormatter.ReadBatch')],
['hardware follow-up deferred',/Next round \(needs TIA Portal hardware\): forward externalAccessible\/\s*externalVisible\/\s*externalWritable\/\s*isSafety on create_tag/i.test(P)]]};
let fail=false;for(const [ac,a] of Object.entries(groups)){for(const [n,ok] of a){console.log(ac+' '+(ok?'PASS ':'FAIL ')+n);if(!ok)fail=true;}console.log(ac+' '+a.filter(x=>x[1]).length+'/'+a.length+' assertions passed');}console.log('create_tag='+create.trim());console.log('targeted_changed_paths='+(changed||'(none)'));if(fail)process.exitCode=1;
```

Observed:

```text
AC-009 8/8 assertions passed
AC-010 5/6 assertions passed
AC-010 FAIL README Round 5 disclosure
AC-011 6/6 assertions passed
create_tag="create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress, op.ProjectPath),
targeted_changed_paths=(none)
Exit code: 1
```

The non-zero exit is the deterministic failure signal for AC-010; AC-009 and AC-011 completed all assertions successfully.

## Summary

**Total criteria:** 11  
**Passed:** 10  
**Failed:** 1  
**Blocked:** 0 (0 due to failed dependency, 0 due to missing infrastructure)

## Failed and Blocked Criteria (detail)

**AC-010: The known `get_project_status(projectPath)` lifecycle exception is carried forward explicitly rather than misstated as enforced read policy**

- Result: FAIL
- Command run: the AC-009 through AC-011 JavaScript static-assertion command above.
- Output: `AC-010 5/6 assertions passed`; `AC-010 FAIL README Round 5 disclosure`; exit code 1.
- Reason: the code path still forwards `projectPath` through the lifecycle RPC and can open the supplied path; `docs/IMPROVEMENT_PLAN.md` explicitly calls this the known Round 5 exception and says not to use it for switching. Both duplicated README binding sections disclose the exception, the shared write-state-probe reason, and the prohibition on switching, but neither states that the exception is deferred to Round 5. This does not meet AC-010's requirement that both documents state the Round 5 exception.
- Suggested fix: update both duplicated README session-binding disclosures to identify `get_project_status(projectPath)` explicitly as the human-approved Round 5 exception while retaining the current reason and `open_project` switching guidance.

No criteria were Blocked.

## Overall Verdict

**FAIL** — 1 criterion did not pass. The main agent must create a fix task in `executing-plans` for AC-010, then rerun review and acceptance testing.
