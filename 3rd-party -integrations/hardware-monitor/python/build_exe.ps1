$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$hiddenImports = @(
    "win32pipe",
    "win32file",
    "win32event",
    "pywintypes",
    "win32api",
    "pythoncom",
    "wmi",
    "pynvml",
    "serial",
    "serial.tools.list_ports"
)

$commonArgs = @(
    "--noconfirm",
    "--clean",
    "--onefile",
    "--distpath", "dist",
    "--workpath", "build",
    "--specpath", "build"
)

foreach ($import in $hiddenImports) {
    $commonArgs += @("--hidden-import", $import)
}
$commonArgs += @("--collect-submodules", "wmi")

Write-Host "Building GUI plugin: dist/PCStatusMonitor.exe"
pyinstaller @commonArgs --windowed --name PCStatusMonitor temperature_monitor_ui.py

Write-Host "Building CLI monitor: dist/HardwareMonitor.exe"
pyinstaller @commonArgs --console --name HardwareMonitor temperature_monitor.py

Write-Host "Done. Executables are in dist/"
