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
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
}
finally {
    $archive.Dispose()
}

$canonicalPrefix = 'tools/net8.0/any/openness-worker/'
$workerEntries = @($entries | Where-Object { $_ -match '(^|/)openness-worker/' })
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
    throw "NuGet package contains non-canonical worker subtree entries: $($nonCanonicalEntries -join ', ')."
}

$duplicateEntries = @($canonicalEntries |
    Group-Object |
    Where-Object { $_.Count -ne 1 } |
    Select-Object -ExpandProperty Name)
if ($duplicateEntries.Count -gt 0) {
    throw "NuGet package contains duplicate canonical worker entries: $($duplicateEntries -join ', ')."
}

$requiredFiles = @(
    'TiaMcpServer.OpennessWorker.exe',
    'TiaMcpServer.OpennessWorker.exe.config',
    'TiaMcpServer.Contracts.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'System.Buffers.dll',
    'System.IO.Pipelines.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Text.Encodings.Web.dll',
    'System.Text.Json.dll',
    'System.Threading.Tasks.Extensions.dll'
)

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
    throw "NuGet package must not include worker runtimeconfig files: $($workerRuntimeConfigs -join ', ')."
}

$siemensAssemblies = @($entries | Where-Object {
    [System.IO.Path]::GetFileName($_) -match '^Siemens\.Engineering.*\.dll$'
})
if ($siemensAssemblies.Count -gt 0) {
    throw "NuGet package must not include Siemens Openness assemblies: $($siemensAssemblies -join ', ')."
}

Write-Host "Verified ${resolvedPackage}: one canonical worker subtree, $($requiredFiles.Count) required files, no worker runtimeconfig, no Siemens DLLs."
