Set-StrictMode -Version Latest

function Get-MagicMusicWallVersion {
    param([string]$ProjectRoot, [string]$RequestedVersion)
    $value = $RequestedVersion
    if ([string]::IsNullOrWhiteSpace($value)) {
        [xml]$project = Get-Content -LiteralPath (Join-Path $ProjectRoot 'src\MagicMusicWall.App\MagicMusicWall.App.csproj') -Raw
        $value = [string]$project.Project.PropertyGroup.Version
    }
    if ($value -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a numeric application version such as 0.5.1.' }
    return $value
}

function Resolve-MagicMusicWallPayload {
    param([string]$ProjectRoot, [string]$PayloadDirectory, [string]$Version)
    if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) { $PayloadDirectory = Join-Path $ProjectRoot 'artifacts\release-staging\app' }
    $payload = (Resolve-Path -LiteralPath $PayloadDirectory -ErrorAction Stop).Path
    $livePackage = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'artifacts\MagicMusicWall-win-x64')).TrimEnd('\')
    if ($payload.TrimEnd('\') -ieq $livePackage) { throw 'Use a clean release staging directory, not the working app package.' }
    foreach ($required in @('MagicMusicWall.exe', 'MagicMusicWall.runtimeconfig.json', 'coreclr.dll', 'libvlc\win-x64\libvlc.dll', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'licenses', 'OfflineDocs\index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Incomplete release payload: missing $required." }
    }
    $binaryVersion = (Get-Item -LiteralPath (Join-Path $payload 'MagicMusicWall.exe')).VersionInfo.ProductVersion
    if (($binaryVersion -split '\+')[0] -ne $Version) { throw "Payload version $binaryVersion does not match requested $Version." }
    foreach ($entry in Get-ChildItem -LiteralPath $payload -Recurse -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Release payload contains a symbolic link or placeholder: $($entry.Name)." }
        if ($entry.PSIsContainer -and $entry.Name -in @('Data', '.git', '.vs', 'TestResults', 'obj')) { throw "Release payload contains a private/build data directory: $($entry.Name)." }
        if (-not $entry.PSIsContainer -and ($entry.Name -match '^(library|settings)(\..*)?\.json($|\.)|^music-audio-(features\.json|scan\.txt)($|\.)|^errors\.log($|\.)|^\.env($|\.)|\.mmw-backup-|\.pfx$|\.p12$|\.key$')) {
            throw "Release payload contains a runtime data or secret file: $($entry.Name)."
        }
    }
    return $payload
}

function Resolve-MagicMusicWallOutput {
    param([string]$ProjectRoot, [string]$OutputDirectory, [string]$Version, [string]$PayloadDirectory)
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $ProjectRoot "artifacts\release\$Version" }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $payloadRoot = [IO.Path]::GetFullPath($PayloadDirectory).TrimEnd('\')
    if ($output.TrimEnd('\') -ieq $payloadRoot -or $output.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The release output directory must be outside the application payload.'
    }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    return $output
}

function New-MagicMusicWallZip {
    param([string]$Directory, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($Directory, $Destination, [IO.Compression.CompressionLevel]::Optimal, $false)
}

function Write-MagicMusicWallChecksums {
    param([string[]]$Files, [string]$Destination)
    $lines = foreach ($file in $Files | Sort-Object -Unique) {
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($file)
    }
    [IO.File]::WriteAllLines($Destination, [string[]]$lines, [Text.UTF8Encoding]::new($false))
}
