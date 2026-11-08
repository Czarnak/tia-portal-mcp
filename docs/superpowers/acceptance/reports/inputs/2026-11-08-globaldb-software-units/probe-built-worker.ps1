param(
    [int]$PortalProcessId = 43340,
    [string]$ProjectPath = 'C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\MixOfEverything\MixOfEverything.ap21',
    [string]$WorkerPath = (Join-Path $PSScriptRoot '..\..\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'built-worker')
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$wirePath=Join-Path $EvidenceDirectory 'worker-wire.jsonl'
$nativePath=Join-Path $EvidenceDirectory 'native-readback.jsonl'
if((Test-Path -LiteralPath $wirePath) -or (Test-Path -LiteralPath $nativePath)) { throw 'Refusing to overwrite earlier evidence.' }
function Native-Record($data) {
    $line=$data | ConvertTo-Json -Depth 20 -Compress
    Add-Content -LiteralPath $nativePath -Value $line -Encoding UTF8
    Write-Output $line
}
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=[IO.Path]::GetFullPath($WorkerPath)
$start.Arguments='--access-mode read-write'
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
$worker=New-Object Diagnostics.Process
$worker.StartInfo=$start
$script:identity=$null
$script:wireSequence=0
function Invoke-Worker([hashtable]$request) {
    $request.protocolVersion='project-tree-v3'
    if($null -ne $script:identity) { $request.expectedSessionIdentity=$script:identity }
    $json=$request | ConvertTo-Json -Depth 15 -Compress
    $worker.StandardInput.WriteLine($json)
    $worker.StandardInput.Flush()
    $pending=$worker.StandardOutput.ReadLineAsync()
    if(!$pending.Wait(300000)) { throw 'Worker timeout; inspect native state before retrying.' }
    $line=$pending.Result
    if($null -eq $line) { throw 'Worker ended without response.' }
    $response=$line | ConvertFrom-Json
    Add-Content -LiteralPath $wirePath -Encoding UTF8 -Value (@{sequence=$script:wireSequence;request=$request;response=$response} | ConvertTo-Json -Depth 30 -Compress)
    $script:wireSequence++
    if($null -ne $response.sessionIdentity) {
        if($response.sessionIdentity.portalProcessId -ne $PortalProcessId -or $response.sessionIdentity.projectPath -ne $ProjectPath) { throw 'Worker response identity differs from authorized disposable project.' }
        $script:identity=$response.sessionIdentity
    }
    Write-Host ($request.method + ': success=' + $response.success + ' category=' + $response.failureCategory)
    if(!$response.success) { throw ($response | ConvertTo-Json -Depth 8 -Compress) }
    return $response
}
$portal=$null
try {
    $null=$worker.Start()
    $stderr=$worker.StandardError.ReadToEndAsync()
    $null=Invoke-Worker @{method='hello'}
    $null=Invoke-Worker @{method='select_portal_project';projectPath=$ProjectPath;portalProcessId=$PortalProcessId}
    $null=Invoke-Worker @{method='get_project_status';projectPath=$ProjectPath}
    $nestedPath='PLC_1/Units/Unit_1/Blocks/Issue221FixedGroup'
    $null=Invoke-Worker @{method='create_block_group';projectPath=$ProjectPath;blockPath=$nestedPath}
    $cases=@(
        @{scope='root';blockPath='PLC_1/Blocks/Issue221FixedRoot'},
        @{scope='unit';blockPath='PLC_1/Units/Unit_1/Blocks/Issue221FixedUnit'},
        @{scope='unitNested';blockPath=($nestedPath+'/Issue221FixedNested')}
    )
    foreach($case in $cases) {
        $created=Invoke-Worker @{method='create_block';projectPath=$ProjectPath;blockPath=$case.blockPath;blockType='GlobalDB'}
        $read=Invoke-Worker @{method='get_block_content';projectPath=$ProjectPath;blockPath=$case.blockPath;format='xml'}
        $compile=Invoke-Worker @{method='compile_check';projectPath=$ProjectPath;blockPath=$case.blockPath;plcName='PLC_1'}
        $case.created=$created.payload | ConvertFrom-Json
        $case.read=$read.payload
        $case.compile=$compile.payload | ConvertFrom-Json
    }
} finally {
    if(!$worker.HasExited) {
        $worker.StandardInput.Close()
        if(!$worker.WaitForExit(15000)) { $worker.Kill() }
    }
    if($null -ne $stderr) { $stderr.Result | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'worker-stderr.log') -Encoding UTF8 }
    $worker.Dispose()
}
# Independent native reads assert actual state after the built worker has detached.
[Reflection.Assembly]::LoadFrom('C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll') | Out-Null
[Reflection.Assembly]::LoadFrom('C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Step7.dll') | Out-Null
function Get-ServiceInstance($target,[type]$serviceType) {
    return [Siemens.Engineering.IEngineeringServiceProvider].GetMethod('GetService').MakeGenericMethod($serviceType).Invoke($target,$null)
}
try {
    $process=@([Siemens.Engineering.TiaPortal]::GetProcesses() | Where-Object Id -eq $PortalProcessId)
    if($process.Count -ne 1) { throw 'Expected Portal missing.' }
    $portal=$process[0].Attach()
    $projects=@($portal.Projects | Where-Object { $_.Path.FullName -eq $ProjectPath })
    if($projects.Count -ne 1 -or @($portal.Projects).Count -ne 1) { throw 'Expected sole disposable project missing.' }
    $project=$projects[0]
    $device=$project.Devices.Find('S7-1500/ET200MP station_1')
    if($null -eq $device) { throw 'Expected disposable PLC device missing.' }
    $cpuItems=@(foreach($item in $device.DeviceItems) {
        $container=Get-ServiceInstance $item ([Siemens.Engineering.HW.Features.SoftwareContainer])
        if($null -ne $container -and $container.Software.Name -eq 'PLC_1') { $item }
    })
    if($cpuItems.Count -ne 1) { throw 'Expected unique CPU item.' }
    $cpu=$cpuItems[0]
    $plc=(Get-ServiceInstance $cpu ([Siemens.Engineering.HW.Features.SoftwareContainer])).Software
    $unit=(Get-ServiceInstance $plc ([Siemens.Engineering.SW.Units.PlcUnitProvider])).UnitGroup.Units.Find('Unit_1')
    $group=$unit.BlockGroup.Groups.Find('Issue221FixedGroup')
    Native-Record @{kind='setup';utc=[DateTime]::UtcNow.ToString('o');pid=$PortalProcessId;project=$ProjectPath;cpuName=$cpu.Name;cpuTypeIdentifier=$cpu.GetAttribute('TypeIdentifier');workerSha256=(Get-FileHash -LiteralPath $WorkerPath -Algorithm SHA256).Hash}
    $locations=@(
        @{scope='root';group=$plc.BlockGroup;name='Issue221FixedRoot'},
        @{scope='unit';group=$unit.BlockGroup;name='Issue221FixedUnit'},
        @{scope='unitNested';group=$group;name='Issue221FixedNested'}
    )
    foreach($location in $locations) {
        $block=$location.group.Blocks.Find($location.name)
        if($null -eq $block) { throw ('Built worker did not create '+$location.name) }
        $memoryLayout=$block.GetAttribute('MemoryLayout')
        $compiled=(Get-ServiceInstance $block ([Siemens.Engineering.Compiler.ICompilable])).Compile()
        Native-Record @{kind='readback';scope=$location.scope;name=$block.Name;memoryLayout=$memoryLayout.ToString();compileState=$compiled.State.ToString();errors=$compiled.ErrorCount;warnings=$compiled.WarningCount}
        if($memoryLayout.ToString() -ne 'Optimized' -or $compiled.ErrorCount -ne 0 -or $compiled.State.ToString() -ne 'Success') { throw 'Built worker result failed native optimized/compile check.' }
    }
} finally {
    if($null -ne $portal -and $null -ne $project) {
        $exclusive=$portal.ExclusiveAccess('Issue221 remove built-worker probe objects')
        try {
            $transaction=$exclusive.Transaction($project,'Issue221 cleanup built-worker objects')
            try {
                if($null -ne $plc) {
                    $rootBlock=$plc.BlockGroup.Blocks.Find('Issue221FixedRoot')
                    if($null -ne $rootBlock) { $rootBlock.Delete() }
                }
                if($null -ne $unit) {
                    $unitBlock=$unit.BlockGroup.Blocks.Find('Issue221FixedUnit')
                    if($null -ne $unitBlock) { $unitBlock.Delete() }
                    $createdGroup=$unit.BlockGroup.Groups.Find('Issue221FixedGroup')
                    if($null -ne $createdGroup) { $createdGroup.Delete() }
                }
                $transaction.CommitOnDispose()
            } finally { $transaction.Dispose() }
        } finally { $exclusive.Dispose() }
        Native-Record @{kind='cleanup';projectSaved=$false;probeNamesAbsent=($null -eq $plc.BlockGroup.Blocks.Find('Issue221FixedRoot') -and $null -eq $unit.BlockGroup.Blocks.Find('Issue221FixedUnit') -and $null -eq $unit.BlockGroup.Groups.Find('Issue221FixedGroup'))}
    }
    if($null -ne $portal) { $portal.Dispose() }
}

