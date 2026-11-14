[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadDirectory,
    [string]$NuGetPackagesDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$runtimeConfigPath = Join-Path $payload 'MagicMusicWall.runtimeconfig.json'
$depsPath = Join-Path $payload 'MagicMusicWall.deps.json'
if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
    throw "Runtime configuration is missing: $runtimeConfigPath"
}
if ([string]::IsNullOrWhiteSpace($NuGetPackagesDirectory)) {
    $NuGetPackagesDirectory = $env:NUGET_PACKAGES
    if ([string]::IsNullOrWhiteSpace($NuGetPackagesDirectory)) {
        $NuGetPackagesDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
    }
}
$packageCache = (Resolve-Path -LiteralPath $NuGetPackagesDirectory).Path
$config = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$frameworks = @{}
foreach ($propertyName in @('includedFrameworks', 'frameworks', 'framework')) {
    $property = $config.runtimeOptions.PSObject.Properties[$propertyName]
    if ($null -ne $property) {
        foreach ($framework in @($property.Value)) {
            $frameworks[[string]$framework.name] = [string]$framework.version
        }
    }
}

$runtimeIdentifier = 'win-x64'
if (Test-Path -LiteralPath $depsPath -PathType Leaf) {
    $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    foreach ($library in $deps.libraries.PSObject.Properties) {
        if ($library.Name -match '^runtimepack\.(Microsoft\.(?:NETCore|AspNetCore|WindowsDesktop)\.App)\.Runtime\.(win-(?:x64|x86|arm64))/([^/]+)$') {
            $name, $rid, $version = $Matches[1], $Matches[2], $Matches[3]
            if ($frameworks.ContainsKey($name) -and $frameworks[$name] -ne $version) {
                throw "Runtime version mismatch between configuration and dependency manifest for $name."
            }
            $frameworks[$name] = $version
            $runtimeIdentifier = $rid
        }
    }
}
if (-not $frameworks.ContainsKey('Microsoft.NETCore.App')) {
    throw 'Cannot determine the exact Microsoft.NETCore.App runtime version from the payload.'
}

$definitions = @{
    'Microsoft.NETCore.App' = @{ Package = 'microsoft.netcore.app.runtime'; Folder = 'dotnet-runtime'; RequireThirdParty = $true }
    'Microsoft.AspNetCore.App' = @{ Package = 'microsoft.aspnetcore.app.runtime'; Folder = 'aspnetcore-runtime'; RequireThirdParty = $true }
    'Microsoft.WindowsDesktop.App' = @{ Package = 'microsoft.windowsdesktop.app.runtime'; Folder = 'windowsdesktop-runtime'; RequireThirdParty = $false }
}
$plannedCopies = @()
foreach ($name in @($frameworks.Keys | Sort-Object)) {
    if (-not $definitions.ContainsKey($name)) { throw "No runtime notice policy exists for $name." }
    $version = $frameworks[$name]
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Invalid runtime version for $name." }
    $definition = $definitions[$name]
    $packageId = $definition.Package + '.' + $runtimeIdentifier
    $sourceDirectory = Join-Path (Join-Path $packageCache $packageId) $version
    if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
        throw "Runtime pack $packageId/$version is unavailable in the NuGet package cache. Restore/publish this target first."
    }
    $license = @(Get-ChildItem -LiteralPath $sourceDirectory -File | Where-Object { $_.Name -match '^LICENSE(?:\.TXT)?$' })
    $notices = @(Get-ChildItem -LiteralPath $sourceDirectory -File | Where-Object { $_.Name -match '^THIRD-PARTY-NOTICES(?:\.TXT)?$' })
    if ($license.Count -ne 1) { throw "Expected one license file in runtime pack $packageId/$version." }
    if ($definition.RequireThirdParty -and $notices.Count -eq 0) { throw "Required third-party notices are missing for $packageId/$version." }
    foreach ($file in @($license + $notices)) {
        $relativePath = 'licenses/' + $definition.Folder + '/' + $file.Name
        $plannedCopies += [pscustomobject]@{
            Source = $file.FullName
            RelativePath = $relativePath
            Package = $packageId
            Version = $version
        }
    }
}

# Validate every required source first, then copy unchanged notice files.
$manifest = @()
foreach ($copy in $plannedCopies) {
    $target = Join-Path $payload $copy.RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $copy.Source -Destination $target -Force
    $manifest += [pscustomobject]@{
        package = $copy.Package
        version = $copy.Version
        file = $copy.RelativePath
        sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifestPath = Join-Path $payload 'licenses/runtime-notices.json'
ConvertTo-Json -InputObject $manifest -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Copied $($manifest.Count) runtime license/notice files for $runtimeIdentifier."
Write-Output $manifestPath
