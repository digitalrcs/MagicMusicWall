[CmdletBinding()]
param(
    [string]$PayloadDirectory,
    [string]$OutputDirectory,
    [string]$Version,
    [string]$InstallerPath,
    [string[]]$AdditionalAsset = @()
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'installer\Packaging.Common.ps1')
$releaseVersion = Get-MagicMusicWallVersion -ProjectRoot $projectRoot -RequestedVersion $Version
$payload = Resolve-MagicMusicWallPayload -ProjectRoot $projectRoot -PayloadDirectory $PayloadDirectory -Version $releaseVersion
$output = Resolve-MagicMusicWallOutput -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Version $releaseVersion -PayloadDirectory $payload
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Start-MagicMusicWall.cmd'))) { throw 'The clean payload must include Start-MagicMusicWall.cmd for the portable ZIP.' }
$installerName = "MagicMusicWall-$releaseVersion-Setup-x64.exe"
if ([string]::IsNullOrWhiteSpace($InstallerPath)) { $InstallerPath = Join-Path $output $installerName }
$installer = (Resolve-Path -LiteralPath $InstallerPath -ErrorAction Stop).Path
$installerFile = Get-Item -LiteralPath $installer
if ($installerFile.Length -lt 1MB) { throw 'The installer is unexpectedly small.' }
$installerVersion = $installerFile.VersionInfo.ProductVersion
if ($installerVersion -ne $releaseVersion -and $installerVersion -ne ($releaseVersion + '.0')) {
    throw "Installer version $installerVersion does not match requested $releaseVersion."
}
$installerTarget = Join-Path $output $installerName
if ($installer -ine $installerTarget) { Copy-Item -LiteralPath $installer -Destination $installerTarget -Force }

$portable = Join-Path $output "MagicMusicWall-$releaseVersion-Portable-x64.zip"
New-MagicMusicWallZip -Directory $payload -Destination $portable
$releaseAssets = [Collections.Generic.List[string]]::new()
$releaseAssets.Add($installerTarget); $releaseAssets.Add($portable)
foreach ($asset in $AdditionalAsset) {
    $source = (Resolve-Path -LiteralPath $asset -ErrorAction Stop).Path
    if ((Get-Item -LiteralPath $source).PSIsContainer) { throw 'Additional release assets must be individual files.' }
    $name = [IO.Path]::GetFileName($source)
    if ($name -in @($installerName, [IO.Path]::GetFileName($portable), 'SHA256SUMS.txt', 'START-HERE.txt') -or $name -like '*-USB-Kit.zip') { throw "Reserved release asset name: $name." }
    $destination = Join-Path $output $name
    if ($releaseAssets.Contains($destination)) { throw "Duplicate release asset name: $name." }
    if ($source -ine $destination) { Copy-Item -LiteralPath $source -Destination $destination -Force }
    $releaseAssets.Add($destination)
}

$startHere = @"
MAGIC MUSIC WALL $releaseVersion - WINDOWS 11 USB KIT
DigitalRCS.com | https://digitalrcs.com

INSTALL ON A PC
1. Copy or extract this USB kit to a writable folder on the USB drive.
2. On the Windows 11 PC, run $installerName.
3. Follow Setup. It installs for your account and adds a Start menu shortcut.
4. Open the app, add your music/video folders, and choose your audio output.
The .NET runtime and VLC engine are included. Installation works offline.

RUN WITHOUT INSTALLING
1. Extract MagicMusicWall-$releaseVersion-Portable-x64.zip completely.
2. Keep the extracted folder together, including VLC and the runtime files.
3. Run Start-MagicMusicWall.cmd. Its Data folder stays beside the launcher.
Do not run from inside the ZIP. Use a writable USB drive and close the app
before unplugging it or moving it to another PC.

MOVING TO ANOTHER PC
The installer can be carried on USB and installed on each PC independently.
Music files are not included in this kit. Add accessible folders on each PC.
The portable launcher keeps a separate library/settings folder on the USB.
Paths to music must exist on the destination PC; Bluetooth pairing and output
selection are specific to each PC. See OfflineDocs/index.html in the app folder
for portable paths, moving a library, remote control and troubleshooting.

PLATFORM AND INTERNET
This is an x64 Windows 11 build for Intel/AMD PCs. ARM x64 emulation has not
been validated. Online metadata lookup needs internet; playback of local
files does not. TMDB movie lookup needs your optional token in Setup.

CHECK THE DOWNLOAD
SHA256SUMS.txt lists the SHA-256 hashes of the files inside this kit.
The installer is unsigned unless the release page explicitly states otherwise.
Only use the package from the official project release. Do not disable Windows
security to run an untrusted download.

DOCUMENTATION AND UPDATES
OfflineDocs/index.html is included in the installer and portable app.
https://github.com/digitalrcs/MagicMusicWall
https://github.com/digitalrcs/MagicMusicWall/releases

Uninstalling keeps your library, playlists, settings and original media.
Do not include your Data folder or personal media when sharing this public kit.
"@
$startHerePath = Join-Path $output 'START-HERE.txt'
[IO.File]::WriteAllText($startHerePath, $startHere, [Text.UTF8Encoding]::new($false))
$releaseAssets.Add($startHerePath)

$kitStage = Join-Path $output ('.usb-kit-' + [Guid]::NewGuid().ToString('N'))
$kit = Join-Path $output "MagicMusicWall-$releaseVersion-USB-Kit.zip"
try {
    New-Item -ItemType Directory -Path $kitStage | Out-Null
    foreach ($asset in $releaseAssets) { Copy-Item -LiteralPath $asset -Destination $kitStage }
    Write-MagicMusicWallChecksums -Files $releaseAssets.ToArray() -Destination (Join-Path $kitStage 'SHA256SUMS.txt')
    New-MagicMusicWallZip -Directory $kitStage -Destination $kit
}
finally {
    # Only this invocation's unique staging directory may be recursively removed.
    $resolvedStage = [IO.Path]::GetFullPath($kitStage)
    $expectedParent = [IO.Path]::GetFullPath($output).TrimEnd('\') + '\'
    if (-not $resolvedStage.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedStage) -notlike '.usb-kit-*') {
        throw 'Refusing cleanup outside the expected release staging directory.'
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
$releaseAssets.Add($kit)
Write-MagicMusicWallChecksums -Files $releaseAssets.ToArray() -Destination (Join-Path $output 'SHA256SUMS.txt')
Write-Output $kit
Write-Output (Join-Path $output 'SHA256SUMS.txt')
