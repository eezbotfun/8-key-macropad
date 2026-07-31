# EezBotFun Hardware Monitor Plugin

Standalone C# app for EezBotFun Configurator. **Ship `HardwareMonitor.exe`** — one self-contained program; no Windows service install.

Sensor data comes from **[LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib/)** ([LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)), linked in-process.

## Solution projects

| Project | Output | Role |
|---------|--------|------|
| **HardwareMonitor.Windows.Core** | Class library | Sensors, EZBF pipe, settings, monitor loop |
| **HardwareMonitor.Windows.UI** | **`HardwareMonitor.exe`** | **User-facing app** (publish this) |
| **HardwareMonitor.Windows.Service** | Service exe | Optional / legacy Windows service (not shipped to users) |
| **HardwareMonitor.Windows** | Console | Debug via `--once` |

## LibreHardwareMonitor integration

### NuGet dependency

The Core project references the official NuGet package:

```xml
<PackageReference Include="LibreHardwareMonitorLib" Version="0.9.4" />
```

Source and API docs: [LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

To upgrade, bump the version in `HardwareMonitor.Windows.Core/HardwareMonitor.Windows.Core.csproj` and rebuild. Check the upstream [releases](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases) for breaking changes.

### License

[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and **LibreHardwareMonitorLib** are licensed under the [Mozilla Public License 2.0 (MPL-2.0)](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE). This project consumes the library as an unmodified NuGet dependency; upstream source is available at the repository above. Some bundled upstream components have additional terms — see [THIRD-PARTY-NOTICES.txt](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/THIRD-PARTY-NOTICES.txt).

### How this project uses the library

On startup, `HardwareStatusCollector` opens a `Computer` instance with all relevant hardware groups enabled (CPU, GPU, memory, motherboard, storage, network, controllers), then refreshes sensors each tick via an `UpdateVisitor`:

```csharp
using LibreHardwareMonitor.Hardware;

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsNetworkEnabled = true,
    IsStorageEnabled = true,
};
computer.Open();
// each capture: computer.Accept(new UpdateVisitor()); then read sensors
```

Implementation files:

| File | Role |
|------|------|
| `Hardware/HardwareStatusCollector.cs` | Opens `Computer`, maps LHM sensors to the EZBF JSON payload |
| `Hardware/LhmSensorCache.cs` | Single pass over the hardware tree per tick |
| `Hardware/UpdateVisitor.cs` | Calls `hardware.Update()` on each node (required for fresh values) |
| `Hardware/LhmNetworkReader.cs` | Network throughput from LHM network sensors |

### Sensors read from LHM

| Metric | LHM source | Fallback if missing |
|--------|------------|---------------------|
| CPU package / core temperature | `SensorType.Temperature` on `HardwareType.Cpu` | — |
| CPU load | `SensorType.Load` ("CPU Total") | `PerformanceCounter` (`CpuLoadMetrics`) |
| CPU power | `SensorType.Power` ("Package") | — |
| GPU temperature / load / power / fan / clock / VRAM | NVIDIA, AMD, Intel GPU hardware types | — |
| Storage temperature | `HardwareType.Storage` | — |
| Network up/down | `HardwareType.Network` throughput sensors | `NetworkMetrics` (PerformanceCounter) |
| Board / case fans | `Motherboard`, `SuperIO`, `Cooler` | GPU fan RPM |

RAM usage and primary disk fill level use Windows APIs (`MemoryMetrics`, `DriveInfo`) because LHM does not expose those fields in the shape the macropad expects. Disk read/write totals use `DiskIoMetrics` (PerformanceCounter).

### Administrator / driver access

LibreHardwareMonitor reads low-level hardware sensors. If CPU/GPU temperatures stay at zero, run **`HardwareMonitor.exe` as administrator** (same as the standalone Libre Hardware Monitor app).

### Standalone vs desktop app

| Approach | This plugin | Separate LHM desktop app |
|----------|-------------|--------------------------|
| Extra install for end users | No — library is embedded in `HardwareMonitor.exe` | User must download and run LHM |
| Sensor accuracy | Same underlying library | Same |
| WMI bridge to running LHM | Not used | Possible but not required here |

## Quick start

**End users:** [../docs/WINDOWS_PLUGIN_HELP.md](../docs/WINDOWS_PLUGIN_HELP.md) — unzip and run **`HardwareMonitor.exe`**.

### Developer / local build

```bash
dotnet build HardwareMonitor.Windows.sln -c Release
dotnet run --project HardwareMonitor.Windows.UI
```

Or publish the release zip:

```powershell
.\publish.ps1
```

Output: `windows/dist/HardwareMonitor.exe` (single self-contained file). Zip `dist\` for distribution.

Ensure EezBotFun Configurator is running with **Enable Named Pipe Service** while testing.

## App behavior

- Starts monitoring automatically when opened.
- **Single instance** — only one copy runs; launching again brings the existing window to the front.
- **Interval (seconds)** — how often status is sent (1–300, default 2); saved to `%ProgramData%\EezBotFun\HardwareMonitor\settings.json`.
- Close the app to stop monitoring. Minimize is fine — keep it running for live LCD data.

## Optional: Windows service (legacy)

The **HardwareMonitor.Windows.Service** project remains for advanced/headless deployment. End-user releases use the single **`HardwareMonitor.exe`** app only.

- **Service name:** `EezBotFunHardwareMonitor`
- Console debug: `HardwareMonitor.Windows.Service.exe --console`

## JSON / protocol

- Framing: `../../EZBF_IPC_PROTOCOL.txt`
- Payload: `../schemas/pc-status-event.example.json`

## Publish

Self-contained single-file **`HardwareMonitor.exe`** for `win-x64`:

```powershell
.\publish.ps1
```

Zip the `dist\` folder for release. See [../docs/WINDOWS_PLUGIN_HELP.md](../docs/WINDOWS_PLUGIN_HELP.md).

Manual publish:

```bash
dotnet publish HardwareMonitor.Windows.UI/HardwareMonitor.Windows.UI.csproj -c Release -r win-x64 -p:PublishProfile=FolderProfile -p:SelfContained=true
```

Output assembly name: **`HardwareMonitor.exe`**.

### Visual Studio publish

Right-click **HardwareMonitor.Windows.UI** → **Publish** → **FolderProfile**, platform **x64**.
