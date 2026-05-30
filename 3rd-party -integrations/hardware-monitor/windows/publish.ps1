$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$rid = "win-x64"
$config = "Release"
$dist = Join-Path $PSScriptRoot "dist"

Write-Host "Publishing HardwareMonitor (self-contained $rid)..."
dotnet publish "HardwareMonitor.Windows.UI\HardwareMonitor.Windows.UI.csproj" `
    -c $config -r $rid -p:PublishProfile=FolderProfile -p:SelfContained=true

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $dist | Out-Null

$publishDir = Join-Path $PSScriptRoot "HardwareMonitor.Windows.UI\publish"
Copy-Item -Path (Join-Path $publishDir "*") -Destination $dist -Recurse -Force
foreach ($junkDir in @("publish", "win-x64")) {
    $path = Join-Path $dist $junkDir
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}

$helpFile = Join-Path $PSScriptRoot "..\docs\WINDOWS_PLUGIN_HELP.md"
if (Test-Path $helpFile) {
    Copy-Item $helpFile (Join-Path $dist "WINDOWS_PLUGIN_HELP.md")
}

Write-Host ""
Write-Host "Done. Release folder: $dist"
Write-Host "  HardwareMonitor.exe  (single app — zip this folder for users)"
