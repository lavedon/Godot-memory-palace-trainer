[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $projectRoot '.tools'
$version = '4.7.2-stable'
$baseUrl = "https://github.com/godotengine/godot-builds/releases/download/$version"
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$sumsPath = Join-Path $toolRoot 'SHA512-SUMS.txt'
if (-not (Test-Path -LiteralPath $sumsPath)) {
    & curl.exe -fL --retry 3 --silent --show-error "$baseUrl/SHA512-SUMS.txt" -o $sumsPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not download Godot checksums.' }
}
function Get-VerifiedAsset([string]$name) {
    $destination = Join-Path $toolRoot $name
    $checksumLine = Get-Content -LiteralPath $sumsPath | Where-Object { $_ -match ([regex]::Escape($name) + '$') }
    if (-not $checksumLine) { throw "Missing official checksum for $name" }
    $expectedHash = ($checksumLine -split '\s+')[0]
    if (-not (Test-Path -LiteralPath $destination)) {
        Write-Host "Downloading $name"
        & curl.exe -fL --retry 3 --silent --show-error "$baseUrl/$name" -o $destination
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $name" }
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA512).Hash -ne $expectedHash) {
        throw "Checksum mismatch: $destination. Remove this file and rerun bootstrap."
    }
    return $destination
}
$editorDir = Join-Path $toolRoot "Godot_v${version}_mono_win64"
if (-not (Test-Path -LiteralPath (Join-Path $editorDir "Godot_v${version}_mono_win64.exe"))) {
    $editorZip = Get-VerifiedAsset "Godot_v${version}_mono_win64.zip"
    Expand-Archive -LiteralPath $editorZip -DestinationPath $toolRoot -Force
}
$templateDir = Join-Path $toolRoot 'templates'
if (-not (Test-Path -LiteralPath (Join-Path $templateDir 'windows_release_x86_64.exe'))) {
    $templateZip = Get-VerifiedAsset "Godot_v${version}_mono_export_templates.tpz"
    New-Item -ItemType Directory -Path $templateDir -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($templateZip)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.Name -in @('windows_release_x86_64.exe', 'windows_debug_x86_64.exe', 'version.txt')) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templateDir $entry.Name), $true)
            }
        }
    } finally { $archive.Dispose() }
}
Write-Host "Godot $version .NET ready at $editorDir"
