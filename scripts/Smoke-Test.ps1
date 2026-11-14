$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot 'src\MagicMusicWall.App\bin\Release\net10.0-windows\MagicMusicWall.exe'
& dotnet build (Join-Path $projectRoot 'src\MagicMusicWall.App\MagicMusicWall.App.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$testData = Join-Path $projectRoot 'artifacts\smoke'
$run = Start-Process -FilePath $appPath -ArgumentList @('--smoke-test', '--data-dir', ('"' + $testData + '"')) -WindowStyle Hidden -PassThru
if (-not $run.WaitForExit(30000)) { $run.Kill(); throw 'Smoke check timed out.' }
$resultPath = Join-Path $testData 'smoke-result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json -Depth 4
if ($run.ExitCode -ne 0 -or -not $result.success) { throw 'Smoke check failed.' }
