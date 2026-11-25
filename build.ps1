# Script de build & publication d'une nouvelle version Relive:GO sur GitHub
param(
    [string]$Version = "1.0.0",
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# 1. Compile ReliveGOLauncher.exe
New-Item -ItemType Directory -Path (Join-Path $root 'dist') -Force | Out-Null
$exeOut = Join-Path $root 'dist\ReliveGOLauncher.exe'
& $csc /nologo /target:winexe /optimize+ /out:$exeOut `
    /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll `
    /r:System.Web.Extensions.dll `
    (Join-Path $root 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Write-Host "Built: $exeOut ($((Get-Item $exeOut).Length) bytes)"

# 2. Refresh client_files & manifest.json
$cf = Join-Path $root 'client_files'
if (Test-Path 'D:\SteamLibrary\steamapps\common\csgo legacy\csgo.exe') {
    Copy-Item 'D:\SteamLibrary\steamapps\common\csgo legacy\csgo.exe' (Join-Path $cf 'csgo.exe') -Force
}

$tag = "v$Version"
$releaseBase = "https://github.com/nat649/relive-go-launcher/releases/download/$tag"
$fileSpecs = @(
    @{ path = 'csgo.exe';                 asset = 'csgo.exe';        backup = $true },
    @{ path = 'csgo_gc/config.txt';       asset = 'config.txt';      backup = $false },
    @{ path = 'csgo_gc/items_extra.txt';  asset = 'items_extra.txt'; backup = $false },
    @{ path = 'csgo_gc/price_sheet.txt';  asset = 'price_sheet.txt'; backup = $false }
)
$manifestFiles = @()
foreach ($f in $fileSpecs) {
    $full = Join-Path $cf ($f.path -replace '/', '\')
    $manifestFiles += [ordered]@{
        path   = $f.path
        url    = "$releaseBase/$($f.asset)"
        sha256 = (Get-FileHash $full -Algorithm SHA256).Hash.ToLower()
        size   = (Get-Item $full).Length
        backup = $f.backup
    }
}
$manifest = [ordered]@{
    version          = $Version
    launcher_version = $Version
    gc_server_url    = 'https://rlgo.specs.qzz.io'
    updated_at       = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    files            = $manifestFiles
}
[System.IO.File]::WriteAllText((Join-Path $root 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

if ($Publish) {
    Push-Location $root
    try {
        git add manifest.json src/Program.cs README.md build.ps1 .gitignore
        git commit -m "Release $tag"
        git push
        gh release create $tag `
            (Join-Path $root 'dist\ReliveGOLauncher.exe') `
            (Join-Path $cf 'csgo.exe') `
            (Join-Path $cf 'csgo_gc\config.txt') `
            (Join-Path $cf 'csgo_gc\items_extra.txt') `
            (Join-Path $cf 'csgo_gc\price_sheet.txt') `
            --title "Relive:GO $tag" `
            --notes "Mise à jour Relive:GO $tag (auto-updater SHA-256)."
    } finally {
        Pop-Location
    }
}
