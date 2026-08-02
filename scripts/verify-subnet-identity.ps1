#Requires -Version 5.1
<#
.SYNOPSIS
    READ-ONLY probe: does TIA Portal V21 Openness expose a stable unique identity for a Subnet?

.DESCRIPTION
    The Network Operations Phase 2 plan locks `SubnetInfo.SubnetId` and mandates matching subnets
    by ordinal `subnetId`. Reflecting Siemens.Engineering.Base.dll (V21) shows `Subnet` exposes
    only Name, NetType, TypeIdentifier, Nodes, IoSystems and Parent -- no SubnetId CLR property.

    But Openness objects also carry DYNAMIC attributes reachable through GetAttributeInfos() /
    GetAttribute(), which reflection alone cannot enumerate. This script answers the only question
    that decides the design: on a real project, is there any readable Subnet attribute that is
    unique and stable enough to serve as the write selector?

    It attaches to an ALREADY-RUNNING TIA Portal with the project open, reads, and detaches.

    THIS SCRIPT NEVER WRITES. It calls only GetAttributeInfos/GetAttribute and property getters.
    It never calls SetAttribute, Create, Delete, Save, or CloseProject. Detaching leaves your
    TIA Portal session and project exactly as it found them.

.PARAMETER ProjectPath
    Optional. Absolute path to the .ap21 file, used to pick the right instance when several
    TIA Portal processes are running. Omit it when only one is open.

.PARAMETER OpennessDir
    Optional. Folder holding Siemens.Engineering.Base.dll. Defaults to the same resolution order
    the worker's AssemblyResolver uses: $env:TiaPortalV21Dir, then $env:TiaPortalLocation, then
    the HKLM InstalledApps registry key, then the standard install path.

.PARAMETER ReportPath
    Optional. Where to write the full findings. Defaults to .superpowers/sdd/subnet-identity-probe.txt

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/verify-subnet-identity.ps1

.NOTES
    MUST run under Windows PowerShell 5.1 (powershell.exe), NOT PowerShell 7 (pwsh.exe).
    The Openness assemblies target .NET Framework 4.8 and do not load into the .NET 8 runtime
    that PowerShell 7 uses. This is why this script's #Requires differs from the repo's other
    live-test scripts, which only pipe JSON to the worker executable and never load Siemens DLLs.

    TIA Portal will show an Openness access-confirmation dialog on attach unless your account is
    in the "Siemens TIA Openness" Windows group. Accept it to let the probe read.
#>
[CmdletBinding()]
param(
    [string] $ProjectPath,
    [string] $OpennessDir,
    [string] $ReportPath = ".superpowers/sdd/subnet-identity-probe.txt"
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw "Run this under Windows PowerShell 5.1 (powershell.exe). Detected PSEdition '$($PSVersionTable.PSEdition)'. The Openness assemblies are .NET Framework 4.8 and will not load in PowerShell 7."
}

$script:Lines = @()
function Write-Both {
    param([string] $Text = "")
    $script:Lines += $Text
    Write-Host $Text
}

# --- Resolve the Openness directory the same way TiaMcpServer.OpennessWorker/Openness/AssemblyResolver.cs does ---
function Resolve-OpennessDir {
    param([string] $Explicit)

    $suffix = "PublicAPI\V21\net48"
    $candidates = @()
    if ($Explicit)                  { $candidates += $Explicit.Trim().Trim('"') }
    if ($env:TiaPortalV21Dir)       { $candidates += $env:TiaPortalV21Dir.Trim().Trim('"') }
    if ($env:TiaPortalLocation)     { $candidates += (Join-Path $env:TiaPortalLocation.Trim().Trim('"') $suffix) }

    $regKey = "SOFTWARE\Siemens\Automation\InstalledApps\Totally Integrated Automation Portal V21"
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        try {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            $key = $base.OpenSubKey($regKey)
            if ($key) {
                $installPath = $key.GetValue("INSTALLPATH")
                if ($installPath) { $candidates += (Join-Path $installPath.Trim().Trim('"') $suffix) }
            }
        } catch { }
    }

    $candidates += "C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"

    foreach ($c in $candidates) {
        if ((Test-Path (Join-Path $c "Siemens.Engineering.Base.dll")) -and
            (Test-Path (Join-Path $c "Siemens.Engineering.Step7.dll"))) {
            return $c
        }
    }

    throw "TIA Portal V21 Openness assemblies not found. Checked: $($candidates -join '; '). Pass -OpennessDir explicitly."
}

$dir = Resolve-OpennessDir -Explicit $OpennessDir
Write-Both "Openness directory: $dir"

# Load eagerly from the LoadFrom context, which already probes this same folder for dependencies.
#
# Deliberately NO AppDomain.AssemblyResolve handler: a PowerShell ScriptBlock used as that
# delegate re-enters itself (running the handler needs assemblies, which raises the event again)
# and the process dies with StackOverflowException. Eager loading avoids the event entirely.
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $dir "Siemens.Engineering.Base.dll"))
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $dir "Siemens.Engineering.Step7.dll"))

# --- Attach to the running TIA Portal (read-only) ---
$processes = @([Siemens.Engineering.TiaPortal]::GetProcesses())
if ($processes.Count -eq 0) {
    throw "No running TIA Portal process found. Open TIA Portal V21 with your sandbox project, then rerun."
}

$target = $null
if ($ProjectPath) {
    $target = $processes | Where-Object { $_.ProjectPath -and $_.ProjectPath.FullName -eq (Resolve-Path $ProjectPath).Path } | Select-Object -First 1
    if (-not $target) { throw "No running TIA Portal has '$ProjectPath' open. Open projects: $(($processes | ForEach-Object { $_.ProjectPath }) -join '; ')" }
} else {
    $withProject = @($processes | Where-Object { $_.ProjectPath })
    if ($withProject.Count -eq 0) { throw "A TIA Portal is running but has no project open. Open your sandbox project and rerun." }
    if ($withProject.Count -gt 1) { throw "Several TIA Portal instances have projects open. Pass -ProjectPath to choose: $(($withProject | ForEach-Object { $_.ProjectPath }) -join '; ')" }
    $target = $withProject[0]
}

Write-Both "Attaching (read-only) to PID $($target.Id): $($target.ProjectPath)"
$portal = $target.Attach()

try {
    $project = @($portal.Projects)[0]
    if (-not $project) { throw "Attached, but the portal exposes no project." }
    Write-Both "Project: $($project.Name)"
    Write-Both ""

    # --- Helper: dump every dynamic attribute an Openness object exposes ---
    function Get-AttributeDump {
        param($EngineeringObject)

        $rows = @()
        try {
            $infos = $EngineeringObject.GetAttributeInfos()
        } catch {
            return ,@([pscustomobject]@{ Name = "<GetAttributeInfos failed>"; Access = ""; Value = $_.Exception.Message })
        }

        foreach ($info in $infos) {
            # Compared as strings on purpose: a [Siemens.Engineering...] type literal here would
            # bind at runtime and fail confusingly if the enum ever moves assemblies.
            $value = ""
            $access = [string]$info.AccessMode
            if ($access -eq 'Read' -or $access -eq 'ReadWrite') {
                try {
                    $raw = $EngineeringObject.GetAttribute($info.Name)
                    $value = if ($null -eq $raw) { "<null>" } else { [string]$raw }
                } catch {
                    $value = "<read failed: $($_.Exception.Message)>"
                }
            } else {
                $value = "<not readable>"
            }
            $rows += [pscustomobject]@{ Name = $info.Name; Access = [string]$info.AccessMode; Value = $value }
        }
        return ,$rows
    }

    # ============================ SUBNETS ============================
    Write-Both "=============================================================="
    Write-Both " SUBNETS  (the decisive evidence)"
    Write-Both "=============================================================="

    $subnets = @($project.Subnets)
    Write-Both "Project-level subnet count: $($subnets.Count)"
    Write-Both ""

    $subnetNames = @()
    $attributeNameSets = @()

    foreach ($subnet in $subnets) {
        $subnetNames += $subnet.Name
        Write-Both "--- Subnet '$($subnet.Name)' ---"
        Write-Both "  CLR properties: Name='$($subnet.Name)'  NetType='$($subnet.NetType)'  TypeIdentifier='$($subnet.TypeIdentifier)'"
        Write-Both "  Nodes: $(@($subnet.Nodes).Count)   IoSystems: $(@($subnet.IoSystems).Count)"
        Write-Both "  Dynamic attributes:"

        $dump = Get-AttributeDump -EngineeringObject $subnet
        $attributeNameSets += ,@($dump | ForEach-Object { $_.Name })
        foreach ($row in $dump) {
            Write-Both ("    {0,-32} [{1,-9}] = {2}" -f $row.Name, $row.Access, $row.Value)
        }

        # The plan's assumed selector, probed explicitly.
        foreach ($candidate in @("SubnetId", "Id", "Identifier", "UId", "Uid", "SubnetUId", "ObjectId")) {
            try {
                $probe = $subnet.GetAttribute($candidate)
                Write-Both "    PROBE GetAttribute('$candidate') -> '$probe'"
            } catch {
                Write-Both "    PROBE GetAttribute('$candidate') -> THROWS: $($_.Exception.GetType().Name)"
            }
        }
        Write-Both ""
    }

    # ============================ NODES ============================
    Write-Both "=============================================================="
    Write-Both " NODES  (NodeId is a real CLR property -- confirm it is populated and unique)"
    Write-Both "=============================================================="

    $allNodeIds = @()
    foreach ($subnet in $subnets) {
        foreach ($node in @($subnet.Nodes)) {
            $allNodeIds += $node.NodeId
            $owner = try { $node.Parent.ToString() } catch { "<unreadable>" }
            Write-Both "  Node '$($node.Name)'  NodeId='$($node.NodeId)'  NodeType='$($node.NodeType)'  ConnectedSubnet='$(if ($node.ConnectedSubnet) { $node.ConnectedSubnet.Name } else { '<none>' })'"
            Write-Both "    Parent: $owner"
        }
    }
    Write-Both ""
    if (@($subnets).Count -gt 0 -and @($subnets[0].Nodes).Count -gt 0) {
        Write-Both "  Full attribute dump for the first node (to see what else could identify it):"
        foreach ($row in (Get-AttributeDump -EngineeringObject @($subnets[0].Nodes)[0])) {
            Write-Both ("    {0,-32} [{1,-9}] = {2}" -f $row.Name, $row.Access, $row.Value)
        }
        Write-Both ""
    }

    # ============================ IO SYSTEMS ============================
    Write-Both "=============================================================="
    Write-Both " IO SYSTEMS  (Number is a real CLR property, Int32 non-nullable)"
    Write-Both "=============================================================="

    $ioPairs = @()
    foreach ($subnet in $subnets) {
        foreach ($io in @($subnet.IoSystems)) {
            $ioPairs += "$($subnet.Name)/$($io.Number)"
            Write-Both "  IoSystem '$($io.Name)'  Number=$($io.Number)  Subnet='$(if ($io.Subnet) { $io.Subnet.Name } else { '<none>' })'  ConnectedIoDevices=$(@($io.ConnectedIoDevices).Count)"
        }
    }
    Write-Both ""

    # ============================ VERDICT ============================
    Write-Both "=============================================================="
    Write-Both " VERDICT"
    Write-Both "=============================================================="

    $subnetIdWorks = $false
    foreach ($subnet in $subnets) {
        try {
            $v = $subnet.GetAttribute("SubnetId")
            if ($v -and [string]$v -ne "") { $subnetIdWorks = $true }
        } catch { }
    }

    Write-Both "1. Subnet exposes a usable 'SubnetId': $(if ($subnetIdWorks) { 'YES -- the plan is implementable as written' } else { 'NO -- the plan premise is false' })"

    $dupNames = @($subnetNames | Group-Object | Where-Object { $_.Count -gt 1 })
    Write-Both "2. Subnet names unique in this project: $(if ($dupNames.Count -eq 0) { "YES ($($subnetNames.Count) subnets)" } else { "NO -- duplicates: $(($dupNames | ForEach-Object { $_.Name }) -join ', ')" })"

    $emptyNodeIds = @($allNodeIds | Where-Object { -not $_ })
    $dupNodeIds = @($allNodeIds | Where-Object { $_ } | Group-Object | Where-Object { $_.Count -gt 1 })
    Write-Both "3. NodeId populated for every node: $(if ($emptyNodeIds.Count -eq 0) { "YES ($($allNodeIds.Count) nodes)" } else { "NO -- $($emptyNodeIds.Count) empty" })"
    Write-Both "4. NodeId unique project-wide: $(if ($dupNodeIds.Count -eq 0) { 'YES' } else { "NO -- repeated: $(($dupNodeIds | ForEach-Object { $_.Name }) -join ', ') (may still be unique per device)" })"

    if ($attributeNameSets.Count -gt 0) {
        $common = $attributeNameSets[0]
        foreach ($set in $attributeNameSets) { $common = @($common | Where-Object { $set -contains $_ }) }
        Write-Both "5. Attributes present on EVERY subnet (identity candidates): $(if ($common.Count) { $common -join ', ' } else { '<none>' })"
    }

    Write-Both ""
    Write-Both "If (1) is NO and (2) is YES, the workable fix is to key subnets by Name -- TIA enforces"
    Write-Both "unique subnet names -- keeping the plan's fail-closed exactly-one-match rule intact."
}
finally {
    Write-Both ""
    Write-Both "Detaching. No write of any kind was performed; the project is untouched."
    if ($portal) { $portal.Dispose() }

    $reportDir = Split-Path -Parent $ReportPath
    if ($reportDir -and -not (Test-Path $reportDir)) { New-Item -ItemType Directory -Path $reportDir -Force | Out-Null }
    $script:Lines | Out-File -FilePath $ReportPath -Encoding utf8
    Write-Host ""
    Write-Host "Full findings written to: $ReportPath"
}
