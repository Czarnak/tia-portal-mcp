# Multiuser PR 3 offline qualification

**Status:** Offline qualification passed. Independent maintained-documentation review passed;
final whole-branch review is a separate controller gate. Task 5 live acceptance is pending exact
fixture/scope authorization. This report makes no merge-ready claim.

**Compiled code candidate:** `29468ba1af3ad57b0d8d7244b87bba747f2add12` on
`feature/multiuser-pr3`, with base `ea269c222e7415af347f2929cbc82a366aa2ee0f`. Documentation
commits `467590d` and `fba3cc7` do not change executable source. The local package was built at
`fba3cc7d3d2c4bea5b2452ffbf0d4b664d99007c`, after the maintained README update. Later
report/status-only commits retain this source evidence and package provenance.

## Serial offline gates

All gates completed with exit code 0. Logs and exit files are retained locally under
`.superpowers/sdd/2026-10-05-multiuser-pr3-read-only-inventory/qualification/`; no parallel builds
or tests ran. Restore was already valid; every command below used existing restored assets.

| Gate | Result | Local log |
| --- | --- | --- |
| Matching Release/stub solution build before subprocess tests | 0 warnings/errors; 26.88 s | `stub-build.log` |
| Full serial suite plus XPlat coverage | 5,536 passed, 0 failed/skipped; 13 m 26 s | `full-coverage.log` |
| Existing scoped 80% line threshold | 93.83%, 11,132/11,864 lines; passed unchanged scope/threshold | `coverage-threshold.log` |
| Generated source-reference drift verifier | Both tracked references current; existing Step7 warning noted below | `reference-drift.log` |
| Installed V21 compile-only probe | 0 warnings/errors; 0.54 s | `installed-probe.log` |
| Installed V21 Release solution build | 0 warnings/errors; 26.12 s | `installed-solution.log` |
| Local NuGet pack using installed references | Traceable local version; no installation/publication | `package-build.log` |
| Package layout/leak check | One canonical worker subtree, 15 required files, no worker runtimeconfig/Siemens DLL | `package-verify.log` |
| Additional archive check | 0 ref/stub/probe entries; one README exactly matches maintained source | `package-extra.log` |
| Documentation links/anchors/JSON and whitespace | 13 touched Markdown files, 288 local/repository links, 17 JSON examples; whitespace clean | `docs-final-check.log` (earlier checks: 287 links) |

Commands (each build uses `-m:1`; test parallelism is explicitly disabled):

```powershell
dotnet build TiaMcpServer.slnx -c Release -m:1 /p:UseTiaPortalReferenceStubs=true --no-restore
dotnet test TiaMcpServer.Tests -c Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/pr3-20261006-final --logger "trx;LogFileName=pr3-final.trx" -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath TestResults/pr3-20261006-final/6831ca9c-9974-416a-8f9b-22b99724c4ae/coverage.cobertura.xml -MinimumLineRate 0.80
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -NoRestore
dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet build TiaMcpServer.slnx -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release --no-restore -o artifacts/multiuser-pr3-offline /p:UseTiaPortalReferenceStubs=false "/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" /p:Version=3.0.1-local.316.gfba3cc7 /p:PackageVersion=3.0.1-local.316.gfba3cc7 /p:InformationalVersion=3.0.1-local.316.gfba3cc7 /p:IncludeSourceRevisionInInformationalVersion=false
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath artifacts/multiuser-pr3-offline/TiaMcpServer.3.0.1-local.316.gfba3cc7.nupkg
git diff --check
```

## Coverage and executable worker evidence

The unchanged coverage scope has branch rate 85.56% (5,809/6,789). Added/modified executable
host lines in `ea269c2..29468ba` intersected with Cobertura, unioned by file/line across measured
classes, give **199/206 lines (96.60%)** and **189/224 branches (84.375%)**. Deleted lines and
non-executable declarations are not measurable lines; this is a changed-line diagnostic, not
a new coverage gate or a broadened scope.

| Changed host file | Covered lines | Covered branches |
| --- | --- | --- |
| `ProjectBindingInspectionCatalog.cs` | 48/48 | 101/110 |
| `ProjectBindingInspectionPayloadContract.cs` | 61/62 | 60/76 |
| `ProjectBindingTools.cs` | 24/30 | 22/32 |
| `StandaloneToolResponses.cs` | 10/10 | 0/0 |
| `StructuredStandaloneResult.cs` | 2/2 | 6/6 |
| `OpennessWorkerClient.cs` | 54/54 | 0/0 |
| `WorkerCallResult.cs` | 0/0 | 0/0 |

Worker source and Siemens doubles are excluded by `coverage.runsettings`. The completed TRX
separately records 30 `MultiuserInventoryWorkerTests`, 27 `MultiuserPortalReadDispatchTests`
and 26 `ActiveProjectContextSessionTests`, all passed (83 executable source-linked cases).
These establish offline worker logic; they are not measured worker coverage or installed runtime
acceptance. The full suite also covers strict registered inputs, default/null binding shape,
host binding/cursor continuity, typed canonical payloads, secret sanitization and omission limits.

## Artifact provenance

The package version follows `git describe --tags --long` (`v3.0.1-316-gfba3cc7`) and the existing
local-version procedure. Package:
`artifacts/multiuser-pr3-offline/TiaMcpServer.3.0.1-local.316.gfba3cc7.nupkg`.

| Artifact | SHA256 |
| --- | --- |
| Local package | `DB9071CE8D07E22A462B9BE22507041E6F25E02D4B67AB65876F59509042A072` |
| Pack-built host DLL | `FD873C33830F171803BC4948786D90E2E244DFCF6CEEB5A53A5D65B96AB78CF9` |
| Pack-built embedded worker EXE | `DA131AC6CB7CEE3BFA2E0FFAA7163B43A5A4CE4AF82C648BDDAE872E77182625` |
| Pack-built contracts DLL | `BE6CA8E25378D0E991EC787775FAF42C57C530611C693C6322AA880F29C132DD` |
| Final TRX | `20B999C111373BE70C0D425561E24C7D8F0E6813ED92EBBD86435CED4FFC2FCA` |
| Cobertura attachment | `B038EA78BEAA331754C092D2E139D6B638B11F1A154A7D5C9DFF6F13FC8397DC` |
| Tracked Base compile reference | `6CE8101D2835CF4E958E39715F14A69243D149F2AD377EACCBA56BB3B183CC54` |
| Tracked Step7 compile reference | `002C4AFC8EC7AC888DB7C4A8B25DFF4621C8775D37B1FA537C277FC7BB169AD6` |
| Installed V21 Base compile reference | `8C12B0FA70C298F1CD1221105880752AB204ED63C9F6D9DCB68833D977BCB5D0` |
| Installed V21 Step7 compile reference | `DFBBE3863005FA2FBC8CB553A3E7E4612ACBACA19124939EB8D29DE24C814EBA` |

Canonical generated source hash:
`4706bad1743defeb401065622a19eeb3b0402c3cba1654c0370aedb8ab8e3df2`.
TRX is `TestResults/pr3-20261006-final/pr3-final.trx`; canonical coverage attachment is beneath
`6831ca9c-9974-416a-8f9b-22b99724c4ae`. VSTest also copies the identical coverage file under its
machine result folder. Initial artifact analysis received both paths and rejected the extra
positional argument; choosing the canonical attachment corrected analysis only, with no test rerun.

## Review and limits

Independent Task 4 documentation review was Approved with no Critical/Important findings.
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

## Live acceptance pending

The delivered surface is default-compatible `bind_project` plus `list_portals` and five exact
Project Server inventories. Discovery remains 6/16/16. The [maintained reference](../../../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md)
describes selectors, persistent attachment, binding/cursor continuity, typed results and limits.
Source-linked Siemens doubles and FakeWorker evidence are offline only. No TIA Portal,
ProjectServer or PLC invocation/attachment, fixture setup/cleanup, installed-tool replacement,
credential/configuration change or remote write is authorized or performed for qualification.

Task 5 must verify each action and prerequisite on the exact authorized frozen live fixture.
Historical PR 2 standalone acceptance does not qualify PR 3 inventories. `.als21` adoption,
session content/state/markings and mutations, and Issue #65 completion remain undelivered.
