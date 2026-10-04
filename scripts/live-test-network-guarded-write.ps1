#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized public-MCP guarded Network configuration/ordered-outcome acceptance.
.DESCRIPTION
    Inventory is the default. Preview explicitly sends dryRun:true. Apply explicitly sends
    dryRun:false; omitted dryRun executes too. The harness gates are client authorization,
    not server elicitation. All modes require a frozen source and exact already-open ProjectPath.
    FixturePath is a reviewed JSON fixture containing Operations, ExpectedItemStatuses,
    ExpectedSuccess, ExpectedVerificationSuccess, ExpectedSettings, Inspections, BeforeExpected,
    AfterExpected, BeforeNodes, AfterNodes, MultiHomedDevices, RestoreOperations, BeforeRestore,
    BeforeRestoreNodes, RestorationExpected and RestorationNodes. See local-mcp-testing.md.
    Apply additionally requires AllowMutation, APPLY GUARDED NETWORK FIXTURE, exact
    AuthorizedProjectPath and AuthorizedFixtureSha256. Explicit -Restore selects only the
    concrete reviewed RestoreOperations; fresh BeforeRestore inspections and nodes precede its
    dry run and actual call. No automatic restoration after a failed or uncertain write.
    No save, close, compile, download, PLC operation, implicit open, retry or inferred undo.
    Subnet-only movement does not promise implicit IO changes. Record exact subnet/IO tuples.
    This script is never invoked by automated tests. Static source tests do not prove live behavior.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')] [string] $Mode = 'Inventory',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [ValidateSet('read-write', 'full')] [string] $AccessMode = 'read-write',
    [Parameter(Mandatory)] [string] $FixturePath,
    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [string] $AuthorizedProjectPath,
    [string] $AuthorizedFixtureSha256,
    [switch] $Restore,
    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [Parameter(Mandatory)] [string] $ExpectedTree,
    [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,
    [Parameter(Mandatory)] [string] $ExpectedSharedHelperSha256,
    [int] $StartupTimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:HostProcess = $null
$script:NextRequestId = 0
$script:ObservedBinding = $null
$script:ElicitationCount = 0
$script:RequiredAcknowledgement = 'APPLY GUARDED NETWORK FIXTURE'
if (-not [System.IO.Path]::IsPathFullyQualified($ProjectPath) -or
    [System.IO.Path]::GetFullPath($ProjectPath) -cne $ProjectPath -or
    -not $ProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ProjectPath must be the exact canonical absolute approved .ap21 path.'
}
$fixtureSha256 = (Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
if ($Restore -and $Mode -ne 'Apply') { throw 'Restore requires explicitly authorized Apply mode.' }
if ($Mode -eq 'Apply') {
    if (-not $AllowMutation) { throw 'Apply requires AllowMutation.' }
    if ($Acknowledgement -cne $script:RequiredAcknowledgement -or
        $AuthorizedProjectPath -cne $ProjectPath -or
        $AuthorizedFixtureSha256 -ine $fixtureSha256) {
        throw 'Apply requires exact target, fixture hash and acknowledgement authorization.'
    }
}
$operations = if ($Restore) { @($fixture.RestoreOperations) } else { @($fixture.Operations) }
if ($operations.Count -lt 1 -or $operations.Count -gt 50) { throw 'Fixture must contain 1..50 exact operations.' }
foreach ($operation in $operations) {
    if ($operation.projectPath -cne $ProjectPath) { throw 'Fixture operation projectPath differs from approved target.' }
}
# --- Provenance -----------------------------------------------------------------------------

# --- network_read / network_write operations -------------------------------------------------

$script:McpClientName = 'live-test-network-guarded-write'
$script:SharedHelperPath = Join-Path $PSScriptRoot 'network-live-mcp-helpers.ps1'
if (-not (Test-Path -LiteralPath $script:SharedHelperPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $script:SharedHelperPath -Algorithm SHA256).Hash -ine $ExpectedSharedHelperSha256) {
    throw 'Shared Network helper is missing or differs from the frozen source; no host may start.'
}
. $script:SharedHelperPath
# Check each configured exact target is represented in concrete before/after node expectations.
foreach ($operation in $operations) {
    if ($operation.operation -ne 'configure_network_device') { throw 'This fixture harness accepts configuration operations only; use Phase4 for subnet lifecycle.' }
    $beforeNodes = if ($Restore) { @($fixture.BeforeRestoreNodes) } else { @($fixture.BeforeNodes) }
    $afterNodes = if ($Restore) { @($fixture.RestorationNodes) } else { @($fixture.AfterNodes) }
    foreach ($expectedNodes in @(@{ values = $beforeNodes }, @{ values = $afterNodes })) {
        $matched = @($expectedNodes.values | Where-Object { Test-NetworkNodeIdentity $operation.target $_ -SelectorConstraints })
        if ($matched.Count -ne 1) { throw 'Each configured exact node needs concrete before/after expectations.' }
    }
}

function Assert-NodeExpectations {
    param($Hardware, [object[]] $Expected)
    Assert-HardwareWriteEvidence $Hardware
    $nodes = @(Get-HardwareNodes $Hardware)
    foreach ($deviceName in @($fixture.MultiHomedDevices)) {
        if (@($nodes | Where-Object { [string]::Equals($_.deviceName, $deviceName, [System.StringComparison]::OrdinalIgnoreCase) }).Count -lt 2) {
            throw 'The exact multi-homed fixture does not have at least two readable nodes.'
        }
    }
    foreach ($expectation in $Expected) {
        $identity = Resolve-NetworkNodeSelectorEvidence $Hardware $expectation
        # Source indices have been checked above; ordinary selectors expose semantic owners.
        $constraints = $identity | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100
        $constraints.Remove('nodeIndex')
        $found = @($nodes | Where-Object { Test-NetworkNodeIdentity $constraints $_.identity -SelectorConstraints })
        if ($found.Count -ne 1) { throw 'Expected one exact device/node identity.' }
        foreach ($key in @('subnetId', 'ioSystemSubnetId', 'ioSystemNumber')) {
            if (-not $expectation.ContainsKey($key)) { throw 'Node expectation must specify the complete exact subnet/IO tuple, including nulls.' }
            if ($found[0].node.connectionEvidence.$key -cne $expectation[$key]) { throw "Exact node relationship mismatch: $key." }
        }
        foreach ($key in @('ipAddress', 'subnetMask', 'pnDeviceName')) {
            if ($expectation.ContainsKey($key) -and $found[0].node.$key -cne $expectation[$key]) { throw "Node setting mismatch: $key." }
        }
    }
    return $nodes
}
function Assert-Subset {
    param($Observed, $Expected, [string] $Path = 'inspection')
    if ($Expected -is [System.Collections.IDictionary]) {
        foreach ($key in $Expected.Keys) {
            if ($Observed -is [System.Collections.IDictionary]) {
                if (-not $Observed.Contains($key)) { throw "Missing $Path.$key." }
                $value = $Observed[$key]
            } else {
                $member = $Observed.PSObject.Properties[$key]
                if ($null -eq $member) { throw "Missing $Path.$key." }
                $value = $member.Value
            }
            Assert-Subset $value $Expected[$key] "$Path.$key"
        }
    } elseif ($Expected -is [array]) {
        if ($Observed -isnot [array] -or $Observed.Count -ne $Expected.Count) { throw "Array mismatch at $Path." }
        for ($i = 0; $i -lt $Expected.Count; $i++) { Assert-Subset $Observed[$i] $Expected[$i] "$Path[$i]" }
    } elseif ($null -eq $Expected) {
        if ($null -ne $Observed) { throw "Expected explicit null at $Path." }
    } elseif ($Observed -cne $Expected) { throw "Evidence mismatch at $Path." }
}
function Read-FixtureIdentities {
    param($Hardware)
    $observations = [ordered]@{}
    foreach ($inspection in @($fixture.Inspections)) {
        $operation = @{ operationId = $inspection.id; operation = 'inspect_network_object'; projectPath = $ProjectPath; target = $inspection.target }
        if ($inspection.ContainsKey('attributeNames')) { $operation.attributeNames = $inspection.attributeNames }
        $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{ operations = @($operation) }
        $items = @($response.batch.operations)
        if (-not $response.success -or $items.Count -ne 1 -or $items[0].status -ne 'succeeded' -or
            $null -eq $items[0].result -or $null -ne $items[0].omission) { throw 'Fresh exact identity inspection failed or was omitted.' }
        if ($null -ne (Get-NetworkMember $inspection.target 'itemPath')) {
            $identity = Resolve-NetworkNodeSelectorEvidence $Hardware $inspection.target
            if ((Get-NetworkMember $items[0].result.target 'kind') -cne 'node' -or
                -not (Test-NetworkNodeIdentity $identity $items[0].result.target -SelectorConstraints)) { throw 'Fresh normalized legacy inspection identity differs from source evidence.' }
        } else { Assert-Subset $items[0].result.target $inspection.target }
        $observations[$inspection.id] = $items[0].result
    }
    return $observations
}
function Assert-Inspections {
    param($Observed, $Expected, $Hardware)
    if ($Expected.Count -eq 0) { throw 'Concrete fixture inspection expectations are required.' }
    foreach ($id in $Expected.Keys) {
        $expectedResult = $Expected[$id]
        $target = Get-NetworkMember $expectedResult 'target'
        if ($null -ne (Get-NetworkMember $target 'itemPath')) {
            $actual = Get-NetworkMember $Observed $id
            $identity = Resolve-NetworkNodeSelectorEvidence $Hardware $target
            if ($null -eq $actual -or (Get-NetworkMember $actual.target 'kind') -cne 'node' -or
                -not (Test-NetworkNodeIdentity $identity $actual.target -SelectorConstraints)) { throw 'Expected legacy inspection identity differs from fresh source evidence.' }
            $remaining = $expectedResult | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100
            $remaining.Remove('target')
            Assert-Subset $actual $remaining
        } else { Assert-Subset $Observed @{ $id=$expectedResult } }
    }
}
function Assert-VerificationCheck {
    param($Check, [string] $Name, [string] $Expected)
    if ($null -eq $Check -or $Check.name -isnot [string] -or $Check.name -cne $Name -or
        $Check.expected -isnot [string] -or $Check.expected -cne $Expected -or
        $Check.observed -isnot [string] -or $Check.status -notin @('passed', 'failed') -or
        ($null -ne $Check.message -and $Check.message -isnot [string]) -or
        ($Check.status -eq 'passed' -and $Check.observed -cne $Expected) -or
        ($Check.status -eq 'failed' -and ($Check.observed -ceq $Expected -or [string]::IsNullOrWhiteSpace($Check.message)))) {
        throw 'Verification check is missing, unreadable, wrongly typed or contradictory; inspect before retry.'
    }
}
function Assert-Outcome {
    param($Response, [object[]] $ExpectedItemStatuses, [bool] $ExpectedSuccess, [bool] $ExpectedVerificationSuccess, [object[]] $ExpectedSettings)
    if ($Response.contractVersion -ne '1.0' -or $Response.phase -ne 'applied' -or $null -ne $Response.error -or
        $Response.success -isnot [bool] -or $Response.success -ne $ExpectedSuccess -or $null -ne $Response.omission -or
        $Response.verification.success -isnot [bool] -or $Response.verification.success -ne $ExpectedVerificationSuccess -or
        $null -ne $Response.verification.omission) { throw 'Unexpected typed applied outcome; inspect before retry.' }
    $items = @($Response.batch.operations)
    if ($items.Count -ne $operations.Count -or $items.Count -ne $ExpectedItemStatuses.Count) { throw 'Ordered outcome count mismatch.' }
    $failed = $false
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        if ($item.operationId -cne $operations[$i].operationId -or $item.status -cne $ExpectedItemStatuses[$i] -or $null -ne $item.omission) {
            throw 'Exact ordered operation identity/status or evidence delivery mismatch.'
        }
        if ($failed -and ($item.status -ne 'skipped' -or $item.skipReason -ne 'earlierOperationFailed')) { throw 'Later operation was not stopped after failure.' }
        if ($item.status -eq 'failed') { $failed = $true }
    }
    foreach ($settings in $ExpectedSettings) {
        $item = @($items | Where-Object { $_.operationId -ceq $settings.operationId })
        if ($item.Count -ne 1 -or $null -eq $item[0].result) { throw 'Expected sparse typed settings result unavailable.' }
        foreach ($field in @('appliedSettings', 'skippedSettings')) {
            $observed = $item[0].result.$field | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            if ($observed.Count -ne $settings[$field].Count) { throw 'Unrequested settings appeared or requested sparse settings vanished.' }
            Assert-Subset $observed $settings[$field]
        }
    }
    # Require the exact attempted sequence, not just any entries the response happens to include.
    $attempted = @($items | Where-Object { $_.status -ne 'skipped' })
    $immediateChecks = $Response.verification.operations
    $finalChecks = $Response.verification.finalChecks
    if ($immediateChecks -isnot [array] -or $finalChecks -isnot [array] -or $immediateChecks.Count -ne $attempted.Count) {
        throw 'Complete immediate/final verification arrays are required.'
    }
    $requiredFinal = [System.Collections.Generic.List[object]]::new()
    $verificationPassed = $true
    for ($i = 0; $i -lt $attempted.Count; $i++) {
        $item = $attempted[$i]
        $operation = @($operations | Where-Object { $_.operationId -ceq $item.operationId })[0]
        $verification = $immediateChecks[$i]
        $evidence = $verification.evidence
        if ($verification.operationId -cne $item.operationId -or $verification.operation -cne 'configure_network_device' -or
            $item.operation -cne $verification.operation -or $null -ne $verification.omission -or $null -eq $evidence -or
            $null -eq $item.result -or $item.result.deviceName -isnot [string] -or
            -not [string]::Equals($item.result.deviceName, $operation.target.deviceName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $evidence.status -cne $verification.status -or $evidence.status -notin @('passed', 'failed', 'not_required') -or
            $evidence.identity.deviceName -isnot [string] -or $evidence.identity.nodeId -isnot [string] -or
            -not (Test-NetworkNodeIdentity $operation.target $evidence.identity) -or
            @($evidence.identity.PSObject.Properties | Where-Object { $_.Name -cnotin @('deviceName','nodeId','interfacePath','interfaceName') }).Count -ne 0 -or $evidence.checks -isnot [array]) {
            throw 'Exact attempted identity/order and typed immediate evidence are required.'
        }
        $identityKey = Get-NetworkNodeKey $evidence.identity
        $resultEvidence = $item.result.verification | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable
        Assert-Subset $evidence $resultEvidence
        $requested = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
        foreach ($pair in @(@('Address','ipAddress'), @('SubnetMask','subnetMask'), @('PnDeviceName','pnDeviceName'))) {
            if ($operation.changes.ContainsKey($pair[1]) -and $null -ne $operation.changes[$pair[1]]) { $requested.Add($pair[0], $operation.changes[$pair[1]]) }
        }
        if ($operation.changes.ContainsKey('subnet') -and $null -ne $operation.changes.subnet) { $requested.Add('Subnet', $operation.changes.subnet.subnetId) }
        if ($operation.changes.ContainsKey('ioSystem') -and $null -ne $operation.changes.ioSystem) { $requested.Add('IoSystem', $operation.changes.ioSystem.number.ToString([System.Globalization.CultureInfo]::InvariantCulture)) }
        $applied = $item.result.appliedSettings | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $skipped = $item.result.skippedSettings | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $accounted = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($key in $skipped.Keys) {
            if (-not $requested.ContainsKey($key) -or $skipped[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($skipped[$key]) -or -not $accounted.Add($key)) { throw 'Malformed sparse skipped settings.' }
        }
        if ($skipped.Count -gt 0 -and $item.status -ne 'failed') { throw 'Requested skips must fail the item.' }
        if ($evidence.checks.Count -ne $applied.Count) { throw 'Immediate applied-setting check coverage is incomplete.' }
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($check in $evidence.checks) {
            if ($null -eq $check -or -not $seen.Add($check.name) -or -not $applied.Contains($check.name) -or
                -not $requested.ContainsKey($check.name) -or $applied[$check.name] -isnot [string] -or
                $applied[$check.name] -cne $requested[$check.name] -or -not $accounted.Add($check.name)) { throw 'Missing, duplicate or unexpected applied verification keys.' }
            $expected = $applied[$check.name]
            if ($check.name -ceq 'IoSystem') {
                $tuple = ConvertFrom-Json -InputObject $check.expected -NoEnumerate
                if ($tuple -isnot [array] -or $tuple.Count -ne 2 -or $tuple[0] -cne $operation.changes.ioSystem.subnetId -or
                    $tuple[1] -isnot [long] -and $tuple[1] -isnot [int] -or $tuple[1] -ne $operation.changes.ioSystem.number) { throw 'Immediate IO verification requires the exact subnet/number tuple.' }
                $expected = $check.expected
            }
            Assert-VerificationCheck $check $check.name $expected
        }
        if ($accounted.Count -ne $requested.Count) { throw 'Requested settings are missing from sparse results.' }
        $status = if ($applied.Count -eq 0) { 'not_required' } elseif (@($evidence.checks | Where-Object { $_.status -eq 'failed' }).Count) { 'failed' } else { 'passed' }
        if ($evidence.status -cne $status) { throw 'Immediate summary contradicts applied-setting evidence.' }
        if ($status -eq 'failed') { $verificationPassed = $false }
        # Configuration-only effective prefix: latest explicit same-field value supersedes; IO remains independent of Subnet.
        foreach ($field in @('exists') + @($evidence.checks | ForEach-Object { $_.name })) {
            $value = if ($field -ceq 'exists') { 'true' } else { @($evidence.checks | Where-Object { $_.name -ceq $field })[0].expected }
            $existing = @($requiredFinal | Where-Object { $_.identityKey -ceq $identityKey -and $_.field -ceq $field })
            if ($existing.Count) { $existing[0].expected = $value }
            else { $requiredFinal.Add(@{ identityKey=$identityKey; identity=$evidence.identity; field=$field; expected=$value }) }
        }
    }
    if ($finalChecks.Count -ne $requiredFinal.Count) { throw 'Final effective-prefix evidence is incomplete.' }
    $seenFinal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($check in $finalChecks) {
        if ($null -eq $check -or -not $seenFinal.Add($check.name)) { throw 'Duplicate or missing final evidence.' }
        $expected = @($requiredFinal | Where-Object { $check.name -ceq (Get-NetworkNodeCheckName $_.identity $_.field) })
        if ($expected.Count -ne 1) { throw 'Unexpected final identity/setting evidence.' }
        Assert-VerificationCheck $check $check.name $expected[0].expected
        if ($check.status -eq 'failed') { $verificationPassed = $false }
    }
    if ($Response.verification.success -ne $verificationPassed) { throw 'Verification summary contradicts immediate/final evidence.' }
}
function Invoke-NetworkWritePreview {
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    Assert-HardwareWriteEvidence (Read-HardwareConfig)
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    $preview = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $operations; dryRun = $true }
    if ($preview.phase -ne 'preview' -or -not $preview.success -or $null -ne $preview.error -or
        $null -ne $preview.omission -or @($preview.effects | Where-Object { $null -ne $_.omission }).Count -ne 0) {
        throw 'Preview blocked or incomplete; no execution permitted.'
    }
    return $preview
}
function Invoke-NetworkWriteApply {
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    Assert-HardwareWriteEvidence (Read-HardwareConfig)
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    return (Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $operations; dryRun = $false })
}
function Invoke-LifecycleGroupAndVerify {
    $script:Evidence['preview'] = Invoke-NetworkWritePreview
    $script:Evidence['applied'] = Invoke-NetworkWriteApply
    if ($Restore) {
        Assert-Outcome $script:Evidence.applied (@($operations | ForEach-Object { 'succeeded' })) $true $true @()
    } else {
        Assert-Outcome $script:Evidence.applied $fixture.ExpectedItemStatuses $fixture.ExpectedSuccess $fixture.ExpectedVerificationSuccess $fixture.ExpectedSettings
    }
}
function Invoke-Inventory {
    $script:Evidence['hardware'] = Read-HardwareConfig
    $script:Evidence['inspections'] = Read-FixtureIdentities $script:Evidence.hardware
}
function Invoke-Preview {
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    Invoke-Inventory
    Assert-Inspections $script:Evidence.inspections $fixture.BeforeExpected $script:Evidence.hardware
    Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeNodes | Out-Null
    $script:Evidence['preview'] = Invoke-NetworkWritePreview
}
function Invoke-Apply {
    Assert-NetworkFixtureHash $FixturePath $fixtureSha256
    Invoke-Inventory
    if ($Restore) {
        # Fresh exact identity inspection and concrete restoration preconditions BEFORE preview.
        Assert-Inspections $script:Evidence.inspections $fixture.BeforeRestore $script:Evidence.hardware
        Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeRestoreNodes | Out-Null
    } else {
        Assert-Inspections $script:Evidence.inspections $fixture.BeforeExpected $script:Evidence.hardware
        Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeNodes | Out-Null
    }
    Invoke-LifecycleGroupAndVerify
    $script:Evidence['postHardware'] = Read-HardwareConfig
    $script:Evidence['postInspections'] = Read-FixtureIdentities $script:Evidence.postHardware
    if ($Restore) {
        Assert-Inspections $script:Evidence.postInspections $fixture.RestorationExpected $script:Evidence.postHardware
        Assert-NodeExpectations $script:Evidence.postHardware $fixture.RestorationNodes | Out-Null
    } else {
        Assert-Inspections $script:Evidence.postInspections $fixture.AfterExpected $script:Evidence.postHardware
        Assert-NodeExpectations $script:Evidence.postHardware $fixture.AfterNodes | Out-Null
    }
}
$candidate = Assert-FrozenCandidate -ExpectedCommit $ExpectedCommit -ExpectedTree $ExpectedTree -ExpectedHarnessSha256 $ExpectedHarnessSha256 -HarnessPath $PSCommandPath -ExpectedSharedHelperSha256 $ExpectedSharedHelperSha256
$script:Evidence = [ordered]@{
    mode = $Mode; restore = [bool]$Restore; requestedProjectPath = $ProjectPath; accessMode = $AccessMode
    fixtureSha256 = $fixtureSha256.ToLowerInvariant(); requestedOperations = $operations
    testedCommit = $candidate.testedCommit; testedTree = $candidate.testedTree; testedHarnessSha256 = $candidate.testedHarnessSha256; testedSharedHelperSha256 = $candidate.testedSharedHelperSha256
    outcome = 'started'; failure = $null
}
try {
    if ($Mode -in @('Preview','Apply')) { Assert-NetworkFixtureHash $FixturePath $fixtureSha256 }
    Connect-McpHost | Out-Null
    $script:Evidence['projectStatus'] = Get-ObservedProjectStatus
    switch ($Mode) {
        'Inventory' { Invoke-Inventory }
        'Preview' { Invoke-Preview }
        'Apply' { Invoke-Apply }
    }
    $script:Evidence.outcome = 'passed'
} catch {
    $script:Evidence.outcome = 'failed-inspect-before-retry'
    $script:Evidence.failure = $_.Exception.Message
    throw
} finally {
    Stop-McpHost
    $script:Evidence['elicitationCount'] = $script:ElicitationCount
    $script:Evidence['generatedAtUtc'] = [DateTime]::UtcNow.ToString('o')
    $artifactRoot = Join-Path $script:RepositoryRoot 'artifacts/live-network-guarded-write'
    [void](New-Item -ItemType Directory -Force -Path $artifactRoot)
    $artifactPath = Join-Path $artifactRoot ((Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + $Mode.ToLowerInvariant() + '.json')
    $script:Evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
    [Console]::Out.WriteLine($artifactPath)
}
