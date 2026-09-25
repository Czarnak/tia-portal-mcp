#Requires -Version 7
<#
.SYNOPSIS
Separately authorized, worker-only fixture qualification. Default is read-only.
.DESCRIPTION
The ignored JSON manifest must be refreshed on a clean frozen candidate. Required fields:
commit, tree, projectPath, portalProcessId, hostSha256, workerSha256, binaryHashes
(repository-relative runtime file paths mapped to SHA256), fixtures (alias, ioSystemSelector).
Inventory captures public inspection, raw metadata and unique owner proof. Preview additionally
requires an ignored ProposalPath JSON object containing exactly attributeName, expectedValue,
desiredValue (closed worker scalar objects). No arrays of edits are accepted.
Compile requires a matching Inventory or Preview evidence file; Apply requires Preview evidence.
Both require its ExpectedPreviewSha256, the explicit effectful switch and exact phrase.
The manifest and all evidence/proposals must reside under the ignored evidence directory.
An unmodified initial baseline uses no prior evidence. A modified baseline requires the exact
ignored PriorEvidencePath and ExpectedPriorEvidenceSha256 of a successful completed Compile or
Apply from this frozen candidate and Portal process. The prior target, five complete public
observations, linked PN names and owner are re-read before continuing. Project status has no
whole-project mutation revision. An unrelated change that leaves these observations and the
status identical cannot be detected.
Preview/Apply compare durable identity across launches; each request uses its own freshly read
worker session identity. A reopened project with indistinguishable durable state is not detected;
fresh exact-target authorization and reinspection remain necessary. Restoration is a separate
new Preview and separately authorized Apply using the new selector and current expected value.
This script never builds binaries, retries, or performs automatic restoration.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Compile', 'Apply')]
    [string] $Mode = 'Inventory',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [ValidateSet('PN-A', 'DP-A', 'PN-B', 'DP-B')] [string] $FixtureAlias,
    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [Parameter(Mandatory)] [string] $ExpectedTree,
    [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,
    [Parameter(Mandatory)] [string] $ExpectedManifestSha256,
    [string] $ProposalPath,
    [string] $PreviewPath,
    [string] $ExpectedPreviewSha256,
    [string] $PriorEvidencePath,
    [string] $ExpectedPriorEvidenceSha256,
    [switch] $AllowEffectfulQualification,
    [string] $ConfirmationPhrase,
    [ValidateRange(1, 600)] [int] $TimeoutSeconds = 120
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Root = Split-Path -Parent $PSScriptRoot
$script:EvidenceRoot = Join-Path $script:Root 'artifacts/live-network-phase5-pr2'
$script:HarnessPath = $PSCommandPath
$script:Calls = [Collections.Generic.List[object]]::new()
$script:Children = [Collections.Generic.List[object]]::new()
$script:RequestId = 0

if (-not [IO.Path]::IsPathFullyQualified($ProjectPath) -or
    -not $ProjectPath.EndsWith('.ap21', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFullPath($ProjectPath) -cne $ProjectPath) {
    throw 'An explicit canonical absolute .ap21 path is required.'
}
if ($Mode -cnotin @('Inventory', 'Preview', 'Compile', 'Apply')) {
    throw 'Mode must use an exact closed value.'
}
$fixtureLabel = switch -CaseSensitive ($FixtureAlias) {
    'PN-A' { 'A' }
    'DP-A' { 'A' }
    'PN-B' { 'B' }
    'DP-B' { 'B' }
    default { throw 'Fixture alias must use an exact closed value.' }
}
if ($Mode -in @('Compile', 'Apply') -and
    (-not $AllowEffectfulQualification -or $ConfirmationPhrase -cne "QUALIFY FIXTURE $fixtureLabel $Mode")) {
    throw 'Effectful qualification requires its exact confirmation phrase.'
}

function Get-Sha([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-Json($Value) { ConvertTo-Json -InputObject $Value -Depth 100 -Compress }
function Assert-Same($Expected, $Actual) {
    # Compare structures, including JSON types; property insertion order is immaterial.
    $left = [System.Text.Json.Nodes.JsonNode]::Parse((Get-Json $Expected))
    $right = [System.Text.Json.Nodes.JsonNode]::Parse((Get-Json $Actual))
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($left, $right)) { throw 'Evidence or request drift.' }
}
function Assert-Keys($Value, [string[]] $Keys) {
    if ($Value -isnot [Collections.IDictionary]) { throw 'Expected one closed object.' }
    Assert-Same @($Keys | Sort-Object -CaseSensitive) @($Value.Keys | Sort-Object -CaseSensitive)
}
function Assert-PrivatePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Missing private artifact path.' }
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($script:EvidenceRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact must be inside the private evidence directory.' }
    $cursor = $full
    while (-not (Test-Path -LiteralPath $cursor)) { $cursor = Split-Path -Parent $cursor }
    $item = Get-Item -LiteralPath $cursor
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point artifact paths are refused.' }
    if ($item -is [IO.FileInfo]) { $item = $item.Directory }
    for (; $null -ne $item; $item = $item.Parent) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point artifact paths are refused.' }
        if ($item.FullName -eq $script:Root) { break }
    }
    & git -C $script:Root check-ignore --quiet -- $full
    if ($LASTEXITCODE -ne 0) { throw 'Artifact is not Git ignored.' }
    return $full
}
function Assert-Candidate {
    if ((& git -C $script:Root rev-parse HEAD) -cne $ExpectedCommit -or $LASTEXITCODE -ne 0) { throw 'Candidate commit mismatch.' }
    if ((& git -C $script:Root rev-parse 'HEAD^{tree}') -cne $ExpectedTree -or $LASTEXITCODE -ne 0) { throw 'Candidate tree mismatch.' }
    $changes = @(& git -C $script:Root status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -ne 0) { throw 'Frozen candidate must be clean.' }
    if ((Get-Sha $script:HarnessPath) -cne $ExpectedHarnessSha256.ToLowerInvariant()) { throw 'Harness SHA mismatch.' }
    if ((Get-Sha $ManifestPath) -cne $ExpectedManifestSha256.ToLowerInvariant()) { throw 'Manifest SHA mismatch.' }
    if ($manifest.commit -cne $ExpectedCommit -or $manifest.tree -cne $ExpectedTree) { throw 'Manifest candidate mismatch.' }
    if ($manifest.projectPath -cne $ProjectPath) { throw 'Manifest project mismatch.' }
    if ((Get-Sha $hostDll) -cne $manifest.hostSha256 -or (Get-Sha $workerExe) -cne $manifest.workerSha256) { throw 'Executable SHA mismatch.' }
    if ($manifest.binaryHashes -isnot [Collections.IDictionary] -or $manifest.binaryHashes.Count -lt 4) { throw 'Runtime provenance is incomplete.' }
    foreach ($entry in $manifest.binaryHashes.GetEnumerator()) {
        $path = [IO.Path]::GetFullPath((Join-Path $script:Root $entry.Key))
        if (-not $path.StartsWith($runtimeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime path outside frozen output.' }
        if ((Get-Sha $path) -cne $entry.Value) { throw 'Runtime file SHA mismatch.' }
    }
    foreach ($file in Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File) {
        if ($file.Extension -in @('.dll', '.exe', '.json', '.config')) {
            $relative = [IO.Path]::GetRelativePath($script:Root, $file.FullName).Replace('\', '/')
            if (-not $manifest.binaryHashes.Contains($relative)) { throw 'Runtime provenance omits a loadable file.' }
        }
    }
    return $true
}
function Start-Child([string] $Executable, [string[]] $Arguments, [string] $Directory) {
    $assertedCandidate = Assert-Candidate
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Executable
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void] $psi.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($psi)
    $child = @{ process = $process; stderr = $process.StandardError.ReadToEndAsync() }
    $script:Children.Add($child)
    return $process
}
function Send-Line($Process, $Request, [switch] $Notification) {
    $call = @{ atUtc = [DateTime]::UtcNow.ToString('o'); request = $Request; response = $null; transportCompleted = $false }
    $script:Calls.Add($call)
    $Process.StandardInput.WriteLine((Get-Json $Request))
    $Process.StandardInput.Flush()
    if ($Notification) { return }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $task = $Process.StandardOutput.ReadLineAsync()
        $remaining = [int] ($deadline - [DateTime]::UtcNow).TotalMilliseconds
        if ($remaining -le 0 -or -not $task.Wait($remaining) -or $null -eq $task.Result) { throw 'Transport ended or timed out. Stop. No automatic retry; outcome may be ambiguous.' }
        $response = ConvertFrom-Json -InputObject $task.Result -AsHashtable -Depth 100
    } while ($Request.Contains('jsonrpc') -and (-not $response.Contains('id') -or $response.id -ne $Request.id))
    $call.response = $response
    $call.transportCompleted = $true
    return $response
}
function Invoke-Rpc([string] $Method, $Parameters) {
    $script:RequestId++
    Send-Line $hostProcess @{ jsonrpc = '2.0'; id = $script:RequestId; method = $Method; params = $Parameters }
}
function Invoke-Public([string] $Name, $Arguments) {
    if ($Name -cnotin @('get_project_status', 'network_read')) { throw 'Public tool outside read-only allowlist.' }
    $rpc = Invoke-Rpc 'tools/call' @{ name = $Name; arguments = $Arguments }
    if ($rpc['error'] -or $rpc.result['isError']) { throw 'Public read failed.' }
    $doc = $rpc.result['structuredContent']
    if ($null -eq $doc) { $doc = ConvertFrom-Json $rpc.result.content[0].text -AsHashtable -Depth 100 }
    if ($doc.success -ne $true) { throw 'Public read unsuccessful.' }
    return $doc
}
function Get-PublicStatus {
    $result = Invoke-Public 'get_project_status' @{}
    $status = ConvertFrom-Json $result.payload -AsHashtable -Depth 100
    if ($status.projectPath -cne $ProjectPath) { throw 'Unexpected public project.' }
    return $status
}
function Get-PublicInspection($ReadTarget) {
    if ($null -eq $ReadTarget) { $ReadTarget = $target }
    $inspection = Invoke-Public 'network_read' @{ operations = @(@{ operation = 'inspect_network_object'; operationId = 'fixture'; target = $ReadTarget; attributeNames = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment') }) }
    $batch = $inspection.batch
    if ($batch.operations.Count -ne 1 -or $batch.operations[0].status -cne 'succeeded' -or
        $batch.operations[0]['omission'] -or ($batch['truncation'] -and $batch.truncation['truncated'])) { throw 'Incomplete public inspection.' }
    $result = $batch.operations[0].result
    Assert-SameIoSelector $ReadTarget $result.target
    return $result
}
function Get-Baseline($inspection) {
    $snapshot = @()
    foreach ($name in @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment')) {
        $matches = @($inspection.attributes | Where-Object name -CEQ $name)
        if ($matches.Count -ne 1) { throw 'Five-attribute inspection incomplete.' }
        $attribute = $matches[0]
        $value = $null
        if ($attribute.availability -ceq 'available') {
            $kind = $attribute.value.kind
            if ($kind -cnotin @('string', 'integer', 'boolean')) { throw 'Unsupported observed scalar.' }
            $value = @{ kind = $kind }
            $value[$kind + 'Value'] = $attribute.value.value
        }
        # Keep the complete public observation for cross-invocation drift checks. The
        # convenience booleans/scalar below are only for one-field proposal validation.
        # In particular, unavailable states and the observed CLR type must not collapse.
        $snapshot += @{
            name = $name
            available = ($attribute.availability -ceq 'available')
            writable = ($attribute.access -ceq 'readWrite')
            supportedTypes = $attribute.supportedTypes
            value = $value
            observation = $attribute
        }
    }
    return ,$snapshot
}
function Invoke-Probe($Request) {
    if ($Request.method -cnotin @('get_project_status', 'probe_network_object_attributes', 'probe_io_system_qualification')) { throw 'Worker method outside allowlist.' }
    $Request.protocolVersion = 'project-tree-v3'
    $Request.projectPath = $ProjectPath
    $result = Send-Line $workerProcess $Request
    if ($result.success -ne $true) { throw 'Worker call failed. Stop. No automatic retry; inspect private evidence.' }
    return $result
}
function Get-WorkerStatus {
    $response = Invoke-Probe @{ method = 'get_project_status' }
    $status = ConvertFrom-Json $response.payload -AsHashtable -Depth 100
    if ($status.projectPath -cne $ProjectPath -or $response.sessionIdentity.projectPath -cne $ProjectPath -or
        $response.sessionIdentity.portalProcessId -ne $manifest.portalProcessId -or $manifest.portalProcessId -le 0 -or
        $null -eq $response.sessionIdentity.sessionGeneration -or
        [string]::IsNullOrWhiteSpace($response.sessionIdentity.workerSessionId)) { throw 'Unexpected project/session identity.' }
    return @{ status = $status; identity = $response.sessionIdentity }
}
function Assert-Owner($owner) {
    if ($owner -isnot [Collections.IDictionary] -or
        ($owner.ownerMatchCount -isnot [int] -and $owner.ownerMatchCount -isnot [long]) -or
        $owner.ownerMatchCount -ne 1 -or
        $owner.ownerIdentityVerified -isnot [bool] -or $owner.ownerIdentityVerified -ne $true -or
        $owner.ownerTarget.kind -cne 'deviceItem' -or
        $owner.evidenceOmitted -isnot [bool] -or $owner.evidenceOmitted -ne $false -or
        [string]::IsNullOrWhiteSpace($owner.ownerTarget.deviceName) -or
        $owner.ownerTarget.itemPath -isnot [array] -or $owner.ownerTarget.itemPath.Count -eq 0) { throw 'Exact unique compiler owner is unproven.' }
}
function Assert-Proposal($proposal, $owner) {
    Assert-Keys $proposal @('attributeName', 'expectedValue', 'desiredValue')
    $allowed = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension')
    if ($FixtureAlias -cin @('DP-A', 'DP-B')) { $allowed = @('Name', 'Number') }
    if ($proposal.attributeName -cnotin $allowed) { throw 'Unsupported or unqualified field.' }
    $kind = switch -CaseSensitive ($proposal.attributeName) { 'Name' { 'string' }; 'Number' { 'integer' }; default { 'boolean' } }
    $member = $kind + 'Value'
    foreach ($scalar in @($proposal.expectedValue, $proposal.desiredValue)) {
        Assert-Keys $scalar @('kind', $member)
        if ($scalar.kind -cne $kind -or $null -eq $scalar[$member]) { throw 'Invalid scalar shape.' }
        if (($kind -eq 'string' -and $scalar[$member] -isnot [string]) -or
            ($kind -eq 'boolean' -and $scalar[$member] -isnot [bool]) -or
            ($kind -eq 'integer' -and ($scalar[$member] -isnot [long] -and $scalar[$member] -isnot [int])) -or
            ($kind -eq 'integer' -and ($scalar[$member] -lt 0 -or $scalar[$member] -gt [int]::MaxValue))) { throw 'Invalid scalar type or range.' }
    }
    if ((Get-Json $proposal.expectedValue) -ceq (Get-Json $proposal.desiredValue)) { throw 'Expected and desired values must differ.' }
    $matches = @($owner.before | Where-Object name -CEQ $proposal.attributeName)
    if ($matches.Count -ne 1) { throw 'Current field is not unique.' }
    $current = $matches[0]
    if ($current.available -ne $true -or $current.writable -ne $true) { throw 'Field is unavailable or not writable.' }
    Assert-Same $proposal.expectedValue $current.value
}
function Assert-FiveQualificationAttributes($Attributes) {
    if ($Attributes -isnot [array] -or $Attributes.Count -ne 5) { throw 'Incomplete qualification attribute evidence.' }
    $expected = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment')
    $actual = @($Attributes | ForEach-Object {
        if ($_ -isnot [Collections.IDictionary] -or $_.name -isnot [string]) { throw 'Invalid qualification attribute evidence.' }
        $_.name
    })
    Assert-Same @($expected | Sort-Object -CaseSensitive) @($actual | Sort-Object -CaseSensitive)
}
function Assert-IoSelector($Selector, [switch] $AllowObservedFields) {
    if ($Selector -isnot [Collections.IDictionary] -or $Selector.kind -cne 'ioSystem' -or
        $Selector.subnetId -isnot [string] -or [string]::IsNullOrWhiteSpace($Selector.subnetId) -or
        ($Selector.number -isnot [int] -and $Selector.number -isnot [long]) -or
        $Selector.number -lt 0 -or $Selector.number -gt [int]::MaxValue) { throw 'Invalid exact IO-system selector.' }
    foreach ($key in $Selector.Keys) {
        if ($key -cin @('kind', 'subnetId', 'number') -or $null -eq $Selector[$key]) { continue }
        if ($AllowObservedFields -and $key -ceq 'ioSystemIndex' -and
            ($Selector[$key] -is [int] -or $Selector[$key] -is [long]) -and $Selector[$key] -ge 0) { continue }
        if ($AllowObservedFields -and $key -ceq 'ioSystemName' -and $Selector[$key] -is [string]) { continue }
        throw 'Unexpected selector field.'
    }
}
function Assert-SameIoSelector($Expected, $Actual) {
    Assert-IoSelector $Expected -AllowObservedFields
    Assert-IoSelector $Actual -AllowObservedFields
    Assert-Same @{ kind = $Expected.kind; subnetId = $Expected.subnetId; number = $Expected.number } `
        @{ kind = $Actual.kind; subnetId = $Actual.subnetId; number = $Actual.number }
}
function Get-ComparableScalar($Scalar) {
    if ($null -eq $Scalar) { return $null }
    if ($Scalar -isnot [Collections.IDictionary] -or $Scalar.kind -cnotin @('string', 'integer', 'boolean')) {
        throw 'Five-attribute scalar is untyped.'
    }
    $member = $Scalar.kind + 'Value'
    if (-not $Scalar.Contains($member)) { throw 'Five-attribute scalar is incomplete.' }
    $value = $Scalar[$member]
    if (($Scalar.kind -ceq 'string' -and $value -isnot [string]) -or
        ($Scalar.kind -ceq 'integer' -and $value -isnot [int] -and $value -isnot [long]) -or
        ($Scalar.kind -ceq 'boolean' -and $value -isnot [bool])) { throw 'Five-attribute scalar has the wrong type.' }
    foreach ($key in $Scalar.Keys) {
        if ($key -cnotin @('kind', $member) -and $null -ne $Scalar[$key]) { throw 'Unexpected scalar value.' }
    }
    return @{ kind = $Scalar.kind; value = $value }
}
function Assert-SnapshotMatchesBaseline($Expected, $Actual) {
    Assert-FiveQualificationAttributes $Expected
    Assert-FiveQualificationAttributes $Actual
    foreach ($name in @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment')) {
        $old = @($Expected | Where-Object name -CEQ $name)[0]
        $now = @($Actual | Where-Object name -CEQ $name)[0]
        foreach ($attribute in @($old, $now)) {
            if ($attribute.available -isnot [bool] -or $attribute.writable -isnot [bool] -or
                $attribute.supportedTypes -isnot [array] -or
                @($attribute.supportedTypes | Where-Object { $_ -isnot [string] }).Count -ne 0) {
                throw 'Five-attribute snapshot is untyped.'
            }
        }
        Assert-Same @{ name = $old.name; available = $old.available; writable = $old.writable;
            supportedTypes = @($old.supportedTypes | Sort-Object -CaseSensitive); value = (Get-ComparableScalar $old.value) } `
            @{ name = $now.name; available = $now.available; writable = $now.writable;
            supportedTypes = @($now.supportedTypes | Sort-Object -CaseSensitive); value = (Get-ComparableScalar $now.value) }
    }
}
function Assert-PriorEvidence($Prior) {
    if ($Prior -isnot [Collections.IDictionary] -or $Prior.success -isnot [bool] -or -not $Prior.success -or
        $Prior.mode -cnotin @('Compile', 'Apply') -or
        $Prior.fixtureAlias -cnotin @('PN-A', 'DP-A', 'PN-B', 'DP-B') -or
        $Prior.binding -isnot [Collections.IDictionary] -or
        $Prior.binding.fixtureAlias -cne $Prior.fixtureAlias -or
        $Prior.binding.commit -cne $ExpectedCommit -or $Prior.binding.tree -cne $ExpectedTree -or
        $Prior.binding.harnessSha256 -cne $ExpectedHarnessSha256 -or
        $Prior.binding.manifestSha256 -cne $ExpectedManifestSha256 -or
        ($Prior.binding.priorEvidenceSha256 -and $Prior.binding.priorEvidenceSha256 -cnotmatch '^[0-9a-f]{64}$') -or
        $Prior.durableIdentity -isnot [Collections.IDictionary] -or
        ($manifest.portalProcessId -isnot [int] -and $manifest.portalProcessId -isnot [long]) -or
        $manifest.portalProcessId -le 0 -or
        ($Prior.durableIdentity.portalProcessId -isnot [int] -and $Prior.durableIdentity.portalProcessId -isnot [long]) -or
        $Prior.durableIdentity.portalProcessId -ne $manifest.portalProcessId -or
        $Prior.durableIdentity.projectPath -isnot [string] -or $Prior.durableIdentity.projectPath -cne $ProjectPath -or
        $Prior.after -isnot [Collections.IDictionary] -or
        $Prior.after.identity -isnot [Collections.IDictionary] -or
        ($Prior.after.identity.portalProcessId -isnot [int] -and $Prior.after.identity.portalProcessId -isnot [long]) -or
        $Prior.after.identity.portalProcessId -ne $manifest.portalProcessId -or
        $Prior.after.identity.projectPath -isnot [string] -or $Prior.after.identity.projectPath -cne $ProjectPath -or
        $Prior.after.status -isnot [Collections.IDictionary] -or
        $Prior.after.status.projectPath -isnot [string] -or $Prior.after.status.projectPath -cne $ProjectPath -or
        $Prior.after.status.project -isnot [Collections.IDictionary] -or
        $Prior.after.status.project.isOpen -isnot [bool] -or -not $Prior.after.status.project.isOpen -or
        $Prior.after.status.project.path -isnot [string] -or $Prior.after.status.project.path -cne $ProjectPath -or
        $Prior.after.status.project.isModified -isnot [bool] -or
        $Prior.baseline -isnot [array] -or
        $Prior.afterPublicBaseline -isnot [array] -or
        $Prior.beforePnDeviceNames -isnot [array] -or $Prior.afterPnDeviceNames -isnot [array] -or
        $Prior.ownerTarget -isnot [Collections.IDictionary] -or
        $Prior.effect -isnot [Collections.IDictionary]) { throw 'Prior qualification evidence is incomplete or drifted.' }
    $fixtures = @($manifest.fixtures | Where-Object alias -CEQ $Prior.fixtureAlias)
    if ($fixtures.Count -ne 1) { throw 'Prior fixture is not in the current manifest.' }
    Assert-IoSelector $fixtures[0].ioSystemSelector
    if ($null -eq $Prior.binding.priorEvidenceSha256) {
        Assert-SameIoSelector $fixtures[0].ioSystemSelector $Prior.binding.target
    }
    Assert-SameIoSelector $Prior.binding.target $Prior.effect.originalTarget
    if ($Prior.mode -ceq 'Apply') {
        Assert-Keys $Prior.proposal @('attributeName', 'expectedValue', 'desiredValue')
        $allowed = if ($Prior.fixtureAlias -cin @('DP-A', 'DP-B')) { @('Name', 'Number') }
            else { @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension') }
        if ($Prior.proposal.attributeName -cnotin $allowed) { throw 'Prior proposal used an unqualified field.' }
    } elseif ($null -ne $Prior.proposal) { throw 'Compile evidence contained a proposal.' }
    $priorOwner = @{ originalTarget = $Prior.effect.originalTarget; ownerTarget = $Prior.ownerTarget }
    Assert-EffectEvidence $Prior.effect $priorOwner $Prior.proposal $Prior.mode $Prior.fixtureAlias
    Assert-SnapshotMatchesBaseline $Prior.effect.before $Prior.baseline
    $priorExpectedSnapshot = if ($Prior.mode -ceq 'Apply') { $Prior.effect.after } else { $Prior.effect.before }
    Assert-SnapshotMatchesBaseline $priorExpectedSnapshot $Prior.afterPublicBaseline
    $priorBeforePn = @{ pnDeviceNameEvidenceScope = $Prior.pnDeviceNameEvidenceScope;
        beforePnDeviceNames = $Prior.beforePnDeviceNames; afterPnDeviceNames = @() }
    $priorAfterPn = @{ pnDeviceNameEvidenceScope = $Prior.pnDeviceNameEvidenceScope;
        beforePnDeviceNames = $Prior.afterPnDeviceNames; afterPnDeviceNames = @() }
    Assert-ReadOnlyPnSnapshot $priorBeforePn $Prior.fixtureAlias
    Assert-ReadOnlyPnSnapshot $priorAfterPn $Prior.fixtureAlias
    if ($Prior.mode -ceq 'Apply') {
        Assert-Same $Prior.beforePnDeviceNames $Prior.effect.beforePnDeviceNames
        Assert-Same $Prior.afterPnDeviceNames $Prior.effect.afterPnDeviceNames
    } else { Assert-Same $Prior.beforePnDeviceNames $Prior.afterPnDeviceNames }
    if ($Prior.effect.compileState -cnotin @('Success', 'Warning') -or $Prior.effect.errorCount -ne 0) {
        throw 'Prior effect was not a successful completed compile.'
    }
}
function Resolve-ContinuationTarget($ManifestTarget, $Prior, [string] $Alias) {
    Assert-IoSelector $ManifestTarget
    if ($null -eq $Prior) { return $ManifestTarget }
    $selector = if ($Prior.mode -ceq 'Apply') { $Prior.effect.appliedTarget } else { $Prior.effect.originalTarget }
    Assert-IoSelector $selector -AllowObservedFields
    if ($Prior.fixtureAlias -cne $Alias) {
        $priorFixtures = @($manifest.fixtures | Where-Object alias -CEQ $Prior.fixtureAlias)
        if ($priorFixtures.Count -ne 1) { throw 'Prior fixture is not unique in the manifest.' }
        Assert-IoSelector $priorFixtures[0].ioSystemSelector
        if ($selector.subnetId -cne $priorFixtures[0].ioSystemSelector.subnetId -or
            $selector.number -ne $priorFixtures[0].ioSystemSelector.number) {
            throw 'Restore the prior fixture Number before switching fixture aliases.'
        }
        return $ManifestTarget
    }
    return @{ kind = 'ioSystem'; subnetId = $selector.subnetId; number = $selector.number }
}
function Assert-ContinuationBaseline($Status, $Prior) {
    if ($Status -isnot [Collections.IDictionary] -or $Status.projectPath -cne $ProjectPath -or
        $Status.project -isnot [Collections.IDictionary] -or
        $Status.project.isOpen -isnot [bool] -or -not $Status.project.isOpen -or
        $Status.project.path -isnot [string] -or $Status.project.path -cne $ProjectPath -or
        $Status.project.isModified -isnot [bool] -or
        ($Status.project.isModified -and $null -eq $Prior) -or
        (-not $Status.project.isModified -and $null -ne $Prior)) {
        throw 'Qualification requires an unmodified project baseline.'
    }
    if ($null -ne $Prior) { Assert-Same $Prior.after.status $Status }
}
function Assert-PnDeviceNameEvidence($Effect, [string] $Alias) {
    $scope = if ($Alias -cin @('PN-A', 'PN-B')) { 'profinet' }
        elseif ($Alias -cin @('DP-A', 'DP-B')) { 'notApplicable' }
        else { throw 'Unknown fixture for affected-node evidence.' }
    if ($Effect.pnDeviceNameEvidenceScope -isnot [string] -or $Effect.pnDeviceNameEvidenceScope -cne $scope -or
        $Effect.beforePnDeviceNames -isnot [array] -or $Effect.afterPnDeviceNames -isnot [array]) {
        throw 'Affected PN node evidence is incomplete.'
    }
    $before = $Effect.beforePnDeviceNames
    $after = $Effect.afterPnDeviceNames
    if ($scope -ceq 'notApplicable') {
        if ($before.Count -ne 0 -or $after.Count -ne 0) { throw 'DP effect reported affected PN nodes.' }
        return
    }
    if ($before.Count -lt 1 -or $before.Count -gt 128 -or $after.Count -ne $before.Count) {
        throw 'Affected PN node set is incomplete.'
    }
    $identities = @()
    foreach ($snapshot in @($before, $after)) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $keys = @()
        foreach ($node in $snapshot) {
            if ($node -isnot [Collections.IDictionary] -or
                $node.deviceLocator -isnot [string] -or [string]::IsNullOrWhiteSpace($node.deviceLocator) -or
                $node.deviceName -isnot [string] -or [string]::IsNullOrWhiteSpace($node.deviceName) -or
                $node.itemPath -isnot [array] -or $node.itemPath.Count -lt 1 -or $node.itemPath.Count -gt 16 -or
                $node.nodeId -isnot [string] -or [string]::IsNullOrWhiteSpace($node.nodeId) -or
                $node.associationKind -cnotin @('controller', 'connector') -or
                $node.available -isnot [bool] -or -not $node.available -or $node.value -isnot [string]) {
                throw 'Affected PN node observation is incomplete or untyped.'
            }
            $indices = @()
            foreach ($segment in $node.itemPath) {
                if ($segment -isnot [Collections.IDictionary] -or
                    ($segment.index -isnot [int] -and $segment.index -isnot [long]) -or $segment.index -lt 0 -or
                    $segment.name -isnot [string] -or [string]::IsNullOrWhiteSpace($segment.name) -or
                    ($segment.positionNumber -isnot [int] -and $segment.positionNumber -isnot [long]) -or
                    $segment.positionNumber -lt 0 -or
                    ($null -ne $segment.typeIdentifier -and $segment.typeIdentifier -isnot [string]) -or
                    ($segment.typeIdentifier -is [string] -and $segment.typeIdentifier.Length -gt 0 -and
                        [string]::IsNullOrWhiteSpace($segment.typeIdentifier))) { throw 'Affected PN node path is incomplete.' }
                $indices += $segment.index
            }
            $key = Get-Json ([ordered]@{ deviceLocator = $node.deviceLocator; itemPathIndices = $indices;
                nodeId = $node.nodeId; associationKind = $node.associationKind })
            if (-not $seen.Add($key)) { throw 'Affected PN node identity is duplicated.' }
            $keys += $key
        }
        $identities += ,@($keys | Sort-Object -CaseSensitive)
    }
    Assert-Same $identities[0] $identities[1]
}
function Assert-ReadOnlyPnSnapshot($Owner, [string] $Alias) {
    if ($Owner -isnot [Collections.IDictionary] -or $Owner.afterPnDeviceNames -isnot [array] -or
        $Owner.afterPnDeviceNames.Count -ne 0) { throw 'Read-only owner reported an applied PN snapshot.' }
    Assert-PnDeviceNameEvidence @{
        pnDeviceNameEvidenceScope = $Owner.pnDeviceNameEvidenceScope
        beforePnDeviceNames = $Owner.beforePnDeviceNames
        afterPnDeviceNames = $Owner.beforePnDeviceNames
    } $Alias
}
function Assert-EffectEvidence($Effect, $Owner, $Proposal, [string] $Mode, [string] $Alias) {
    $expectedMode = if ($Mode -ceq 'Compile') { 'compileBaseline' } else { 'setAndCompile' }
    $expectedCommit = $Mode -ceq 'Apply'
    if ($Effect -isnot [Collections.IDictionary] -or $Effect.mode -cne $expectedMode -or
        $Effect.mutationCommitted -isnot [bool] -or $Effect.mutationCommitted -ne $expectedCommit -or
        ($Effect.ownerMatchCount -isnot [int] -and $Effect.ownerMatchCount -isnot [long]) -or
        $Effect.ownerMatchCount -ne 1 -or $Effect.ownerIdentityVerified -isnot [bool] -or
        $Effect.ownerIdentityVerified -ne $true -or $Effect.hardwareTargetKind -cne 'deviceItem' -or
        $Effect.evidenceOmitted -isnot [bool] -or $Effect.evidenceOmitted -or
        $Effect.compileState -isnot [string] -or
        ($Effect.errorCount -isnot [int] -and $Effect.errorCount -isnot [long]) -or $Effect.errorCount -lt 0 -or
        ($Effect.warningCount -isnot [int] -and $Effect.warningCount -isnot [long]) -or $Effect.warningCount -lt 0 -or
        $Owner.originalTarget -isnot [Collections.IDictionary] -or
        $Effect.originalTarget -isnot [Collections.IDictionary] -or
        $Effect.appliedTarget -isnot [Collections.IDictionary] -or
        $Effect.ownerTarget -isnot [Collections.IDictionary] -or
        $Effect.ownerTarget.kind -cne 'deviceItem' -or
        [string]::IsNullOrWhiteSpace($Effect.ownerTarget.deviceName) -or
        $Effect.ownerTarget.itemPath -isnot [array] -or $Effect.ownerTarget.itemPath.Count -eq 0) {
        throw 'Qualification effect evidence is incomplete or inconsistent.'
    }
    Assert-Same $Owner.originalTarget $Effect.originalTarget
    Assert-FiveQualificationAttributes $Effect.before
    if ($Mode -ceq 'Compile') {
        Assert-Same $Owner.ownerTarget $Effect.ownerTarget
        Assert-Same $Effect.originalTarget $Effect.appliedTarget
        if ($Effect.after -isnot [array] -or $Effect.after.Count -ne 0) { throw 'Baseline compile reported an applied state.' }
        return
    }
    Assert-FiveQualificationAttributes $Effect.after
    # The worker proves object identity for Apply under ExclusiveAccess. Device/path names can
    # legitimately change, so textual pre/post owner-selector equality is not a valid gate.
    Assert-PnDeviceNameEvidence $Effect $Alias
    $expectedNumber = if ($Proposal.attributeName -ceq 'Number') { $Proposal.desiredValue.integerValue } else { $Owner.originalTarget.number }
    if ($Effect.appliedTarget.kind -cne 'ioSystem' -or
        $Effect.appliedTarget.subnetId -cne $Owner.originalTarget.subnetId -or
        ($Effect.appliedTarget.number -isnot [int] -and $Effect.appliedTarget.number -isnot [long]) -or
        $Effect.appliedTarget.number -ne $expectedNumber) {
        throw 'Applied IO-system selector is unverified.'
    }
    $member = $Proposal.expectedValue.kind + 'Value'
    $old = @($Effect.before | Where-Object name -CEQ $Proposal.attributeName)[0]
    $new = @($Effect.after | Where-Object name -CEQ $Proposal.attributeName)[0]
    if ($old.available -isnot [bool] -or $old.available -ne $true -or
        $new.available -isnot [bool] -or $new.available -ne $true -or
        $old.value -isnot [Collections.IDictionary] -or $new.value -isnot [Collections.IDictionary] -or
        $old.value.kind -cne $Proposal.expectedValue.kind -or
        $new.value.kind -cne $Proposal.desiredValue.kind -or
        -not $old.value.Contains($member) -or -not $new.value.Contains($member)) {
        throw 'Committed attribute evidence is incomplete.'
    }
    Assert-Same $Proposal.expectedValue[$member] $old.value[$member]
    Assert-Same $Proposal.desiredValue[$member] $new.value[$member]
}

$hostProcess = $null
$workerProcess = $null
$record = @{ mode = $Mode; fixtureAlias = $FixtureAlias; startedAtUtc = [DateTime]::UtcNow.ToString('o'); calls = $script:Calls; success = $false }
$outputPath = $null
try {
    $ManifestPath = Assert-PrivatePath $ManifestPath
    $manifest = ConvertFrom-Json (Get-Content -LiteralPath $ManifestPath -Raw) -AsHashtable -Depth 100
    $runtimeRoot = [IO.Path]::GetFullPath((Join-Path $script:Root 'TiaMcpServer/bin/Debug/net10.0'))
    $hostDll = Join-Path $runtimeRoot 'TiaMcpServer.dll'
    $workerExe = Join-Path $runtimeRoot 'openness-worker/TiaMcpServer.OpennessWorker.exe'
    $assertedCandidate = Assert-Candidate
    $outputPath = Assert-PrivatePath (Join-Path $script:EvidenceRoot (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')) + '-' + [Guid]::NewGuid().ToString('N') + '.json'))
    $fixtures = @($manifest.fixtures | Where-Object alias -CEQ $FixtureAlias)
    if ($fixtures.Count -ne 1) { throw 'Fixture alias is not unique.' }
    $selector = $fixtures[0].ioSystemSelector
    # Public selectors may include explicit null fields. The worker accepts only exact keys.
    Assert-IoSelector $selector
    $manifestTarget = @{ kind = 'ioSystem'; subnetId = $selector.subnetId; number = $selector.number }
    $prior = $null
    $priorSha = $null
    if ($PriorEvidencePath -or $ExpectedPriorEvidenceSha256) {
        $PriorEvidencePath = Assert-PrivatePath $PriorEvidencePath
        if ($ExpectedPriorEvidenceSha256 -cnotmatch '^[0-9a-fA-F]{64}$') { throw 'Prior evidence SHA is missing or malformed.' }
        $priorSha = (Get-Sha $PriorEvidencePath)
        if ($priorSha -cne $ExpectedPriorEvidenceSha256.ToLowerInvariant()) { throw 'Prior evidence SHA mismatch.' }
        $prior = ConvertFrom-Json (Get-Content -LiteralPath $PriorEvidencePath -Raw) -AsHashtable -Depth 100
        Assert-PriorEvidence $prior
        $record.priorEvidencePath = $PriorEvidencePath
    }
    $target = Resolve-ContinuationTarget $manifestTarget $prior $FixtureAlias
    $binding = @{ commit = $ExpectedCommit; tree = $ExpectedTree; harnessSha256 = $ExpectedHarnessSha256; manifestSha256 = $ExpectedManifestSha256; target = $target; fixtureAlias = $FixtureAlias; priorEvidenceSha256 = $priorSha }
    $record.binding = $binding
    $proposal = $null
    if ($Mode -in @('Preview', 'Apply')) {
        $ProposalPath = Assert-PrivatePath $ProposalPath
        $proposal = ConvertFrom-Json (Get-Content -LiteralPath $ProposalPath -Raw) -AsHashtable -Depth 100
        Assert-Keys $proposal @('attributeName', 'expectedValue', 'desiredValue')
    } elseif ($ProposalPath) { throw 'This mode does not accept a proposal.' }
    $preview = $null
    if ($Mode -in @('Compile', 'Apply')) {
        $PreviewPath = Assert-PrivatePath $PreviewPath
        if ([string]::IsNullOrWhiteSpace($ExpectedPreviewSha256) -or (Get-Sha $PreviewPath) -cne $ExpectedPreviewSha256.ToLowerInvariant()) { throw 'Preview SHA mismatch.' }
        $preview = ConvertFrom-Json (Get-Content -LiteralPath $PreviewPath -Raw) -AsHashtable -Depth 100
        if ($preview.success -ne $true -or $preview.mode -cnotin @('Inventory', 'Preview') -or ($Mode -eq 'Apply' -and $preview.mode -cne 'Preview')) { throw 'Required read-only evidence missing.' }
        Assert-Same $preview.binding $binding
    }

    $hostProcess = Start-Child 'dotnet' @($hostDll, '--project', $ProjectPath) $script:Root
    $initialized = Invoke-Rpc 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'fixture-qualification'; version = '1' } }
    if ($initialized['error']) { throw 'MCP initialize failed.' }
    Send-Line $hostProcess @{ jsonrpc = '2.0'; method = 'notifications/initialized' } -Notification
    $publicBefore = Get-PublicStatus
    Assert-ContinuationBaseline $publicBefore $prior
    $priorTarget = $null
    $priorExpectedSnapshot = $null
    if ($null -ne $prior) {
        $priorTarget = Resolve-ContinuationTarget $prior.binding.target $prior $prior.fixtureAlias
        $priorExpectedSnapshot = if ($prior.mode -ceq 'Apply') { $prior.effect.after } else { $prior.effect.before }
        $priorInspection = Get-PublicInspection $priorTarget
        Assert-SnapshotMatchesBaseline $priorExpectedSnapshot (Get-Baseline $priorInspection)
        Assert-Same $prior.afterPublicBaseline (Get-Baseline $priorInspection)
    }
    $inspection = Get-PublicInspection
    if ($null -ne $prior -and $prior.fixtureAlias -ceq $FixtureAlias) {
        Assert-Same $prior.afterPublicBaseline (Get-Baseline $inspection)
    }
    $record.publicInspection = $inspection
    Assert-Same $publicBefore (Get-PublicStatus)
    $workerProcess = Start-Child $workerExe @() (Split-Path -Parent $workerExe)
    $before = Get-WorkerStatus
    $sessionIdentity = $before.identity
    Assert-Same $publicBefore $before.status
    if ($null -ne $prior) {
        if ($sessionIdentity.portalProcessId -ne $prior.durableIdentity.portalProcessId) { throw 'Prior Portal process changed.' }
        Assert-Same $prior.after.status $before.status
        $priorOwnerResponse = Invoke-Probe @{ method = 'probe_io_system_qualification'; confirm = $true; expectedSessionIdentity = $sessionIdentity; ioSystemQualification = @{ mode = 'inspectOwner'; target = $priorTarget } }
        Assert-Same $sessionIdentity $priorOwnerResponse.sessionIdentity
        $priorOwner = ConvertFrom-Json $priorOwnerResponse.payload -AsHashtable -Depth 100
        Assert-Owner $priorOwner
        Assert-ReadOnlyPnSnapshot $priorOwner $prior.fixtureAlias
        Assert-SameIoSelector $priorTarget $priorOwner.originalTarget
        Assert-Same $prior.effect.ownerTarget $priorOwner.ownerTarget
        Assert-Same $prior.afterPnDeviceNames $priorOwner.beforePnDeviceNames
        Assert-SnapshotMatchesBaseline $priorExpectedSnapshot (Get-Baseline (Get-PublicInspection $priorTarget))
        Assert-Same $publicBefore (Get-PublicStatus)
    }
    $durableIdentity = @{ portalProcessId = $sessionIdentity.portalProcessId; projectPath = $ProjectPath; project = $before.status.project }
    $record.durableIdentity = $durableIdentity
    $raw = Invoke-Probe @{ method = 'probe_network_object_attributes'; expectedSessionIdentity = $sessionIdentity; networkObjectTarget = $target; networkAttributeNames = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment') }
    Assert-Same $sessionIdentity $raw.sessionIdentity
    $ownerResponse = Invoke-Probe @{ method = 'probe_io_system_qualification'; confirm = $true; expectedSessionIdentity = $sessionIdentity; ioSystemQualification = @{ mode = 'inspectOwner'; target = $target } }
    $owner = ConvertFrom-Json $ownerResponse.payload -AsHashtable -Depth 100
    Assert-Owner $owner
    Assert-ReadOnlyPnSnapshot $owner $FixtureAlias
    Assert-Same $sessionIdentity $ownerResponse.sessionIdentity
    # inspectOwner returns owner proof only. Values come from the public typed inspection.
    $owner.before = Get-Baseline $inspection
    Assert-Same $inspection (Get-PublicInspection)
    $record.ownerTarget = $owner.ownerTarget
    $record.baseline = $owner.before
    $record.pnDeviceNameEvidenceScope = $owner.pnDeviceNameEvidenceScope
    $record.beforePnDeviceNames = $owner.beforePnDeviceNames
    $record.proposal = $proposal
    if ($null -ne $proposal) { Assert-Proposal $proposal $owner }
    if ($null -ne $preview) {
        Assert-Same $preview.durableIdentity $durableIdentity
        Assert-Same $preview.baseline $owner.before
        Assert-Same $preview.ownerTarget $owner.ownerTarget
        Assert-Same $preview.pnDeviceNameEvidenceScope $owner.pnDeviceNameEvidenceScope
        Assert-Same $preview.beforePnDeviceNames $owner.beforePnDeviceNames
        if ($Mode -eq 'Apply') { Assert-Same $preview.proposal $proposal }
    }
    $currentStatus = Get-WorkerStatus
    Assert-Same $before $currentStatus
    if ($Mode -in @('Compile', 'Apply')) {
        $assertedCandidate = Assert-Candidate
        if ((Get-Sha $PreviewPath) -cne $ExpectedPreviewSha256.ToLowerInvariant()) { throw 'Preview changed before effect.' }
        $request = @{ method = 'probe_io_system_qualification'; confirm = $true; expectedSessionIdentity = $sessionIdentity; ioSystemQualification = @{ mode = 'compileBaseline'; target = $target } }
        if ($Mode -eq 'Apply') {
            $request.ioSystemQualification = @{ mode = 'setAndCompile'; target = $target; attributeName = $proposal.attributeName; expectedValue = $proposal.expectedValue; desiredValue = $proposal.desiredValue }
        }
        $effect = Invoke-Probe $request
        $record.effect = ConvertFrom-Json $effect.payload -AsHashtable -Depth 100
        Assert-Same $sessionIdentity $effect.sessionIdentity
        $after = Get-WorkerStatus
        Assert-Same $sessionIdentity $after.identity
        $record.after = $after
        Assert-EffectEvidence $record.effect $owner $proposal $Mode $FixtureAlias
        Assert-SnapshotMatchesBaseline $record.effect.before $owner.before
        if ($Mode -ceq 'Apply') {
            Assert-Same $owner.pnDeviceNameEvidenceScope $record.effect.pnDeviceNameEvidenceScope
            Assert-Same $owner.beforePnDeviceNames $record.effect.beforePnDeviceNames
        }
        if ($record.effect.compileState -cnotin @('Success', 'Warning') -or $record.effect.errorCount -ne 0 -or $record.effect.evidenceOmitted) { throw 'Qualification did not establish successful complete compilation; stop and inspect evidence.' }
        $postEffectTarget = if ($Mode -ceq 'Apply') { $record.effect.appliedTarget } else { $record.effect.originalTarget }
        $record.afterPublicBaseline = Get-Baseline (Get-PublicInspection $postEffectTarget)
        $postEffectSnapshot = if ($Mode -ceq 'Apply') { $record.effect.after } else { $record.effect.before }
        Assert-SnapshotMatchesBaseline $postEffectSnapshot $record.afterPublicBaseline
        Assert-Same $after.status (Get-PublicStatus)
        $postOwnerResponse = Invoke-Probe @{ method = 'probe_io_system_qualification'; confirm = $true; expectedSessionIdentity = $sessionIdentity; ioSystemQualification = @{ mode = 'inspectOwner'; target = $postEffectTarget } }
        Assert-Same $sessionIdentity $postOwnerResponse.sessionIdentity
        $postOwner = ConvertFrom-Json $postOwnerResponse.payload -AsHashtable -Depth 100
        Assert-Owner $postOwner
        Assert-ReadOnlyPnSnapshot $postOwner $FixtureAlias
        Assert-SameIoSelector $postEffectTarget $postOwner.originalTarget
        if ($Mode -ceq 'Apply') {
            Assert-Same $record.effect.ownerTarget $postOwner.ownerTarget
            Assert-Same $record.effect.afterPnDeviceNames $postOwner.beforePnDeviceNames
        } else {
            Assert-Same $owner.ownerTarget $postOwner.ownerTarget
            Assert-Same $owner.beforePnDeviceNames $postOwner.beforePnDeviceNames
        }
        $record.afterPnDeviceNames = $postOwner.beforePnDeviceNames
        Assert-Same $record.afterPublicBaseline (Get-Baseline (Get-PublicInspection $postEffectTarget))
        Assert-Same $after.status (Get-PublicStatus)
    }
    if ($Mode -in @('Inventory', 'Preview')) {
        $after = Get-WorkerStatus
        Assert-Same $sessionIdentity $after.identity
        Assert-Same $before $after
        $record.after = $after
    }
    $assertedCandidate = Assert-Candidate
    $record.success = $true
} catch {
    # Error text may contain exact identifiers. Keep it only in ignored evidence.
    $record.error = $_.Exception.ToString()
    throw "Fixture $fixtureLabel qualification stopped. Inspect private evidence. No automatic retry."
} finally {
    foreach ($child in $script:Children) {
        try {
            $child.process.StandardInput.Close()
            if (-not $child.process.WaitForExit(3000)) { $child.process.Kill($true); $child.process.WaitForExit(3000) | Out-Null }
            if ($child.stderr.IsCompleted) { $record[('stderr-' + $child.process.Id)] = $child.stderr.Result }
            $child.process.Dispose()
        } catch { $record.cleanupFailed = $true }
    }
    $record.finishedAtUtc = [DateTime]::UtcNow.ToString('o')
    if ($null -ne $outputPath) {
        $null = Assert-PrivatePath $outputPath
        [IO.File]::WriteAllText($outputPath, (Get-Json $record), [Text.UTF8Encoding]::new($false))
        Write-Host ("Fixture {0} {1} {2}: success={3}; evidence SHA256={4}" -f $fixtureLabel, $FixtureAlias, $Mode, $record.success, (Get-Sha $outputPath))
    }
}
