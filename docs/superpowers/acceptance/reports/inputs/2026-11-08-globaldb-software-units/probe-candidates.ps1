param(
    [int]$PortalProcessId = 43340,
    [string]$ProjectPath = 'C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\MixOfEverything\MixOfEverything.ap21',
    [string]$SourceGeneratorPath = (Join-Path $PSScriptRoot '..\..\TiaMcpServer.OpennessWorker\Openness\Block\BlockSourceGenerator.cs'),
    [string]$EvidenceDirectory = (Join-Path $PSScriptRoot 'native-candidates')
)
$ErrorActionPreference = 'Stop'
$opennessDirectory = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
[System.Reflection.Assembly]::LoadFrom((Join-Path $opennessDirectory 'Siemens.Engineering.Base.dll')) | Out-Null
[System.Reflection.Assembly]::LoadFrom((Join-Path $opennessDirectory 'Siemens.Engineering.Step7.dll')) | Out-Null
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$tracePath = Join-Path $EvidenceDirectory 'native-candidates.jsonl'
if (Test-Path -LiteralPath $tracePath) { throw "Evidence path already exists: $tracePath" }
function Record($value) {
    $value | ConvertTo-Json -Depth 15 -Compress | Add-Content -LiteralPath $tracePath -Encoding UTF8
    $value | ConvertTo-Json -Depth 4 -Compress | Write-Output
}
function Get-ServiceInstance($engineeringObject, [type]$serviceType) {
    $method = [Siemens.Engineering.IEngineeringServiceProvider].GetMethod('GetService').MakeGenericMethod($serviceType)
    return $method.Invoke($engineeringObject, $null)
}
function Find-Plc($items) {
    foreach ($item in $items) {
        $container = Get-ServiceInstance $item ([Siemens.Engineering.HW.Features.SoftwareContainer])
        if ($null -ne $container -and $container.Software -is [Siemens.Engineering.SW.PlcSoftware] -and $container.Software.Name -eq 'PLC_1') {
            $container.Software
        }
        Find-Plc $item.DeviceItems
    }
}
$source = Get-Content -LiteralPath $SourceGeneratorPath -Raw
$methodStart = $source.IndexOf('private static string GenerateGlobalDbXml')
$fragment = $source.Substring($methodStart)
$template = [regex]::Match($fragment, '(?s)return \$@"(.*?)";').Groups[1].Value.Replace('""','"')
if (!$template.Contains('<MemoryLayout>Optimized</MemoryLayout>')) { throw 'Expected unchanged baseline GlobalDB generator.' }
$template = $template.Replace('{EngineeringVersion}', 'V21')
$portal = $null
$exclusive = $null
$createdBlocks = New-Object System.Collections.Generic.List[object]
$nestedGroup = $null
try {
    $processes = @([Siemens.Engineering.TiaPortal]::GetProcesses() | Where-Object Id -eq $PortalProcessId)
    if ($processes.Count -ne 1) { throw 'Expected exact Portal PID not found.' }
    $portal = $processes[0].Attach()
    $projects = @($portal.Projects | Where-Object { $_.Path.FullName -eq $ProjectPath })
    if ($projects.Count -ne 1 -or @($portal.Projects).Count -ne 1) { throw 'Expected sole disposable project not found.' }
    $project = $projects[0]
    $plcs = @(foreach($device in $project.Devices) { Find-Plc $device.DeviceItems })
    if ($plcs.Count -ne 1) { throw 'Expected unique PLC_1.' }
    $plc = $plcs[0]
    $provider = Get-ServiceInstance $plc ([Siemens.Engineering.SW.Units.PlcUnitProvider])
    $unit = $provider.UnitGroup.Units.Find('Unit_1')
    if ($null -eq $unit) { throw 'Expected existing Unit_1.' }
    Record @{kind='setup';utc=[DateTime]::UtcNow.ToString('o');pid=$PortalProcessId;project=$project.Path.FullName;sourceSha256=(Get-FileHash -LiteralPath $SourceGeneratorPath -Algorithm SHA256).Hash;opennessVersion=[Siemens.Engineering.TiaPortal].Assembly.GetName().Version.ToString();projectWasModified=$project.IsModified}
    $exclusive = $portal.ExclusiveAccess('Issue 221 disposable GlobalDB XML comparison')
    if ($null -ne $unit.BlockGroup.Groups.Find('Issue221Probe')) { throw 'Probe folder already exists; refusing to reuse.' }
    $transaction = $exclusive.Transaction($project, 'Issue221 create isolated nested folder')
    try {
        $nestedGroup = $unit.BlockGroup.Groups.Create('Issue221Probe')
        $transaction.CommitOnDispose()
    } finally { $transaction.Dispose() }
    $groups = @(
        @{Label='root'; Group=$plc.BlockGroup},
        @{Label='unit'; Group=$unit.BlockGroup},
        @{Label='unitNested'; Group=$nestedGroup}
    )
    foreach($entry in $groups) {
        foreach($variant in @('plain','omitted','readOnly')) {
            if ($project.Path.FullName -ne $ProjectPath) { throw 'Project identity changed.' }
            $name = 'Issue221_' + $entry.Label + '_' + $variant
            if ($null -ne $entry.Group.Blocks.Find($name)) { throw "Probe block already exists: $name" }
            $xml = $template.Replace('{blockName}', $name)
            if ($variant -eq 'omitted') { $xml = $xml.Replace('<MemoryLayout>Optimized</MemoryLayout>', '') }
            if ($variant -eq 'readOnly') { $xml = $xml.Replace('<MemoryLayout>Optimized</MemoryLayout>', '<MemoryLayout ReadOnly="true">Optimized</MemoryLayout>') }
            $inputPath = Join-Path $EvidenceDirectory ($name + '.xml')
            [IO.File]::WriteAllText($inputPath,$xml,(New-Object Text.UTF8Encoding($false)))
            $record = [ordered]@{kind='case';scope=$entry.Label;variant=$variant;name=$name;input=(Split-Path $inputPath -Leaf);import='notAttempted';returnedCount=$null;present=$false;compile=$null;exportedMemoryLayout=$null;exception=$null}
            $transaction = $null
            try {
                $transaction = $exclusive.Transaction($project, "Issue221 import $name")
                $blocks = @($entry.Group.Blocks.Import((New-Object IO.FileInfo($inputPath)),[Siemens.Engineering.ImportOptions]::None))
                $record.returnedCount = $blocks.Count
                if ($blocks.Count -ne 1 -or $blocks[0].Name -ne $name) { throw 'Unexpected import return identity.' }
                $transaction.CommitOnDispose()
                $record.import = 'returned'
            } catch {
                $record.import = 'threw'
                $record.exception = $_.Exception.ToString()
            } finally {
                if ($null -ne $transaction) { $transaction.Dispose() }
            }
            $block = $entry.Group.Blocks.Find($name)
            $record.present = $null -ne $block
            if ($null -ne $block) {
                $createdBlocks.Add($block)
                try {
                    $compilable = Get-ServiceInstance $block ([Siemens.Engineering.Compiler.ICompilable])
                    $compiled = $compilable.Compile()
                    $record.compile = @{state=$compiled.State.ToString();errors=$compiled.ErrorCount;warnings=$compiled.WarningCount}
                    $exportPath = Join-Path $EvidenceDirectory ($name + '.export.xml')
                    $block.Export((New-Object IO.FileInfo($exportPath)),[Siemens.Engineering.ExportOptions]::WithDefaults)
                    [xml]$exportXml = Get-Content -LiteralPath $exportPath -Raw
                    $memory = $exportXml.Document.SelectSingleNode('SW.Blocks.GlobalDB/AttributeList/MemoryLayout')
                    $record.exportedMemoryLayout = if($null -eq $memory) { $null } else { $memory.OuterXml }
                } catch {
                    $record.exception = $_.Exception.ToString()
                }
            }
            Record $record
        }
    }
} finally {
    if ($null -ne $exclusive -and $null -ne $project) {
        $transaction = $exclusive.Transaction($project, 'Issue221 remove only probe-owned blocks and folder')
        try {
            foreach($block in $createdBlocks) { $block.Delete() }
            if($null -ne $nestedGroup) { $nestedGroup.Delete() }
            $transaction.CommitOnDispose()
        } finally { $transaction.Dispose() }
        Record @{kind='cleanup';createdBlocksDeleted=$createdBlocks.Count;nestedFolderDeleted=($null -ne $nestedGroup);projectSaved=$false}
    }
    if ($null -ne $exclusive) { $exclusive.Dispose() }
    if ($null -ne $portal) { $portal.Dispose() }

}

