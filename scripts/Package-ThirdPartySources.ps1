[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.5.1',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$cacheDirectory = Join-Path $projectRoot 'artifacts\third-party-sources'
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot ('artifacts\release\' + $Version)
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

$sources = @(
    [pscustomobject]@{
        Component = 'NAudio.Wasapi and NAudio.Core'; Version = '2.2.1'
        Revision = 'b5d5ff83fd378f046398891fe5cd99426ce44732'
        File = 'NAudio-2.2.1-source.tar.gz'
        Url = 'https://codeload.github.com/naudio/NAudio/tar.gz/b5d5ff83fd378f046398891fe5cd99426ce44732'
        ExpectedSha256 = '4b4ba5cda49bc80e47198744f04546cd09385b118cf124acd118743ad06e3475'
    },
    [pscustomobject]@{
        Component = 'LibVLCSharp and LibVLCSharp.WPF'; Version = '3.10.2'
        Revision = '3082c09ca955131c705fc4767fde03e9c4a14999'
        File = 'LibVLCSharp-3.10.2-source.tar.gz'
        Url = 'https://codeload.github.com/videolan/libvlcsharp/tar.gz/3082c09ca955131c705fc4767fde03e9c4a14999'
        ExpectedSha256 = 'b9e12cbca8038fc78b468f53da80c0fb24f01963cb3c7205a23abbc7f832b79f'
    },
    [pscustomobject]@{
        Component = 'TagLibSharp'; Version = '2.3.0'
        Revision = 'b5ae84f2e84087bf160bb0471420200dd2b5d809'
        File = 'TagLibSharp-2.3.0-source.tar.gz'
        Url = 'https://codeload.github.com/mono/taglib-sharp/tar.gz/b5ae84f2e84087bf160bb0471420200dd2b5d809'
        ExpectedSha256 = '2e54eb7382991caeafd2ac414ca5ab6ca2a4d2b5fe9bba4d8abff3fc1b308195'
    },
    [pscustomobject]@{
        Component = 'QRCoder'; Version = '1.8.1'
        Revision = 'd050cc7c0bc986f14cfedf24b947e0ec5c1a7736'
        File = 'QRCoder-1.8.1-source.tar.gz'
        Url = 'https://codeload.github.com/Shane32/QRCoder/tar.gz/d050cc7c0bc986f14cfedf24b947e0ec5c1a7736'
        ExpectedSha256 = 'cb630124e3c3e852f001b14cc99eeb924432e120bc50cb26826aa472be821cd4'
    },
    [pscustomobject]@{
        Component = 'VLC'; Version = '3.0.24'
        Revision = '6de05adcbaf2e8b85fe86aad4169393098628119'
        File = 'vlc-3.0.24.tar.xz'
        Url = 'https://download.videolan.org/pub/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz'
        ExpectedSha256 = 'e7cab503d1d7d5849b89d2cf0e1ee60d0ef6d012407791b644b9cfc0cc225fdf'
    }
)

$manifest = @()
$bundleFiles = @()
foreach ($source in $sources) {
    $destination = Join-Path $cacheDirectory $source.File
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        $partialPath = $destination + '.' + [guid]::NewGuid().ToString('N') + '.partial'
        try {
            Write-Host "Downloading $($source.Component) $($source.Version) source..."
            Invoke-WebRequest -Uri $source.Url -OutFile $partialPath -TimeoutSec 180 -UseBasicParsing
            $hash = (Get-FileHash -LiteralPath $partialPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($source.ExpectedSha256 -and $hash -ne $source.ExpectedSha256) {
                throw "Source hash mismatch for $($source.File)."
            }
            Move-Item -LiteralPath $partialPath -Destination $destination -Force
        }
        finally {
            if (Test-Path -LiteralPath $partialPath -PathType Leaf) { Remove-Item -LiteralPath $partialPath }
        }
    }
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($source.ExpectedSha256 -and $hash -ne $source.ExpectedSha256) { throw "Cached source hash mismatch for $($source.File)." }
    $manifest += [pscustomobject]@{
        component = $source.Component; version = $source.Version; revision = $source.Revision
        file = $source.File; url = $source.Url; sha256 = $hash
        bytes = (Get-Item -LiteralPath $destination).Length
    }
    $bundleFiles += $destination
}

$manifestPath = Join-Path $cacheDirectory 'manifest.json'
[pscustomobject]@{
    applicationVersion = $Version
    description = 'Selected upstream source snapshots; not verified complete corresponding source for all native binaries. See REDISTRIBUTION.md.'
    archives = $manifest
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$bundleFiles += $manifestPath

$noticeNames = @('NAudio-LICENSE.txt', 'LibVLCSharp-3.10.2-LICENSE.txt', 'TagLibSharp-2.3.0-COPYING.txt', 'QRCoder-LICENSE.txt',
    'VLC-3.0.24-COPYING.txt', 'VLC-3.0.24-COPYING.LIB.txt', 'SOURCES.md', 'REDISTRIBUTION.md')
foreach ($name in $noticeNames) {
    $sourcePath = Join-Path (Join-Path $projectRoot 'licenses') $name
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Required source notice is missing: $name" }
    $bundleFiles += $sourcePath
}
$hashManifest = Join-Path $cacheDirectory 'SHA256SUMS.txt'
@($bundleFiles | ForEach-Object {
    (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $_)
}) | Set-Content -LiteralPath $hashManifest -Encoding ASCII
$bundleFiles += $hashManifest

# Explicit input list prevents unrelated cached files from entering the archive.
$zipPath = Join-Path $OutputDirectory ('MagicMusicWall-' + $Version + '-ThirdParty-Sources.zip')
Compress-Archive -LiteralPath $bundleFiles -DestinationPath $zipPath -Force
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
($zipHash + '  ' + (Split-Path -Leaf $zipPath)) | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
Write-Host 'Source companion built. Review REDISTRIBUTION.md for the native source inventory limitation.'
Write-Output $zipPath
