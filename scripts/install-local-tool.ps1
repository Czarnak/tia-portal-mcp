[CmdletBinding()]
param(
    # Overrides the version derived from `git describe` (<tag>-local.<ahead>.g<sha>).
    [ValidateNotNullOrEmpty()]
    [string] $Version,

    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory = 'artifacts/local-install',

    [ValidateNotNullOrEmpty()]
    [string] $TiaPortalV21Dir = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48',

    # Stops running tia-mcp processes, which drops any live MCP session and project binding.
    [switch] $StopRunningServer,

    [switch] $AllowDirty,

    [switch] $SkipPackageVerification
)

# Installs the current checkout as the `tia-mcp` global tool (docs/development/packaging.md).
# Run from PowerShell: Git Bash rewrites /p:... MSBuild switches and breaks the version overrides.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param([string] $Command, [string[]] $Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (-not $AllowDirty -and (git status --porcelain)) {
        throw 'Working tree is not clean. Commit or stash changes, or pass -AllowDirty.'
    }

    if (-not (Test-Path -LiteralPath $TiaPortalV21Dir)) {
        throw "TIA Portal V21 Openness assemblies not found at '$TiaPortalV21Dir'. Pass -TiaPortalV21Dir."
    }

    if (-not $Version) {
        $described = (git describe --tags --long).Trim()
        if ($described -notmatch '^v?(?<base>\d+\.\d+\.\d+)-(?<ahead>\d+)-g(?<sha>[0-9a-f]+)$') {
            throw "Cannot derive a version from 'git describe' output '$described'. Pass -Version."
        }
        $Version = "$($Matches.base)-local.$($Matches.ahead).g$($Matches.sha)"
    }
    Write-Host "Local package version: $Version"

    $running = @(Get-Process -Name tia-mcp -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        if (-not $StopRunningServer) {
            throw "tia-mcp is running (PID $($running.Id -join ', ')) and holds a lock on the installed exe. Stop it or pass -StopRunningServer."
        }
    }

    Invoke-Native dotnet @('restore', 'TiaMcpServer.slnx')

    Invoke-Native dotnet @(
        'pack', 'TiaMcpServer/TiaMcpServer.csproj', '-c', 'Release', '--no-restore',
        '-o', $OutputDirectory,
        "/p:Version=$Version", "/p:PackageVersion=$Version", "/p:InformationalVersion=$Version",
        '/p:IncludeSourceRevisionInInformationalVersion=false',
        '/p:UseTiaPortalReferenceStubs=false',
        "/p:TiaPortalV21Dir=$TiaPortalV21Dir")

    $package = Join-Path $OutputDirectory "TiaMcpServer.$Version.nupkg"
    if (-not $SkipPackageVerification) {
        & (Join-Path $PSScriptRoot 'verify-doctor-package.ps1') -PackagePath $package
    }

    if ($running.Count -gt 0) {
        Stop-Process -Id $running.Id -Force
    }

    # Uninstall may fail when nothing is installed yet; that is fine.
    dotnet tool uninstall -g TiaMcpServer | Out-Null
    # --version is required: prerelease versions are otherwise ignored and nuget.org stable is installed.
    Invoke-Native dotnet @('tool', 'install', '-g', '--add-source', $OutputDirectory, 'TiaMcpServer', '--version', $Version)

    Invoke-Native dotnet @('tool', 'list', '-g')
    Invoke-Native tia-mcp @('--version')

    Write-Host 'Reconnect the MCP client (e.g. /mcp in Claude Code) and re-run open_project; the previous project binding is gone.'
}
finally {
    Pop-Location
}
