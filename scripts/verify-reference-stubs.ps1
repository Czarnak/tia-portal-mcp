[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoRestore,
    # Test injection: a staged directory inside the repository inferred from this script.
    [string]$GeneratedReferenceDirectory,
    [switch]$Update
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$artifactNames = @('Siemens.Engineering.Base.dll', 'Siemens.Engineering.Step7.dll')
$temporaryRoot = $null

function Resolve-Directory([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($Path))).ProviderPath
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) { throw "Not a directory: $Path" }
    return [IO.Path]::TrimEndingDirectorySeparator($resolved)
}

function Assert-NoReparsePoint([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Path boundary rejects reparse point: $current" }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-ChildPath([string]$Path, [string]$Boundary) {
    $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Boundary)) + [IO.Path]::DirectorySeparatorChar
    if (-not ([IO.Path]::GetFullPath($Path)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path outside resolved repository/temporary boundary: $Path"
    }
    Assert-NoReparsePoint $Path
}

function Get-SourceHash([string]$RepositoryRoot) {
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($relative in @('Directory.Build.props', 'reference-stubs/Directory.Build.props', 'reference-stubs/Siemens.Engineering.PublicKey.snk')) { $paths.Add($relative) }
    foreach ($project in @('Siemens.Engineering.Base', 'Siemens.Engineering.Step7')) {
        $directory = Resolve-Directory (Join-Path $RepositoryRoot "reference-stubs/$project")
        Assert-ChildPath $directory $RepositoryRoot
        foreach ($file in Get-ChildItem -LiteralPath $directory -File) {
            if ($file.Extension -ceq '.cs' -or $file.Extension -ceq '.csproj') {
                $paths.Add([IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/'))
            }
        }
    }
    $paths.Sort([StringComparer]::Ordinal)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        # Unambiguous framing: UTF-8 slash-relative path + LF + decimal byte length + LF + raw bytes + LF.
        foreach ($relative in $paths) {
            $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
            Assert-ChildPath $path $RepositoryRoot
            $bytes = [IO.File]::ReadAllBytes($path)
            $length = $bytes.Length.ToString([Globalization.CultureInfo]::InvariantCulture)
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes("$relative`n$length`n"))
            $hash.AppendData($bytes)
            $hash.AppendData([byte[]]@(10))
        }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose() }
}

function Assert-Artifact([string]$Path, [string]$ExpectedName, [string]$ExpectedHash) {
    try {
        Assert-NoReparsePoint $Path
        $identity = [Reflection.AssemblyName]::GetAssemblyName($Path)
        $token = [Convert]::ToHexString($identity.GetPublicKeyToken()).ToLowerInvariant()
        if ($identity.Name -cne [IO.Path]::GetFileNameWithoutExtension($ExpectedName) -or $identity.Version.ToString() -cne '21.0.0.0' -or $token -cne '29bfe5fdf4ba5d3b') {
            throw 'assembly identity mismatch (simple name, version or public-key token)'
        }
        $stream = [IO.File]::OpenRead($Path)
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                $metadata = @{}
                $reference = $false
                foreach ($handle in $reader.GetAssemblyDefinition().GetCustomAttributes()) {
                    $attribute = $reader.GetCustomAttribute($handle)
                    if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
                    $member = $reader.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
                    if ($member.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
                    $type = $reader.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$member.Parent)
                    $name = $reader.GetString($type.Namespace) + '.' + $reader.GetString($type.Name)
                    if ($name -ceq 'System.Runtime.CompilerServices.ReferenceAssemblyAttribute') { $reference = $true }
                    if ($name -cne 'System.Reflection.AssemblyMetadataAttribute') { continue }
                    $blob = $reader.GetBlobReader($attribute.Value)
                    if ($blob.ReadUInt16() -ne 1) { throw 'invalid AssemblyMetadata attribute' }
                    $key = $blob.ReadSerializedString()
                    $value = $blob.ReadSerializedString()
                    if ($metadata.ContainsKey($key)) { throw "duplicate assembly metadata $key" }
                    $metadata[$key] = $value
                }
                if (-not $reference) { throw 'not a generated reference assembly' }
                if ($metadata['StubOrigin'] -cne 'tia-portal-mcp') { throw 'StubOrigin mismatch' }
                if ($metadata['StubSourceHash'] -cne $ExpectedHash) { throw "StubSourceHash mismatch; expected $ExpectedHash" }
            } finally { $pe.Dispose() }
        } finally { $stream.Dispose() }
        Write-Output "Verified $ExpectedName source hash $ExpectedHash; PE SHA256 $((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash)"
    } catch { throw "$ExpectedName ($Path): $($_.Exception.Message)" }
}

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed (exit $LASTEXITCODE)" }
}

try {
    $repositoryRoot = Resolve-Directory (Join-Path $PSScriptRoot '..')
    Assert-NoReparsePoint $repositoryRoot
    $targetDirectory = Resolve-Directory (Join-Path $repositoryRoot 'ref')
    Assert-ChildPath $targetDirectory $repositoryRoot
    foreach ($name in $artifactNames) { Assert-ChildPath (Join-Path $targetDirectory $name) $targetDirectory }
    $sourceHash = Get-SourceHash $repositoryRoot
    Write-Output "Canonical source hash: $sourceHash"

    if ($GeneratedReferenceDirectory) {
        $generatedDirectory = Resolve-Directory $GeneratedReferenceDirectory
        Assert-ChildPath $generatedDirectory $repositoryRoot
        if ($generatedDirectory.Equals($targetDirectory, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated reference directory must differ from the target boundary' }
    } else {
        $temporaryRoot = [IO.Directory]::CreateTempSubdirectory('tia-reference-verifier-').FullName
        Assert-ChildPath $temporaryRoot ([IO.Path]::GetTempPath())
        $generatedDirectory = [IO.Directory]::CreateDirectory((Join-Path $temporaryRoot 'generated')).FullName
        $solution = Join-Path $repositoryRoot 'reference-stubs/TiaMcpServer.ReferenceStubs.sln'
        if (-not $NoRestore) { Invoke-Dotnet @('restore', $solution, '-m:1', '/p:UseTiaPortalReferenceStubs=true') }
        $output = Join-Path $temporaryRoot 'build/'
        $intermediate = Join-Path $temporaryRoot 'obj/'
        Invoke-Dotnet @('build', (Join-Path $repositoryRoot 'reference-stubs/Siemens.Engineering.Step7/Siemens.Engineering.Step7.csproj'), '--no-restore', '-m:1', '--configuration', $Configuration, "/p:StubSourceHash=$sourceHash", "/p:OutputPath=$output", "/p:IntermediateOutputPath=$intermediate")
        foreach ($name in $artifactNames) {
            # obj/ref contains compiler-produced reference assemblies; bin contains executable placeholders.
            $reference = [IO.Path]::GetFullPath((Join-Path $intermediate "ref/$name"))
            Assert-ChildPath $reference $temporaryRoot
            Copy-Item -LiteralPath $reference -Destination (Join-Path $generatedDirectory $name)
        }
        Invoke-Dotnet @('build', (Join-Path $repositoryRoot 'reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj'), '--no-restore', '-m:1', '--configuration', $Configuration, '/p:UseTiaPortalReferenceStubs=true', "/p:TiaOpennessReferenceDir=$generatedDirectory", "/p:OutputPath=$(Join-Path $temporaryRoot 'probe/')", "/p:IntermediateOutputPath=$(Join-Path $temporaryRoot 'probe-obj/')")
    }

    $entries = @(Get-ChildItem -LiteralPath $generatedDirectory -Force)
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -or $artifactNames -cnotcontains $entry.Name) { throw "Unexpected generated artifact: $($entry.Name)" }
    }
    foreach ($name in $artifactNames) {
        $generated = [IO.Path]::GetFullPath((Join-Path $generatedDirectory $name))
        Assert-ChildPath $generated $generatedDirectory
        if (-not (Test-Path -LiteralPath $generated -PathType Leaf)) { throw "Missing generated artifact: $name" }
        Assert-Artifact $generated $name $sourceHash
    }
    if ($Update) {
        # All generated files pass before either exact tracked target is replaced.
        foreach ($name in $artifactNames) {
            $destination = [IO.Path]::GetFullPath((Join-Path $targetDirectory $name))
            Assert-ChildPath $destination $targetDirectory
            Copy-Item -LiteralPath (Join-Path $generatedDirectory $name) -Destination $destination -Force
            Write-Output "Replaced $name"
        }
    }
    foreach ($name in $artifactNames) { Assert-Artifact ([IO.Path]::GetFullPath((Join-Path $targetDirectory $name))) $name $sourceHash }
    Write-Output 'Both reference artifacts are current.'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally {
    if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        $cleanup = Resolve-Directory $temporaryRoot
        if (-not $cleanup.Equals([IO.Path]::GetFullPath($temporaryRoot), [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($cleanup)).StartsWith('tia-reference-verifier-', [StringComparison]::Ordinal)) { throw 'Unsafe temporary cleanup target' }
        Assert-ChildPath $cleanup ([IO.Path]::GetTempPath())
        foreach ($entry in Get-ChildItem -LiteralPath $cleanup -Recurse -Force) { Assert-ChildPath $entry.FullName $cleanup }
        Remove-Item -LiteralPath $cleanup -Recurse -Force
    }
}
