<#
.SYNOPSIS
    Verifies documentation link integrity across all tracked markdown files.

.DESCRIPTION
    Runs three checks and fails on the first that reports a problem:

    1. Every relative link target in a tracked markdown file resolves to a file or
       directory that exists.
    2. README.md contains no relative markdown links. README.md is also the NuGet
       package readme (<PackageReadmeFile> in TiaMcpServer/TiaMcpServer.csproj), and
       relative links do not resolve on nuget.org, so its cross-document links must be
       absolute GitHub URLs.
    3. Every tracked markdown file under docs/ is reachable — something links to it.
       This is what keeps docs/README.md honest as the entry point.

.PARAMETER RepositoryRoot
    Repository root to scan. Defaults to the parent of this script's directory.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Push-Location $RepositoryRoot
try {
    $docs = @(git ls-files '*.md') | Where-Object { $_ }
    if ($docs.Count -eq 0) { throw 'No tracked markdown files found.' }

    # Markdown inline links, excluding images: [text](target)
    $linkPattern = '(?<!\!)\[[^\]]*\]\(([^)]+)\)'
    $brokenLinks = [System.Collections.Generic.List[string]]::new()
    $inbound = @{}
    foreach ($doc in $docs) { $inbound[$doc] = 0 }

    foreach ($doc in $docs) {
        $content = Get-Content -LiteralPath $doc -Raw
        $docDir = Split-Path -Parent $doc

        foreach ($match in [regex]::Matches($content, $linkPattern)) {
            $target = $match.Groups[1].Value.Trim()

            # Skip absolute URLs, protocol-relative URLs, and pure anchors.
            if ($target -match '^(https?:|mailto:|//|#)') { continue }

            # Strip anchor and any title suffix.
            $path = ($target -split '#', 2)[0].Trim()
            if ([string]::IsNullOrWhiteSpace($path)) { continue }

            $resolved = if ($docDir) { Join-Path $docDir $path } else { $path }
            $normalized = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $resolved))

            if (-not (Test-Path -LiteralPath $normalized)) {
                $brokenLinks.Add("  $doc -> $target")
                continue
            }

            # Record inbound links between tracked docs (self-links do not count).
            $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $normalized).Replace('\', '/')
            if ($inbound.ContainsKey($relative) -and $relative -ne $doc) {
                $inbound[$relative]++
            }
        }
    }

    if ($brokenLinks.Count -gt 0) {
        Write-Host "Broken relative links ($($brokenLinks.Count)):" -ForegroundColor Red
        $brokenLinks | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "Documentation link check failed: $($brokenLinks.Count) broken link(s)."
    }
    Write-Host "OK: all relative links resolve ($($docs.Count) files scanned)." -ForegroundColor Green

    # README.md is packed as the NuGet readme; relative links break there.
    $readme = Get-Content -LiteralPath 'README.md' -Raw
    $relativeInReadme = @([regex]::Matches($readme, $linkPattern) |
        Where-Object { $_.Groups[1].Value.Trim() -notmatch '^(https?:|mailto:|//|#)' })
    if ($relativeInReadme.Count -gt 0) {
        Write-Host 'README.md must use absolute https://github.com/... links (it is the NuGet package readme):' -ForegroundColor Red
        $relativeInReadme | ForEach-Object { Write-Host "  $($_.Value)" -ForegroundColor Red }
        throw "Documentation link check failed: $($relativeInReadme.Count) relative link(s) in README.md."
    }
    Write-Host 'OK: README.md contains no relative markdown links.' -ForegroundColor Green

    # README.md links out with absolute URLs; credit those as inbound links.
    foreach ($doc in $docs) {
        if ($readme -match [regex]::Escape("/blob/main/$doc")) { $inbound[$doc]++ }
    }

    # docs/README.md is the entry point and README.md is the landing page: neither needs
    # an inbound link.
    $exempt = @('README.md', 'docs/README.md', 'CLAUDE.md', 'AGENTS.md', 'CONTRIBUTING.md', 'SECURITY.md', 'ROADMAP.md')
    $orphans = @($docs | Where-Object { $_ -like 'docs/*' -and $inbound[$_] -eq 0 -and $exempt -notcontains $_ })

    if ($orphans.Count -gt 0) {
        Write-Host "Unreachable documents under docs/ ($($orphans.Count)) — add them to docs/README.md:" -ForegroundColor Red
        $orphans | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Documentation link check failed: $($orphans.Count) unreachable document(s)."
    }
    Write-Host 'OK: every document under docs/ is reachable.' -ForegroundColor Green

    Write-Host 'Documentation link check passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
