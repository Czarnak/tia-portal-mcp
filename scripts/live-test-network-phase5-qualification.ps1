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
function Get-PublicInspection {
    $inspection = Invoke-Public 'network_read' @{ operations = @(@{ operation = 'inspect_network_object'; operationId = 'fixture'; target = $target; attributeNames = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment') }) }
    $batch = $inspection.batch
    if ($batch.operations.Count -ne 1 -or $batch.operations[0].status -cne 'succeeded' -or
        $batch.operations[0]['omission'] -or ($batch['truncation'] -and $batch.truncation['truncated'])) { throw 'Incomplete public inspection.' }
    return $batch.operations[0].result
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
    if ($owner.ownerMatchCount -ne 1 -or $owner.ownerIdentityVerified -ne $true -or
        $owner.ownerTarget.kind -cne 'deviceItem' -or $owner.evidenceOmitted -ne $false -or
        [string]::IsNullOrWhiteSpace($owner.ownerTarget.deviceName) -or @($owner.ownerTarget.itemPath).Count -eq 0) { throw 'Exact unique compiler owner is unproven.' }
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
function Assert-EffectEvidence($Effect, $Owner, $Proposal, [string] $Mode) {
    $expectedMode = if ($Mode -ceq 'Compile') { 'compileBaseline' } else { 'setAndCompile' }
    $expectedCommit = $Mode -ceq 'Apply'
    if ($Effect -isnot [Collections.IDictionary] -or $Effect.mode -cne $expectedMode -or
        $Effect.mutationCommitted -isnot [bool] -or $Effect.mutationCommitted -ne $expectedCommit -or
        ($Effect.ownerMatchCount -isnot [int] -and $Effect.ownerMatchCount -isnot [long]) -or
        $Effect.ownerMatchCount -ne 1 -or $Effect.ownerIdentityVerified -isnot [bool] -or
        $Effect.ownerIdentityVerified -ne $true -or $Effect.hardwareTargetKind -cne 'deviceItem' -or
        $Effect.evidenceOmitted -isnot [bool] -or $Effect.evidenceOmitted -or
        $Owner.originalTarget -isnot [Collections.IDictionary] -or
        $Effect.originalTarget -isnot [Collections.IDictionary] -or
        $Effect.appliedTarget -isnot [Collections.IDictionary] -or
        $Effect.ownerTarget -isnot [Collections.IDictionary] -or
        $Effect.ownerTarget.kind -cne 'deviceItem' -or
        [string]::IsNullOrWhiteSpace($Effect.ownerTarget.deviceName) -or
        @($Effect.ownerTarget.itemPath).Count -eq 0) {
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
    if ($selector.kind -cne 'ioSystem' -or [string]::IsNullOrWhiteSpace($selector.subnetId) -or
        $null -eq $selector.number -or $selector.number -lt 0) { throw 'Invalid exact IO-system selector.' }
    foreach ($key in $selector.Keys) { if ($key -cnotin @('kind', 'subnetId', 'number') -and $null -ne $selector[$key]) { throw 'Unexpected selector field.' } }
    $target = @{ kind = 'ioSystem'; subnetId = $selector.subnetId; number = $selector.number }
    $binding = @{ commit = $ExpectedCommit; tree = $ExpectedTree; harnessSha256 = $ExpectedHarnessSha256; manifestSha256 = $ExpectedManifestSha256; target = $target; fixtureAlias = $FixtureAlias }
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
    if ($publicBefore.project -isnot [Collections.IDictionary] -or
        $publicBefore.project.isOpen -isnot [bool] -or $publicBefore.project.isOpen -ne $true -or
        $publicBefore.project.path -isnot [string] -or $publicBefore.project.path -cne $ProjectPath -or
        $publicBefore.project.isModified -isnot [bool] -or $publicBefore.project.isModified) {
        throw 'Qualification requires an unmodified project baseline.'
    }
    $inspection = Get-PublicInspection
    $record.publicInspection = $inspection
    Assert-Same $publicBefore (Get-PublicStatus)
    $workerProcess = Start-Child $workerExe @() (Split-Path -Parent $workerExe)
    $before = Get-WorkerStatus
    $sessionIdentity = $before.identity
    Assert-Same $publicBefore $before.status
    $durableIdentity = @{ portalProcessId = $sessionIdentity.portalProcessId; projectPath = $ProjectPath; project = $before.status.project }
    $record.durableIdentity = $durableIdentity
    $raw = Invoke-Probe @{ method = 'probe_network_object_attributes'; expectedSessionIdentity = $sessionIdentity; networkObjectTarget = $target; networkAttributeNames = @('Name', 'Number', 'MultipleUseIoSystem', 'UseIoSystemNameAsDeviceNameExtension', 'MaxNumberIWlanLinksPerSegment') }
    Assert-Same $sessionIdentity $raw.sessionIdentity
    $ownerResponse = Invoke-Probe @{ method = 'probe_io_system_qualification'; confirm = $true; expectedSessionIdentity = $sessionIdentity; ioSystemQualification = @{ mode = 'inspectOwner'; target = $target } }
    $owner = ConvertFrom-Json $ownerResponse.payload -AsHashtable -Depth 100
    Assert-Owner $owner
    Assert-Same $sessionIdentity $ownerResponse.sessionIdentity
    # inspectOwner returns owner proof only. Values come from the public typed inspection.
    $owner.before = Get-Baseline $inspection
    Assert-Same $inspection (Get-PublicInspection)
    $record.ownerTarget = $owner.ownerTarget
    $record.baseline = $owner.before
    $record.proposal = $proposal
    if ($null -ne $proposal) { Assert-Proposal $proposal $owner }
    if ($null -ne $preview) {
        Assert-Same $preview.durableIdentity $durableIdentity
        Assert-Same $preview.baseline $owner.before
        Assert-Same $preview.ownerTarget $owner.ownerTarget
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
        Assert-EffectEvidence $record.effect $owner $proposal $Mode
        if ($record.effect.compileState -cnotin @('Success', 'Warning') -or $record.effect.errorCount -ne 0 -or $record.effect.evidenceOmitted) { throw 'Qualification did not establish successful complete compilation; stop and inspect evidence.' }
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
