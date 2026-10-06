# Multiuser PR 3 offline qualification

**Status:** Refreshed offline qualification passed. Whole-candidate review and the scoped
final-fix re-review are Approved with no remaining findings. Exact live authorization was received
on October 6; the [separate installed-tool report](2026-10-06-multiuser-pr3-live-verification.md)
records partial Task 5 verification. This report makes no merge-ready claim.

**Compiled source/test candidate:** `3f45b940ebc66752f14590b900218fcca336d665` on
`feature/multiuser-pr3`, with base `ea269c222e7415af347f2929cbc82a366aa2ee0f`. The final fix
reconciles successful primary and supplemental Portal-discovery identity against an existing
verified binding before payload decoding. Healthy discovery preserves binding/cursors; observed
loss invalidates them. Unverified/configured discovery still never promotes or selects a project.
The separate historical-status clarification is `4ca82c9fa2a675eaf4ce55869dc5295b7ee59feb`, also
the local package head. Later report/status-only commits retain this source and package evidence.

The prior 5,536-case qualification at `29468ba` is superseded by the fresh results below. Only
its installed compile-only probe and generated-reference drift evidence are reused: worker and
reference source are unchanged, and all four reference DLL hashes still match. No runtime probe
or live acceptance is inferred from that evidence.

## Final-review regression evidence

Corrected RED: six discovery-loss cases failed the host outcome assertions after confirming
freshly null project path/removed Portal and primary stamped identity loss. GREEN: 311 focused
client, binding, canonical protocol, discovery and cursor tests passed (49 s). The shared fix is
five host lines using the existing verified-identity reconciliation helper. The independent
scoped re-review of `6182adf..4ca82c9` was Approved, with both findings resolved and no new breakage.

Initial test-fixture compile errors and inactive-scenario attempts are excluded from RED evidence.
`FakeWorkerLocator` prefers Debug: that actual binary was rebuilt from current source, and the new
scenario markers use explicit filename matching. The selected executable is
`TiaMcpServer.FakeWorker/bin/Debug/net10.0/TiaMcpServer.FakeWorker.exe`; its hashes are recorded below.
Detailed commands and correction history remain in the local ignored `final-review-fix-report.md`.

## Serial offline gates

All refreshed gates completed with exit code 0. Logs and exit files are retained locally under
`.superpowers/sdd/2026-10-05-multiuser-pr3-read-only-inventory/final-review/`.
Reused probe/drift logs remain in its sibling `qualification/`. No parallel builds
or tests ran. Restore was already valid; every command below used existing restored assets.

| Gate | Result | Local log |
| --- | --- | --- |
| Matching Release/stub solution build before subprocess tests | 0 warnings/errors; 19.43 s | `stub-build.log` |
| Full serial suite plus XPlat coverage | 5,556 passed, 0 failed/skipped; 10 m 10 s | `full-coverage.log` |
| Existing scoped 80% line threshold | 93.84%, 11,138/11,869 lines; passed unchanged scope/threshold | `coverage-threshold.log` |
| Generated source-reference drift verifier | Reused source-unchanged evidence at `29468ba`; existing Step7 warning noted below | `qualification/reference-drift.log` |
| Installed V21 compile-only probe | Reused source-unchanged evidence at `29468ba`; 0 warnings/errors | `qualification/installed-probe.log` |
| Installed V21 Release solution build | 0 warnings/errors; 39.16 s | `installed-solution.log` |
| Local NuGet pack using installed references | Traceable local version; no installation/publication | `package-build.log` |
| Package layout/leak check | One canonical worker subtree, 15 required files, no worker runtimeconfig/Siemens DLL | `package-verify.log` |
| Additional archive check | 0 ref/stub/probe entries; one README exactly matches maintained source | `package-extra.log` |
| Documentation links/anchors/JSON and whitespace | 13 touched Markdown files, 288 local/repository links, 17 JSON examples; whitespace clean | `docs-final-check.log` |

Commands (each build uses `-m:1`; test parallelism is explicitly disabled):

```powershell
dotnet build TiaMcpServer.slnx -c Release -m:1 /p:UseTiaPortalReferenceStubs=true --no-restore
dotnet test TiaMcpServer.Tests -c Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/pr3-20261006-review-fix --logger "trx;LogFileName=pr3-review-fix.trx" -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath TestResults/pr3-20261006-review-fix/12bc38f3-ade7-4194-9010-442c755db8e0/coverage.cobertura.xml -MinimumLineRate 0.80
dotnet build TiaMcpServer.slnx -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 --no-restore -o artifacts/multiuser-pr3-offline /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" /p:Version=3.0.1-local.319.g4ca82c9 /p:PackageVersion=3.0.1-local.319.g4ca82c9 /p:InformationalVersion=3.0.1-local.319.g4ca82c9 /p:IncludeSourceRevisionInInformationalVersion=false
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath artifacts/multiuser-pr3-offline/TiaMcpServer.3.0.1-local.319.g4ca82c9.nupkg
git diff --check
```

## Coverage and executable worker evidence

The unchanged coverage scope has branch rate 85.63% (5,814/6,789). Added/modified executable
host lines in `ea269c2..3f45b94` intersected with Cobertura, unioned by file/line across measured
classes, give **204/211 lines (96.68%)** and **192/224 branches (85.71%)**. Deleted lines and
non-executable declarations are not measurable lines; this is a changed-line diagnostic, not
a new coverage gate or a broadened scope.

| Changed host file | Covered lines | Covered branches |
| --- | --- | --- |
| `ProjectBindingInspectionCatalog.cs` | 48/48 | 101/110 |
| `ProjectBindingInspectionPayloadContract.cs` | 61/62 | 60/76 |
| `ProjectBindingTools.cs` | 24/30 | 25/32 |
| `StandaloneToolResponses.cs` | 10/10 | 0/0 |
| `StructuredStandaloneResult.cs` | 2/2 | 6/6 |
| `OpennessWorkerClient.cs` | 59/59 | 0/0 |
| `WorkerCallResult.cs` | 0/0 | 0/0 |

Worker source and Siemens doubles are excluded by `coverage.runsettings`. The completed TRX
separately records 30 `MultiuserInventoryWorkerTests`, 27 `MultiuserPortalReadDispatchTests`
and 26 `ActiveProjectContextSessionTests`, all passed (83 executable source-linked cases).
These establish offline worker logic; they are not measured worker coverage or installed runtime
acceptance. The full suite also covers strict registered inputs, default/null binding shape,
host binding/cursor continuity, typed canonical payloads, secret sanitization and omission limits.

## Artifact provenance

The package version follows `git describe --tags --long` (`v3.0.1-319-g4ca82c9`) and the existing
local-version procedure. Package:
`artifacts/multiuser-pr3-offline/TiaMcpServer.3.0.1-local.319.g4ca82c9.nupkg`.

| Artifact | SHA256 |
| --- | --- |
| Local package | `3CE5F088739EDCD0A9518AD73CE990B20E12C6BD4541182BD82D418ACF4282A3` |
| Pack-built host DLL | `EB8264BE40A0CD9C21314E3BACBAE0714BDD7DBC88C15EC410340BF3CFAF9DF0` |
| Pack-built embedded worker EXE | `2930756A057399381E8159F736698F273082F81A8DE0D71F017BE5A66DC49CDA` |
| Pack-built contracts DLL | `7A3AD7EAD96D420958530DC748283C07E901FF11C55E95C6018C4EFBDA5A0AAA` |
| Final TRX | `D3AD849A43E9DDBE2168A86D1BF4B05C6B3025D2DAC3DE712D484E4BEA2EDE1B` |
| Cobertura attachment | `BA258887CFDA00F74BA6488F08145FAF154CAF0068152F0D52F3E963C7C8B405` |
| Selected Debug FakeWorker EXE | `4D470530783C7AEE54ECCAB9F429EA97D52CF43516413322D8A6EA31A7C591AC` |
| Selected Debug FakeWorker DLL | `6057DE3C54A2B7D7A5C96436C43E36F1ED93FDA256CC0217230039AD02F7CF6C` |
| Tracked Base compile reference | `6CE8101D2835CF4E958E39715F14A69243D149F2AD377EACCBA56BB3B183CC54` |
| Tracked Step7 compile reference | `002C4AFC8EC7AC888DB7C4A8B25DFF4621C8775D37B1FA537C277FC7BB169AD6` |
| Installed V21 Base compile reference | `8C12B0FA70C298F1CD1221105880752AB204ED63C9F6D9DCB68833D977BCB5D0` |
| Installed V21 Step7 compile reference | `DFBBE3863005FA2FBC8CB553A3E7E4612ACBACA19124939EB8D29DE24C814EBA` |

Canonical generated source hash:
`4706bad1743defeb401065622a19eeb3b0402c3cba1654c0370aedb8ab8e3df2`.
TRX is `TestResults/pr3-20261006-review-fix/pr3-review-fix.trx`; canonical coverage attachment is beneath
`12bc38f3-ade7-4194-9010-442c755db8e0`. VSTest also copies the identical coverage file under its
machine result folder; analysis uses the canonical attachment above. Package-contained host,
contracts and worker hashes exactly match the pack-built binaries. NuGet and host informational
versions both equal `3.0.1-local.319.g4ca82c9`; no installation/publication occurred.

## Review and limits

Independent Task 4 documentation review was Approved with no Critical/Important findings.
The final whole-candidate review identified the discovery-identity and historical-footer findings;
one fix wave resolved both, and scoped re-review was Approved with no remaining findings.
The requested minor clarification that `sessionId` is an integer with no positivity restriction
was committed separately as `fba3cc7`; scoped re-review found it addressed without breakage.
Local link/anchor/JSON validation checks repository destinations and README absolute links;
external URLs were not fetched. Documentation-only report/status edits reuse completed source
qualification and do not trigger another expensive suite.

The drift verifier retains pre-existing `Tags.cs:112` CS0108 (`PlcUserConstant.Name` hiding
`PlcConstant.Name`); this PR does not change that source. The generated-reference build reports
one warning/zero errors; both solution builds and the installed probe have zero warnings/errors.
Non-escalated Git reads warned about the inaccessible user ignore file; authorized local writes
and qualification succeeded. No credential/dependency change or environment failure was treated
as passing evidence. The original checkout and its unrelated work were not edited.

## Separate partial live verification

The delivered surface is default-compatible `bind_project` plus `list_portals` and five exact
Project Server inventories. Discovery remains 6/16/16. The [maintained reference](../../../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md)
describes selectors, persistent attachment, binding/cursor continuity, typed results and limits.
Source-linked Siemens doubles and FakeWorker evidence are offline only. No TIA Portal,
ProjectServer or PLC invocation/attachment, fixture setup/cleanup, installed-tool replacement,
credential/configuration change or remote write was performed during this offline qualification.

The user subsequently authorized installed-tool verification against three disposable sessions
under `C:\Users\LCZ\Documents\Automation\Sessions`. The
[October 6 live report](2026-10-06-multiuser-pr3-live-verification.md) records successful reads for
all six inspection actions with opened sessions, then a separately authorized standalone `.ap21`
phase. All five inventories succeeded with `.ap21` open and with zero open projects; healthy
verified binding/cursors and genuine worker-loss invalidation/recovery were observed. The exact
prerequisite comparison is complete; remaining Task 5 matrix cases still prevent a merge-ready claim. No source changes
or expensive offline rerun accompanied that documentation update.
Historical PR 2 standalone acceptance does not qualify PR 3 inventories. `.als21` adoption,
session content/state/markings and mutations, and Issue #65 completion remain undelivered.
