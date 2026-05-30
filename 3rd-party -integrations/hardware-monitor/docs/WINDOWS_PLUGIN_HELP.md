# EezBotFun Hardware Monitor — Windows plugin user guide

One app, no Windows service, no install steps. Unzip, run **`HardwareMonitor.exe`**, keep it open while you use the macro pad.

The app reads CPU, GPU, memory, disk, and network data from your PC and sends it to **EezBotFun Configurator** over a fixed named pipe (`ezb-macropad`). The configurator shows the data on your macro pad LCD.

---

## What you need

| Requirement | Notes |
|-------------|--------|
| **Windows 10 or 11 (64-bit)** | Self-contained build — no .NET install required. |
| **EezBotFun Configurator** | Running on the same PC, with **Enable Named Pipe Service** turned on. |
| **Macro pad connected** | Set up in the configurator as usual. |

---

## Quick start

1. **Unzip** the release folder (keep all `.dll` files and subfolders together).
2. **Start EezBotFun Configurator** and enable **Enable Named Pipe Service**.
3. **Double-click `HardwareMonitor.exe`**.
4. Check your **macro pad LCD** — hardware status should update within a few seconds.

That is all. While **`HardwareMonitor.exe`** is running, data is sent to the configurator. You can **minimize** the window. **Close** the app to stop monitoring.

Optional: change **Interval (seconds)** in the app and click **Save settings** (default `1` second is fine).

---

## Files in the zip

| File | Purpose |
|------|---------|
| **`HardwareMonitor.exe`** | **Run this.** Single monitoring app. |
| Other `.dll` files, `runtimes\`, etc. | Bundled runtime and libraries — keep next to the `.exe`. |
| `WINDOWS_PLUGIN_HELP.md` | This guide. |

There is **no** separate service program and **no** administrator install step for normal use.

---

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Interval (seconds)** | `1` | How often status is sent (1–300). |

Settings are saved to:

`%ProgramData%\EezBotFun\HardwareMonitor\settings.json`

---

## Troubleshooting

### Macro pad shows no hardware data

- EezBotFun Configurator is not running.
- **Enable Named Pipe Service** is off in the configurator.
- **`HardwareMonitor.exe`** is not running (or was closed).
- Restart configurator, then run **`HardwareMonitor.exe`** again.

### CPU/GPU temperature shows 0

- Close the app, **right-click `HardwareMonitor.exe` → Run as administrator**, then check the LCD again.
- Update GPU drivers (NVIDIA / AMD / Intel).

### App fails to start after unzip

- Extract the **full** zip again — do not run from inside the zip viewer.
- Keep every file in the same folder as **`HardwareMonitor.exe`**.

---

## Quick reference

```
1. Unzip → run HardwareMonitor.exe
2. EezBotFun Configurator running + Named Pipe Service enabled
3. Macro pad LCD updates while the app is open
```

For developers, see [windows/README.md](../windows/README.md).
