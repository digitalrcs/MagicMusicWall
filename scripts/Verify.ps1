#requires -Version 7.0
param([switch]$Desktop, [switch]$ReplayGain, [string]$FfmpegPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet build src/MagicMusicWall.App -c Release
    if ($LASTEXITCODE) { throw 'Application build failed.' }
    foreach ($project in @('MagicMusicWall.Core.Tests', 'MagicMusicWall.Streaming.Tests')) {
        & dotnet test "tests/$project" -c Release
        if ($LASTEXITCODE) { throw "$project failed." }
    }
    $native = Join-Path $projectRoot 'src/MagicMusicWall.App/bin/Release/net10.0-windows/libvlc/win-x64'
    foreach ($project in @('MagicMusicWall.Visuals.Checks', 'MagicMusicWall.MusicMap.Checks')) {
        & dotnet run --project "tests/$project" -c Release -- $native
        if ($LASTEXITCODE) { throw "$project failed." }
    }
    if ($Desktop) {
        & dotnet run --project tests/MagicMusicWall.Session.Checks -c Release
        if ($LASTEXITCODE) { throw 'Desktop session checks failed.' }
        & dotnet run --project tests/MagicMusicWall.Details.Checks -c Release
        if ($LASTEXITCODE) { throw 'Desktop details checks failed.' }
    }
    if ($ReplayGain) {
        if (-not $FfmpegPath) { $FfmpegPath = (Get-Command ffmpeg -ErrorAction Stop).Source }
        $native = Join-Path $projectRoot 'src/MagicMusicWall.App/bin/Release/net10.0-windows/libvlc/win-x64'
        & dotnet run --project tests/MagicMusicWall.ReplayGain.Checks -c Release -- --native-dir $native --ffmpeg $FfmpegPath
        if ($LASTEXITCODE) { throw 'ReplayGain checks failed.' }
    }
}
finally { Pop-Location }
