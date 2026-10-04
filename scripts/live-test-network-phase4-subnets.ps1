#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized public-MCP acceptance for Ethernet/PROFIBUS subnet lifecycle.
.DESCRIPTION
    Inventory (default) reads only. Preview uses dryRun:true. Apply requires AllowMutation
    and the exact acknowledgement DELETE SUBNETS AND KEEP DEVICES before host startup.
    The explicit already-open ProjectPath is inspected, then bound with bind_project.
    No save, close, compile, download, PLC action, batch rollback or automatic retry.
    Connected Ethernet/PROFIBUS subnet IDs must come from fresh inventory; names are not IDs.
    Apply creates/updates/deletes isolated subnets, then permanently deletes the two connected
    fixture subnets. It does not restore connected deletions: use a disposable backed-up fixture
    and a separately authorized restoration procedure, followed by fresh identity inspection.
    Network has zero server elicitation in read-write/full; harness gates are client protections.
    Explicit dryRun:false is used for execution (omitting dryRun also executes).
    Commit/tree/script SHA-256 freeze evidence is required in every mode. No mode runs in CI.
    Historical Phase4 acceptance does not qualify this guarded candidate.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')]
    [string] $Mode = 'Inventory',

    [Parameter(Mandatory)] [string] $ProjectPath,
    [ValidateSet('read-write', 'full')] [string] $AccessMode = 'read-write',

    [string] $ConnectedEthernetSubnetId,
    [string] $ConnectedProfibusSubnetId,

    [switch] $AllowMutation,
    [string] $Acknowledgement,

    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [Parameter(Mandatory)] [string] $ExpectedTree,
    [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,
    [Parameter(Mandatory)] [string] $ExpectedSharedHelperSha256,

    [int] $StartupTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:RequiredAcknowledgement = 'DELETE SUBNETS AND KEEP DEVICES'
$script:EthernetNetworkType = 'Ethernet'
$script:ProfibusNetworkType = 'Profibus'
$script:HostProcess = $null
$script:NextRequestId = 0
$script:ObservedBinding = $null
$script:ElicitationCount = 0

# --- Mode gating: validated BEFORE the MCP host is launched or anything is read/written --------

if (-not $ProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ProjectPath must be an explicit '.ap21' TIA Portal V21 project path. Got: '$ProjectPath'."
}
if (-not [System.IO.Path]::IsPathFullyQualified($ProjectPath) -or
    -not [string]::Equals([System.IO.Path]::GetFullPath($ProjectPath), $ProjectPath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ProjectPath must be an absolute canonical path to the approved project. Got: '$ProjectPath'."
}

if ($Mode -ne 'Inventory') {
    if ([string]::IsNullOrWhiteSpace($ConnectedEthernetSubnetId) -or [string]::IsNullOrWhiteSpace($ConnectedProfibusSubnetId)) {
        throw "Mode '$Mode' requires -ConnectedEthernetSubnetId and -ConnectedProfibusSubnetId -- the exact existing subnetId values of the caller-supplied connected subnets. A name is never accepted here."
    }
}

if ($Mode -eq 'Apply') {
    if (-not $AllowMutation) {
        throw "Mode 'Apply' additionally requires -AllowMutation. This switch exists so Apply can never run by accident."
    }
    if ($Acknowledgement -cne $script:RequiredAcknowledgement) {
        throw "Mode 'Apply' requires the EXACT acknowledgement string '$script:RequiredAcknowledgement' passed via -Acknowledgement. There is no shortcut and no default-yes value."
    }
}

# --- Minimal real MCP JSON-RPC client over the host's stdio, reused from live-test-network-phase2.ps1 -------

$script:McpClientName = 'live-test-network-phase4-subnets'
$script:SharedHelperPath = Join-Path $PSScriptRoot 'network-live-mcp-helpers.ps1'
if (-not (Test-Path -LiteralPath $script:SharedHelperPath -PathType Leaf) -or
    (Get-FileHash -LiteralPath $script:SharedHelperPath -Algorithm SHA256).Hash -ine $ExpectedSharedHelperSha256) {
    throw 'Shared Network helper is missing or differs from the frozen source; no host may start.'
}
. $script:SharedHelperPath
function Invoke-McpToolCallExpectingError {
    # Invoke-McpToolCall throws on isError:true, which is the wrong helper for a call that is
    # EXPECTED to fail (the three non-mutating negative Preview cases below). This bypasses that
    # throwing wrapper, asserts the call actually failed (throwing if it unexpectedly succeeded --
    # that would mean validation regressed), and returns the parsed error envelope using the same
    # structuredContent-preferred / text-block-fallback logic as Invoke-McpToolCall.
    param([string] $Name, [hashtable] $Arguments)
    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($null -eq $result) {
        throw "Tool '$Name' returned no result for 'tools/call'."
    }
    if (-not (Get-ToolResultIsError -Result $result)) {
        throw "Tool '$Name' was expected to return isError:true for this negative case but succeeded instead -- this likely means validation regressed."
    }
    $structuredContent = Get-ToolResultStructuredContent -Result $result
    if ($null -ne $structuredContent) { return $structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

# --- Provenance -----------------------------------------------------------------------------

function Get-ServerCommit {
    try {
        $commit = (& git -C $script:RepositoryRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commit)) {
            return $commit.Trim().ToLowerInvariant()
        }
    }
    catch {
        return $null
    }
    return $null
}

# --- network_read / network_write operations -------------------------------------------------

function Assert-ConnectedFixture {
    param($Hardware)
    foreach ($expected in @(@{ id = $ConnectedEthernetSubnetId; type = $script:EthernetNetworkType },
                            @{ id = $ConnectedProfibusSubnetId; type = $script:ProfibusNetworkType })) {
        $found = @($Hardware.subnets | Where-Object { $_.subnetId -ceq $expected.id })
        if ($found.Count -ne 1 -or $found[0].networkType -cne $expected.type -or
            $null -eq $found[0].connectionEvidence -or -not $found[0].connectionEvidence.complete -or
            @($found[0].connectionEvidence.nodes).Count -eq 0) {
            throw 'Exact connected Ethernet/PROFIBUS fixture identity or complete inventory was not proved.'
        }
    }
    Get-HardwareNodes $Hardware | Out-Null
}

function Get-ExistingSubnetName {
    param([Parameter(Mandatory)] [object] $Hardware, [Parameter(Mandatory)] [string] $SubnetId)
    # Named "subnetMatches" rather than PowerShell's automatic "matches" variable (the one
    # populated by the -match operator), so a future edit that also uses -match in this scope
    # cannot be silently confused by this being an ordinary local variable instead.
    $subnetMatches = @($Hardware.subnets | Where-Object { $_.subnetId -eq $SubnetId })
    if ($subnetMatches.Count -ne 1) {
        throw "Expected exactly one subnet with subnetId '$SubnetId' in the current hardware configuration; found $($subnetMatches.Count)."
    }
    return $subnetMatches[0].name
}

function New-CreateSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $NetworkType,
        [int] $HighestAddress,
        [string] $TransmissionSpeed
    )
    $subnet = [ordered]@{ name = $Name; networkType = $NetworkType }
    if ($PSBoundParameters.ContainsKey('HighestAddress')) { $subnet.highestAddress = $HighestAddress }
    if (-not [string]::IsNullOrWhiteSpace($TransmissionSpeed)) { $subnet.transmissionSpeed = $TransmissionSpeed }
    [ordered]@{
        operationId = $OperationId
        operation   = 'create_subnet'
        projectPath = $ProjectPath
        subnet      = $subnet
    }
}

function New-UpdateSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $SubnetId,
        [Parameter(Mandatory)] [hashtable] $Changes
    )
    [ordered]@{
        operationId   = $OperationId
        operation     = 'update_subnet'
        projectPath   = $ProjectPath
        target        = [ordered]@{ kind = 'subnet'; subnetId = $SubnetId }
        subnetChanges = $Changes
    }
}

function New-DeleteSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $SubnetId
    )
    [ordered]@{
        operationId = $OperationId
        operation   = 'delete_subnet'
        projectPath = $ProjectPath
        target      = [ordered]@{ kind = 'subnet'; subnetId = $SubnetId }
    }
}

function Invoke-NetworkWritePreview {
    param([Parameter(Mandatory)] [object[]] $Operations)
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $Operations; dryRun = $true }
    if ($response.phase -ne 'preview') {
        throw "Expected phase 'preview' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    if (-not $response.success -or $null -ne $response.omission) { throw 'Preview evidence incomplete; no apply.' }
    return $response
}

function Invoke-NetworkWriteApply {
    param([Parameter(Mandatory)] [object[]] $Operations)
    # Sole actual call, reachable only from the authorized Apply dispatch.
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $Operations; dryRun = $false }
    if ($response.phase -ne 'applied' -or $null -ne $response.error -or $response.contractVersion -ne '1.0') {
        throw 'Expected canonical applied Network response. Inspect before retry.'
    }
    if ($null -ne $response.omission) { throw 'Applied evidence omitted. Inspect before retry.' }
    return $response
}

function Get-PreviewRecord {
    param([Parameter(Mandatory)] [object] $Preview)
    return $Preview
}

function Invoke-LifecycleGroupAndVerify {
    param(
        [Parameter(Mandatory)] [string] $GroupName,
        [Parameter(Mandatory)] [object[]] $Operations,
        [Parameter(Mandatory)] [int] $TotalHardwareDeviceCountBefore,
        [Parameter(Mandatory)] [ref] $RootDeviceCount
    )

    $preview = Invoke-NetworkWritePreview -Operations $Operations
    $previewRecord = Get-PreviewRecord -Preview $preview
    if (-not $preview.success) { throw 'Preview was blocked or incomplete; no mutation attempted.' }
    $applied = Invoke-NetworkWriteApply -Operations $Operations
    if ($applied.verification.success -isnot [bool] -or -not $applied.verification.success -or
        @($applied.verification.finalChecks | Where-Object { $_.status -ne 'passed' }).Count -ne 0 -or
        @($applied.verification.operations | Where-Object { $_.status -notin @('passed', 'not_required') }).Count -ne 0) {
        throw 'Applied verification failed or is unavailable. Inspect before retry; no restoration attempted.'
    }
    if (-not $applied.success) {
        throw "network_write group '$GroupName' reported success:false: $($applied.batch | ConvertTo-Json -Compress -Depth 20)"
    }

    $results = @()
    foreach ($item in @($applied.batch.operations)) {
        if ($item.status -ne 'succeeded') {
            throw "Group '$GroupName' operation '$($item.operationId)' did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 20)"
        }

        $memberNames = @($item.result.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
        $expectedMemberNames = @('name', 'networkDeviceCount', 'networkDeviceCountUnchanged', 'subnetId', 'verification')
        if (Compare-Object -ReferenceObject $expectedMemberNames -DifferenceObject $memberNames) {
            throw "Group '$GroupName' operation '$($item.operationId)' result is not the typed lifecycle shape including immediate verification. Got: $($memberNames -join ', ')."
        }
        if ($item.result.networkDeviceCountUnchanged -isnot [bool] -or
            -not $item.result.networkDeviceCountUnchanged) {
            throw "Group '$GroupName' operation '$($item.operationId)' reported networkDeviceCountUnchanged:false."
        }
        if (($item.result.networkDeviceCount -isnot [int] -and
             $item.result.networkDeviceCount -isnot [long]) -or
            $item.result.networkDeviceCount -lt 0) {
            throw "Group '$GroupName' returned no nonnegative integer root device count."
        }
        if ($null -eq $RootDeviceCount.Value) {
            # First lifecycle result establishes the observed root count; never use the hardware aggregate.
            $RootDeviceCount.Value = $item.result.networkDeviceCount
        }
        elseif ($item.result.networkDeviceCount -ne $RootDeviceCount.Value) {
            throw "Group '$GroupName' reported a root device count inconsistent with earlier lifecycle results."
        }

        $results += [ordered]@{
            operationId        = $item.operationId
            operation          = $item.operation
            subnetId           = $item.result.subnetId
            name               = $item.result.name
            networkDeviceCount = $item.result.networkDeviceCount
            networkDeviceCountUnchanged = $item.result.networkDeviceCountUnchanged
        }
    }

    $postRead = Read-HardwareConfig
    $postNodes = @(Get-HardwareNodes $postRead)
    foreach ($effect in @($preview.effects)) {
        foreach ($affected in @($effect.effect.affectedNodes)) {
            $found = @($postNodes | Where-Object { $_.deviceName -ceq $affected.deviceName -and $_.node.nodeId -ceq $affected.nodeId })
            if ($found.Count -ne 1) { throw 'Affected exact device/node was not preserved in fresh grouped/ungrouped inventory.' }
            if ($effect.effect.operation -eq 'delete_subnet' -and
                ($found[0].node.connectionEvidence.subnetId -ceq $effect.effect.target.subnetId -or
                 $found[0].node.connectionEvidence.ioSystemSubnetId -ceq $effect.effect.target.subnetId)) {
                throw 'Fresh affected node still references the deleted subnet/IO tuple.'
            }
        }
    }
    $totalHardwareDeviceCountAfter = @($postRead.devices).Count
    if ($totalHardwareDeviceCountAfter -ne $TotalHardwareDeviceCountBefore) {
        throw "Group '$GroupName' changed the total hardware device count from $TotalHardwareDeviceCountBefore to $totalHardwareDeviceCountAfter."
    }

    [ordered]@{
        groupName               = $GroupName
        requestedOperations     = $Operations
        preview                 = $previewRecord
        applyResults            = $results
        postReadSubnetIds       = @($postRead.subnets | ForEach-Object { $_.subnetId })
        postReadTotalHardwareDeviceCount = $totalHardwareDeviceCountAfter
        totalHardwareDeviceCountUnchanged = $true
        rootDeviceCount = $RootDeviceCount.Value
        rootDeviceCountUnchanged = $true
    }
}

# --- Modes ------------------------------------------------------------------------------------

function Invoke-Inventory {
    $hardware = Read-HardwareConfig
    $projectStatus = Get-ObservedProjectStatus
    [ordered]@{
        mode            = 'Inventory'
        totalHardwareDeviceCount = @($hardware.devices).Count
        subnets         = @($hardware.subnets | ForEach-Object {
                [ordered]@{ subnetId = $_.subnetId; name = $_.name; networkType = $_.networkType; connectedNodeNames = @($_.ConnectedNodeNames) }
            })
        projectVersion  = $projectStatus.projectVersion
        projectStatus   = $projectStatus
    }
}

function Invoke-Preview {
    $before = Read-HardwareConfig
    Assert-ConnectedFixture $before
    $projectStatus = Get-ObservedProjectStatus

    # Harness-created ISOLATED subnets: request-derived identity only, no subnetId invented here.
    $createOperations = @(
        New-CreateSubnetOperation -OperationId 'preview-create-eth' -Name 'Phase4HarnessEthernetPreview' -NetworkType $script:EthernetNetworkType
        New-CreateSubnetOperation -OperationId 'preview-create-pb' -Name 'Phase4HarnessProfibusPreview' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'Baud500000'
    )

    # Caller-supplied CONNECTED subnets: exact existing subnetId only -- a name is never accepted.
    $updateOperations = @(
        New-UpdateSubnetOperation -OperationId 'preview-update-eth' -SubnetId $ConnectedEthernetSubnetId -Changes @{ name = (Get-ExistingSubnetName -Hardware $before -SubnetId $ConnectedEthernetSubnetId) }
        New-UpdateSubnetOperation -OperationId 'preview-update-pb' -SubnetId $ConnectedProfibusSubnetId -Changes @{ name = (Get-ExistingSubnetName -Hardware $before -SubnetId $ConnectedProfibusSubnetId) }
    )
    $deleteOperations = @(
        New-DeleteSubnetOperation -OperationId 'preview-delete-connected-eth' -SubnetId $ConnectedEthernetSubnetId
        New-DeleteSubnetOperation -OperationId 'preview-delete-connected-pb' -SubnetId $ConnectedProfibusSubnetId
    )

    $createPreview = Invoke-NetworkWritePreview -Operations $createOperations
    $updatePreview = Invoke-NetworkWritePreview -Operations $updateOperations
    $deletePreview = Invoke-NetworkWritePreview -Operations $deleteOperations

    # --- Non-mutating negative-path checks -------------------------------------------------------
    # These negative dry runs never dispatch a mutation.
    $negativeCases = @()

    # 1. Invalid transmission-speed symbol -- rejected by NetworkOperationCatalog.ValidateWrite
    #    before any hardware state is even read.
    $invalidSpeedOperation = New-CreateSubnetOperation -OperationId 'preview-negative-invalid-transmission-speed' -Name 'Phase4HarnessInvalidSpeedPreview' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'NotARealBaudRate'
    $invalidSpeedResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($invalidSpeedOperation); dryRun = $true }
    if ($invalidSpeedResult.error.category -ne 'validation_error') {
        throw "Expected error.category 'validation_error' for the invalid-transmission-speed negative case; got '$($invalidSpeedResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'invalid-transmission-speed-symbol'
        operation     = $invalidSpeedOperation
        errorCategory = $invalidSpeedResult.error.category
        errorMessage  = $invalidSpeedResult.error.message
    }

    # 2. PROFIBUS-only field on an Ethernet target -- rejected during preview's target resolution
    #    (NetworkIdentityResolver.ResolveExistingSubnet), after the hardware read but before any
    #    mutation is dispatched (ordinary discovery reads may run).
    $ethernetHighestAddressOperation = New-UpdateSubnetOperation -OperationId 'preview-negative-ethernet-highest-address' -SubnetId $ConnectedEthernetSubnetId -Changes @{ highestAddress = 10 }
    $ethernetHighestAddressResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($ethernetHighestAddressOperation); dryRun = $true }
    if ($ethernetHighestAddressResult.error.category -ne 'validation_error') {
        throw "Expected error.category 'validation_error' for the PROFIBUS-only-field-on-Ethernet negative case; got '$($ethernetHighestAddressResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'profibus-only-field-on-ethernet-target'
        operation     = $ethernetHighestAddressOperation
        errorCategory = $ethernetHighestAddressResult.error.category
        errorMessage  = $ethernetHighestAddressResult.error.message
    }

    # 3. Bogus subnetId, synthesized deterministically from the real connected Ethernet subnetId
    #    already read above -- defensively confirmed absent from the current hardware
    #    configuration before use. Rejected during preview's target resolution's exact-one-match
    #    check (NetworkIdentityResolver.ResolveExistingSubnet's no-match branch), which reports
    #    postcondition_failed for the established lifecycle selector category.
    $bogusSubnetId = "$ConnectedEthernetSubnetId-does-not-exist"
    if (@($before.subnets | Where-Object { $_.subnetId -eq $bogusSubnetId }).Count -ne 0) {
        throw "The synthesized bogus subnetId '$bogusSubnetId' unexpectedly collides with a real subnet in the current hardware configuration -- choose a different suffix."
    }
    $bogusSubnetOperation = New-DeleteSubnetOperation -OperationId 'preview-negative-bogus-subnet-id' -SubnetId $bogusSubnetId
    $bogusSubnetResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($bogusSubnetOperation); dryRun = $true }
    if ($bogusSubnetResult.error.category -ne 'postcondition_failed') {
        throw "Expected error.category 'postcondition_failed' for the bogus-subnetId negative case; got '$($bogusSubnetResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'bogus-subnet-id'
        operation     = $bogusSubnetOperation
        errorCategory = $bogusSubnetResult.error.category
        errorMessage  = $bogusSubnetResult.error.message
    }

    [ordered]@{
        mode                = 'Preview'
        totalHardwareDeviceCount = @($before.devices).Count
        projectVersion      = $projectStatus.projectVersion
        projectStatus       = $projectStatus
        requestedOperations = [ordered]@{
            create = $createOperations
            update = $updateOperations
            delete = $deleteOperations
        }
        createPreview = Get-PreviewRecord -Preview $createPreview
        updatePreview = Get-PreviewRecord -Preview $updatePreview
        deletePreview = Get-PreviewRecord -Preview $deletePreview
        negativeCases = $negativeCases
    }
}

function Invoke-Apply {
    $before = Read-HardwareConfig
    Assert-ConnectedFixture $before
    $totalHardwareDeviceCountBefore = @($before.devices).Count
    $rootDeviceCount = $null
    $projectStatus = Get-ObservedProjectStatus

    # --- Group 1: create one isolated Ethernet subnet and one isolated PROFIBUS subnet ---------
    $createOperations = @(
        New-CreateSubnetOperation -OperationId 'create-eth' -Name 'Phase4HarnessEthernet' -NetworkType $script:EthernetNetworkType
        New-CreateSubnetOperation -OperationId 'create-pb' -Name 'Phase4HarnessProfibus' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'Baud500000'
    )
    $createGroup = Invoke-LifecycleGroupAndVerify -GroupName 'create-isolated-subnets' -Operations $createOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    $createdEthernetId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-eth' }).subnetId
    $createdProfibusId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-pb' }).subnetId
    if ([string]::IsNullOrWhiteSpace($createdEthernetId) -or [string]::IsNullOrWhiteSpace($createdProfibusId)) {
        throw 'The create group did not report both created subnet IDs.'
    }

    # --- Group 2: update both created subnets ---------------------------------------------------
    $updateOperations = @(
        # TIA Portal enforces a 24-character subnet name limit (confirmed live: Openness'
        # Subnet.set_Name throws "the subnet name is too long. max 24 character are allowed").
        # The original "...Renamed" literals here were 28 characters and failed against a real
        # project -- these two are 23/22 characters, staying under the limit.
        New-UpdateSubnetOperation -OperationId 'update-eth' -SubnetId $createdEthernetId -Changes @{ name = 'Phase4HarnessEthRenamed' }
        New-UpdateSubnetOperation -OperationId 'update-pb' -SubnetId $createdProfibusId -Changes @{ name = 'Phase4HarnessPbRenamed'; highestAddress = 30; transmissionSpeed = 'Baud1500000' }
    )
    $updateGroup = Invoke-LifecycleGroupAndVerify -GroupName 'update-isolated-subnets' -Operations $updateOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    # --- Group 3: delete the two created (isolated) subnets --------------------------------------
    $deleteIsolatedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-created-eth' -SubnetId $createdEthernetId
        New-DeleteSubnetOperation -OperationId 'delete-created-pb' -SubnetId $createdProfibusId
    )
    $deleteIsolatedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-isolated-subnets' -Operations $deleteIsolatedOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    if ($deleteIsolatedGroup.postReadSubnetIds -contains $createdEthernetId -or $deleteIsolatedGroup.postReadSubnetIds -contains $createdProfibusId) {
        throw 'A deleted isolated subnet ID is still present after delete-isolated-subnets.'
    }

    # --- Group 4: delete the caller-supplied connected Ethernet and PROFIBUS subnets -------------
    $deleteConnectedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-connected-eth' -SubnetId $ConnectedEthernetSubnetId
        New-DeleteSubnetOperation -OperationId 'delete-connected-pb' -SubnetId $ConnectedProfibusSubnetId
    )
    $deleteConnectedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-connected-subnets' -Operations $deleteConnectedOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    if ($deleteConnectedGroup.postReadSubnetIds -contains $ConnectedEthernetSubnetId -or $deleteConnectedGroup.postReadSubnetIds -contains $ConnectedProfibusSubnetId) {
        throw 'A deleted connected subnet ID is still present after delete-connected-subnets.'
    }

    [ordered]@{
        mode                     = 'Apply'
        projectVersion           = $projectStatus.projectVersion
        projectStatus            = $projectStatus
        totalHardwareDeviceCountBefore = $totalHardwareDeviceCountBefore
        createGroup              = $createGroup
        updateGroup              = $updateGroup
        deleteIsolatedGroup      = $deleteIsolatedGroup
        deleteConnectedGroup     = $deleteConnectedGroup
        finalRootDeviceCount     = $rootDeviceCount
        rootDeviceCountEvidenceSource = 'subnet lifecycle results; no independent pre-apply root count'
        rootDeviceCountUnchangedAcrossAllGroups = $true
        expectedProjectStateEffects = 'Connected deletion removes subnet/IO references while exact affected devices/nodes remain. Fresh complete node inventory checks those identities/relationships after each group. Connected deletions are not restored by this script.'
    }
}

# --- Main --------------------------------------------------------------------------------------

$candidate = Assert-FrozenCandidate -ExpectedCommit $ExpectedCommit -ExpectedTree $ExpectedTree `
    -ExpectedHarnessSha256 $ExpectedHarnessSha256 -HarnessPath $PSCommandPath -ExpectedSharedHelperSha256 $ExpectedSharedHelperSha256
$evidence = $null
try {
    Connect-McpHost | Out-Null
    $evidence = switch ($Mode) {
        'Inventory' { Invoke-Inventory }
        'Preview'   { Invoke-Preview }
        'Apply'     { Invoke-Apply }
    }
}
finally {
    Stop-McpHost
}

if ($null -eq $evidence) {
    throw 'The selected mode produced no evidence.'
}

$evidence['mode'] = $Mode
$evidence['requestedProjectPath'] = $ProjectPath
$evidence['serverCommit'] = Get-ServerCommit
$evidence['testedCommit'] = $candidate.testedCommit
$evidence['testedTree'] = $candidate.testedTree
$evidence['testedHarnessSha256'] = $candidate.testedHarnessSha256
$evidence['testedSharedHelperSha256'] = $candidate.testedSharedHelperSha256
$evidence['accessMode'] = $AccessMode
$evidence['elicitationCount'] = $script:ElicitationCount
$evidence['generatedAtUtc'] = (Get-Date).ToUniversalTime().ToString('o')

$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts/live-network-phase4'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactName = "$timestamp-$($Mode.ToLowerInvariant()).json"
$artifactPath = Join-Path $artifactRoot $artifactName
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)
