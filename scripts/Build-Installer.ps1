[CmdletBinding()]
param(
    [string]$PayloadDirectory,
    [string]$OutputDirectory,
    [string]$Version,
    [string]$IsccPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'installer\Packaging.Common.ps1')
$releaseVersion = Get-MagicMusicWallVersion -ProjectRoot $projectRoot -RequestedVersion $Version
$payload = Resolve-MagicMusicWallPayload -ProjectRoot $projectRoot -PayloadDirectory $PayloadDirectory -Version $releaseVersion
$output = Resolve-MagicMusicWallOutput -ProjectRoot $projectRoot -OutputDirectory $OutputDirectory -Version $releaseVersion -PayloadDirectory $payload
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup 6 was not found. Install it or provide -IsccPath.' }
foreach ($asset in @('DigitalRCS.ico', 'wizard-side.png', 'wizard-small.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "installer\assets\$asset"))) { throw "Missing installer asset $asset. Run python installer/prepare-assets.py." }
}
$script = Join-Path $projectRoot 'installer\MagicMusicWall.iss'
& $IsccPath "/DAppVersion=$releaseVersion" "/DPayloadDirectory=$payload" "/DReleaseOutputDirectory=$output" $script
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed (exit $LASTEXITCODE)." }
$installer = Join-Path $output "MagicMusicWall-$releaseVersion-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'The compiler did not produce the expected installer.' }
Write-MagicMusicWallChecksums -Files @($installer) -Destination ($installer + '.sha256')
Write-Output $installer
