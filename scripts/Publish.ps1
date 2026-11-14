#requires -Version 7.0
param([string]$Configuration = 'Release', [switch]$SkipArchive, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot 'artifacts\MagicMusicWall-win-x64' }
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
$zipPath = $outputDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + '.zip'
& dotnet publish (Join-Path $projectRoot 'src\MagicMusicWall.App\MagicMusicWall.App.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $outputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Start-MagicMusicWall.cmd') -Destination $outputDirectory
if (Test-Path -LiteralPath (Join-Path $projectRoot 'licenses')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $outputDirectory -Recurse -Force }
& (Join-Path $PSScriptRoot 'Copy-RuntimeNotices.ps1') -PayloadDirectory $outputDirectory
& (Join-Path $PSScriptRoot 'Build-OfflineDocs.ps1') -OutputDirectory (Join-Path $outputDirectory 'OfflineDocs')
if (-not $SkipArchive) {
    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $zipPath -Force
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($zipPath) } | Set-Content -LiteralPath ($zipPath + '.sha256')
    Write-Output $zipPath
}
