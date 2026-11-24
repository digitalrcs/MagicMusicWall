#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$docsOutput = [IO.Path]::GetFullPath($OutputDirectory, $projectRoot)
New-Item -ItemType Directory -Path $docsOutput -Force | Out-Null
$sources = @((Get-Item -LiteralPath (Join-Path $projectRoot 'README.md')),
    (Get-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md')),
    (Get-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md')),
    (Get-Item -LiteralPath (Join-Path $projectRoot 'CONTRIBUTING.md')))
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -Filter '*.md' -Recurse
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -Filter '*.md' -Recurse
$css = @'
html{font-family:Segoe UI,Arial,sans-serif;color:#202532;background:#f5f6fa;line-height:1.65}body{max-width:1080px;margin:auto;padding:32px}main{background:white;padding:32px;border:1px solid #dce0e8;border-radius:10px}h1,h2,h3{line-height:1.25;color:#29213d}h2{margin-top:2em}a{color:#6542a7}img{max-width:100%;height:auto}pre{overflow:auto;background:#f0edf7;padding:16px}code{font-family:Consolas,monospace}table{border-collapse:collapse;width:100%}td,th{padding:10px;border:1px solid #dce0e8;text-align:left}nav{margin-bottom:20px}blockquote{margin-left:0;border-left:4px solid #a894d6;padding-left:18px}@media(max-width:700px){body,main{padding:16px}}
'@
foreach ($source in $sources) {
    $relative = [IO.Path]::GetRelativePath($projectRoot, $source.FullName)
    $targetRelative = [IO.Path]::ChangeExtension($relative, '.html')
    $target = Join-Path $docsOutput $targetRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    $html = (ConvertFrom-Markdown -LiteralPath $source.FullName).Html
    $html = [regex]::Replace($html, 'href="([^":?#]+)\.md([#?][^"]*)?"', 'href="$1.html$2"')
    $homeLink = [IO.Path]::GetRelativePath((Split-Path -Parent $target), (Join-Path $docsOutput 'index.html')).Replace('\','/')
    $guideLink = [IO.Path]::GetRelativePath((Split-Path -Parent $target), (Join-Path $docsOutput 'docs\README.html')).Replace('\','/')
    $title = [Net.WebUtility]::HtmlEncode($source.BaseName + ' · Magic Music Wall')
    $page = "<!doctype html><html lang=`"en`"><head><meta charset=`"utf-8`"><meta name=`"viewport`" content=`"width=device-width,initial-scale=1`"><title>$title</title><style>$css</style></head><body><nav><a href=`"$homeLink`">Magic Music Wall</a> · <a href=`"$guideLink`">All guides</a></nav><main>$html</main></body></html>"
    [IO.File]::WriteAllText($target, $page)
}
$images = Join-Path $projectRoot 'docs\images'
if (Test-Path -LiteralPath $images) { Copy-Item -LiteralPath $images -Destination (Join-Path $docsOutput 'docs') -Recurse -Force }
foreach ($retiredImage in @('installer.png', 'playlists.png', 'metadata.png', 'setup-metadata.png')) {
    $retiredPath = Join-Path $docsOutput ('docs\images\' + $retiredImage)
    if (Test-Path -LiteralPath $retiredPath -PathType Leaf) { Remove-Item -LiteralPath $retiredPath }
}
Copy-Item -LiteralPath (Join-Path $docsOutput 'README.html') -Destination (Join-Path $docsOutput 'index.html') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $docsOutput -Force
Write-Output (Join-Path $docsOutput 'index.html')
