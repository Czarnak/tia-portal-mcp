[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $entries = @($archive.Entries |
        Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName.Replace('\', '/') })
}
finally {
    $archive.Dispose()
}

$canonicalToolPrefix = 'tools/net10.0/any/'
$canonicalPrefix = $canonicalToolPrefix + 'openness-worker/'
$ambiguousPathEntries = @($entries | Where-Object {
    $segments = $_ -split '/'
    return $segments -contains '.' -or $segments -contains '..'
})
if ($ambiguousPathEntries.Count -gt 0) {
    throw "NuGet package contains $($ambiguousPathEntries.Count) file entries with ambiguous dot path segments; tool files must use canonical prefix '$canonicalToolPrefix'."
}

$workerEntries = @($entries | Where-Object { $_ -imatch '(^|/)openness-worker/' })
$canonicalEntries = @($workerEntries | Where-Object {
    $_.StartsWith($canonicalPrefix, [System.StringComparison]::Ordinal)
})
$nonCanonicalEntries = @($workerEntries | Where-Object {
    -not $_.StartsWith($canonicalPrefix, [System.StringComparison]::Ordinal)
})

if ($canonicalEntries.Count -eq 0) {
    throw "NuGet package has no canonical '$canonicalPrefix' worker subtree."
}

if ($nonCanonicalEntries.Count -gt 0) {
    throw "NuGet package contains $($nonCanonicalEntries.Count) non-canonical worker file entries; expected canonical prefix '$canonicalPrefix'."
}

$toolEntries = @($entries | Where-Object { $_ -imatch '^tools/' })
$nonCanonicalToolEntries = @($toolEntries | Where-Object {
    -not $_.StartsWith($canonicalToolPrefix, [System.StringComparison]::Ordinal)
})
if ($nonCanonicalToolEntries.Count -gt 0) {
    throw "NuGet package contains $($nonCanonicalToolEntries.Count) non-canonical tool file entries; expected canonical prefix '$canonicalToolPrefix'."
}

$duplicateEntries = @($canonicalEntries |
    Group-Object |
    Where-Object { $_.Count -ne 1 } |
    Select-Object -ExpandProperty Name)
if ($duplicateEntries.Count -gt 0) {
    throw "NuGet package contains $($duplicateEntries.Count) duplicate canonical worker file paths."
}

$requiredFiles = @(
    'TiaMcpServer.OpennessWorker.exe',
    'TiaMcpServer.OpennessWorker.exe.config',
    'TiaMcpServer.Contracts.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.IO.Pipelines.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Text.Encodings.Web.dll',
    'System.Text.Json.dll',
    'System.Threading.Tasks.Extensions.dll',
    'System.ValueTuple.dll',
    'TiaMcpServer.Contracts.pdb',
    'TiaMcpServer.OpennessWorker.pdb'
)

$requiredFileNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
foreach ($requiredFile in $requiredFiles) {
    [void] $requiredFileNames.Add($requiredFile)
}

foreach ($requiredFile in $requiredFiles) {
    $expectedEntry = $canonicalPrefix + $requiredFile
    $matches = @($canonicalEntries | Where-Object {
        [string]::Equals($_, $expectedEntry, [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -ne 1) {
        throw "NuGet package must contain exactly one '$expectedEntry'; found $($matches.Count)."
    }
}

$workerRuntimeConfigs = @($workerEntries | Where-Object {
    $_.EndsWith('runtimeconfig.json', [System.StringComparison]::OrdinalIgnoreCase)
})
if ($workerRuntimeConfigs.Count -gt 0) {
    throw "NuGet package must not include worker runtimeconfig files; found $($workerRuntimeConfigs.Count)."
}

$siemensAssemblies = @($entries | Where-Object {
    [System.IO.Path]::GetFileName($_) -match '^Siemens\.Engineering.*\.dll$'
})
if ($siemensAssemblies.Count -gt 0) {
    throw "NuGet package must not include Siemens Openness assemblies; found $($siemensAssemblies.Count)."
}

$unexpectedWorkerFiles = @($canonicalEntries | Where-Object {
    $relativePath = $_.Substring($canonicalPrefix.Length)
    return -not $requiredFileNames.Contains($relativePath)
})
if ($unexpectedWorkerFiles.Count -gt 0) {
    throw "NuGet package contains $($unexpectedWorkerFiles.Count) unexpected canonical worker files."
}

Write-Host "Verified ${resolvedPackage}: one canonical worker subtree, $($requiredFiles.Count) required files, no worker runtimeconfig, no Siemens DLLs."
