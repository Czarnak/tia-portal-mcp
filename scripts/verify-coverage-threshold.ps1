[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $CoveragePath,

    [Parameter(Mandatory = $true)]
    [double] $MinimumLineRate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedCoveragePath = (Resolve-Path -LiteralPath $CoveragePath -ErrorAction Stop).Path

try {
    $coverageXml = New-Object System.Xml.XmlDocument
    $coverageXml.Load($resolvedCoveragePath)
}
catch {
    throw "Failed to parse Cobertura report as XML: '$resolvedCoveragePath'."
}

$rootElement = $coverageXml.DocumentElement
if ($null -eq $rootElement) {
    throw "Cobertura report has no root element: '$resolvedCoveragePath'."
}

$lineRateAttribute = $rootElement.GetAttribute('line-rate')
if ([string]::IsNullOrWhiteSpace($lineRateAttribute)) {
    throw "Cobertura report is missing a root 'line-rate' attribute: '$resolvedCoveragePath'."
}

$lineRate = [double]::NaN
$isNumericLineRate = [double]::TryParse(
    $lineRateAttribute,
    [System.Globalization.NumberStyles]::Float,
    [System.Globalization.CultureInfo]::InvariantCulture,
    [ref] $lineRate)

if (-not $isNumericLineRate) {
    throw "Cobertura report has a non-numeric root 'line-rate' attribute: '$resolvedCoveragePath'."
}

if ($lineRate -lt $MinimumLineRate) {
    [Console]::Error.WriteLine("Coverage line-rate $lineRate is below the required minimum $MinimumLineRate.")
    exit 1
}

Write-Host "Coverage line-rate $lineRate meets the required minimum $MinimumLineRate."
exit 0
