$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$rid = "win-x64"
$config = "Release"
$dist = Join-Path $PSScriptRoot "dist"
$exeName = "HardwareMonitor.exe"

Write-Host "Publishing $exeName (self-contained single-file $rid)..."
dotnet publish "HardwareMonitor.Windows.UI\HardwareMonitor.Windows.UI.csproj" `
    -c $config -r $rid -p:PublishProfile=FolderProfile -p:SelfContained=true

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $dist | Out-Null

$publishDir = Join-Path $PSScriptRoot "HardwareMonitor.Windows.UI\publish"
$publishedExe = Join-Path $publishDir $exeName
if (-not (Test-Path $publishedExe)) {
    throw "Publish failed: $publishedExe not found."
}

Copy-Item $publishedExe (Join-Path $dist $exeName) -Force

$helpFile = Join-Path $PSScriptRoot "..\docs\WINDOWS_PLUGIN_HELP.md"
if (Test-Path $helpFile) {
    Copy-Item $helpFile (Join-Path $dist "WINDOWS_PLUGIN_HELP.md")
}

$sizeMb = [math]::Round((Get-Item (Join-Path $dist $exeName)).Length / 1MB, 1)
Write-Host ""
Write-Host "Done. Release folder: $dist"
Write-Host "  $exeName  ($sizeMb MB — single self-contained exe, zip for users)"
