#requires -Version 7.0
# Creates disposable screenshot fixtures only. Never imports an existing library or media.
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$demoData = Join-Path $projectRoot 'artifacts\docs-demo\Data'
$demoMedia = 'C:\Users\Public\Music\MagicMusicWall Demo'
foreach ($directory in @($demoData, $demoMedia)) {
    if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -gt 0) {
        throw "Demo destination is not empty; no existing files were changed: $directory"
    }
}

$tagLibrary = @(
    (Join-Path $projectRoot 'artifacts\release-staging\app\TagLibSharp.dll'),
    (Join-Path $projectRoot 'src\MagicMusicWall.App\bin\Release\net10.0-windows\TagLibSharp.dll')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$writeTags = $false
if ($tagLibrary) {
    try { Add-Type -Path $tagLibrary; $writeTags = $true }
    catch { Write-Warning 'TagLib could not load; the demonstration catalog still supplies metadata.' }
}

function New-SilentWave([string]$Path, [int]$Seconds) {
    $samples = [byte[]]::new(8000 * 2 * $Seconds) # 8 kHz, mono, signed 16-bit PCM silence.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $samples.Length))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([int]16)
        $writer.Write([int16]1); $writer.Write([int16]1); $writer.Write([int]8000); $writer.Write([int]16000)
        $writer.Write([int16]2); $writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$samples.Length); $writer.Write($samples)
    }
    finally { $writer.Dispose() }
}

$albums = @(
    @{ Artist='Night Garden'; Album='Evening Colors'; Genre='Rock'; Year=2025; Titles=@('Evening Light','Open Windows') },
    @{ Artist='Night Garden'; Album='Quiet Horizons'; Genre='Rock'; Year=2026; Titles=@('Lantern Road','Silver Horizon') },
    @{ Artist='Sunday Trio'; Album='Around the Table'; Genre='Jazz'; Year=2025; Titles=@('After Dinner','Gentle Conversation') },
    @{ Artist='Sunday Trio'; Album='Morning Sketches'; Genre='Jazz'; Year=2026; Titles=@('Sunday Sketch','First Cup') },
    @{ Artist='Neon Avenue'; Album='City Reflections'; Genre='80s'; Year=1986; Titles=@('City Afterglow','Neon Walk') },
    @{ Artist='Neon Avenue'; Album='Weekend Signals'; Genre='80s'; Year=1988; Titles=@('Weekend Signal','Distant Arcade') }
)
[IO.Directory]::CreateDirectory($demoData) | Out-Null
[IO.Directory]::CreateDirectory($demoMedia) | Out-Null
$items = [Collections.Generic.List[object]]::new()
foreach ($album in $albums) {
    $albumDirectory = Join-Path (Join-Path $demoMedia $album.Artist) $album.Album
    [IO.Directory]::CreateDirectory($albumDirectory) | Out-Null
    for ($i = 0; $i -lt $album.Titles.Count; $i++) {
        $title = $album.Titles[$i]
        $path = Join-Path $albumDirectory ('{0:00} - {1}.wav' -f ($i + 1), $title)
        $seconds = 45 + ($items.Count * 3)
        New-SilentWave -Path $path -Seconds $seconds
        if ($writeTags) {
            $tagFile = $null
            try {
                $tagFile = [TagLib.File]::Create($path)
                $tagFile.Tag.Title = $title; $tagFile.Tag.Performers = [string[]]@($album.Artist)
                $tagFile.Tag.AlbumArtists = [string[]]@($album.Artist); $tagFile.Tag.Album = $album.Album
                $tagFile.Tag.Genres = [string[]]@($album.Genre); $tagFile.Tag.Year = [uint32]$album.Year
                $tagFile.Tag.Track = [uint32]($i + 1); $tagFile.Tag.Disc = [uint32]1
                $tagFile.Tag.Comment = 'Synthetic silent documentation fixture; no recorded song.'
                $tagFile.Save()
            }
            catch { Write-Warning "File tags unavailable for this fixture; using catalog metadata: $title" }
            finally { if ($tagFile) { $tagFile.Dispose() } }
        }
        $items.Add([ordered]@{
            Id=[guid]::NewGuid().ToString('N'); Path=$path; Title=$title; Artist=$album.Artist
            Album=$album.Album; TaggedAlbum=$album.Album; AlbumOverride=''; AlbumLookupSource=''
            Genre=$album.Genre; Year=$album.Year; TrackNumber=($i+1); DiscNumber=1
            DurationSeconds=$seconds; IsVideo=$false; IsAvailable=$true
        })
    }
}
$playlists = foreach ($name in @('Dinner','Jazz','Rock',"80's")) {
    $tracks = switch ($name) {
        'Dinner' { @($items | Where-Object { $_.Artist -eq 'Sunday Trio' -or $_.Title -eq 'Evening Light' }) }
        'Jazz' { @($items | Where-Object Genre -eq 'Jazz') }
        'Rock' { @($items | Where-Object Genre -eq 'Rock') }
        "80's" { @($items | Where-Object Genre -eq '80s') }
    }
    [ordered]@{ Id=[guid]::NewGuid().ToString('N'); Name=$name; ItemIds=@($tracks | ForEach-Object { $_.Id }) }
}
$library = [ordered]@{
    SchemaVersion=1; PortableBasePath=''; Items=@($items.ToArray())
    Folders=@([ordered]@{ Id=[guid]::NewGuid().ToString('N'); Path=$demoMedia }); Playlists=@($playlists)
}
$settings = [ordered]@{
    Volume=0; AudioDeviceId=''; AudioDeviceName=''; Shuffle=$false; RepeatQueue=$false
    RescanOnStartup=$false; CollectionGridView=$false; SharingAddress=''; SharingPort=0
    RemoteEnabled=$false; BrowserPlaybackEnabled=$true; RemoteAddress=''; RemotePort=8787
    RemoteAccessToken=''; TmdbTokenProtected=''
}
$library | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $demoData 'library.json') -Encoding utf8
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $demoData 'settings.json') -Encoding utf8
Write-Output "Created $($items.Count) silent demo tracks. Data: $demoData"
Write-Output "Media: $demoMedia"
Write-Output ('Launch: dotnet run --project src\MagicMusicWall.App\MagicMusicWall.App.csproj -c Release -- --data-dir "{0}"' -f $demoData)
