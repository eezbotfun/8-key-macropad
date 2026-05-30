# Standalone hardware-monitor plugins (per OS)

The EezBotFun integration model is **already plugin-friendly**: your program is a separate process that speaks **EZBF IPC** over a **Windows named pipe**. It is not tied to Python. The current Python app is a **reference implementation**, not a requirement.

See also:

- `../../EZBF_IPC_PROTOCOL.txt` — wire format (12-byte header + UTF-8 JSON)
- `../../ReadMe.txt` — integration overview
- `../schemas/pc-status-event.example.json` — payload shape this monitor sends

---

## Recommended layout (one repo, multiple deliverables)

```
hardware-monitor/
  docs/                    # This file
  python/                  # Python implementation
  windows/                 # C# + LibreHardwareMonitorLib (see windows/README.md)
```

Ship **one binary per OS**. Share only the **JSON fields** and **EZBF framing**, not source code.

---

## What stays the same on every platform


| Layer         | Responsibility                                                                      |
| ------------- | ----------------------------------------------------------------------------------- |
| **Transport** | Connect as pipe **client** to `\\.\pipe\ezb-macropad` (Windows today)               |
| **Framing**   | `EZBF` magic, version `1`, type `0x20` (EVENT), 4-byte little-endian payload length |
| **Payload**   | Minified JSON matching `schemas/pc-status-event.example.json`                       |
| **Cadence**   | ~1 Hz is enough for LCD; faster polling wastes CPU                                  |


Unknown JSON fields must be ignored by the host (forward-compatible). Your plugin may add debug fields later (e.g. `"cpuSource":"lhm"`) if the host ignores extras.

---

## What differs per OS (sensor stack)

There is **no portable API** for “true CPU die temperature.” Each OS plugin should embed or link a **native sensor provider** and use the same priority idea: **accurate source first**, OS fallback last, never fake offsets.

### Windows (current host support)

**Best standalone accuracy:** link **[LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)** inside your process (C# is the most common wrapper; C++/CLI or a small C# sensor DLL + C++ pipe client also works). Licensed under [MPL-2.0](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE).


| Provider                             | CPU                         | GPU          | Notes                                                             |
| ------------------------------------ | --------------------------- | ------------ | ----------------------------------------------------------------- |
| LibreHardwareMonitorLib (in-process) | Excellent                   | All vendors  | Single `.exe`, no separate LHM app; may need admin on some boards |
| NVML / ADL / DXGI vendor SDKs        | N/A                         | NVIDIA / AMD | Good if you only target one GPU vendor                            |
| WMI → running LHM/OHM                | Good                        | Good         | No extra deps in *your* binary, but user must run LHM             |
| ACPI / perf thermal zones            | Poor on many Intel desktops | N/A          | Last resort only                                                  |


**Suggested Windows stack for a new native plugin:**

1. C# (.NET 8) console app + `LibreHardwareMonitorLib` → publish single-file `HardwareMonitor.Windows.exe`
2. Collect CPU package + hottest core, GPU edge temp, load via same library
3. Send EZBF EVENT messages (same as Python reference)

Alternative: **C++** pipe client + small **C# helper process** that only reads sensors and prints JSON on stdout (two binaries — simpler native IPC, slightly more moving parts).

### Linux (when the host exposes IPC)

Use **sysfs hwmon** (`/sys/class/hwmon/*/temp*_input`) via a small C or Rust binary. Supplement with:

- **NVML** for NVIDIA GPU
- `**amdgpu` hwmon** or `**radeontop`** patterns for AMD

`psutil` is not required; reading sysfs directly is fewer dependencies and works in a static binary.

**Transport note:** `EZBF_IPC_PROTOCOL.txt` currently specifies **Windows named pipes only**. A Linux plugin needs the configurator to expose a **Unix domain socket** (or TCP localhost) with the **same EZBF frame**. Until then, Linux builds are spec-ready but not connectable to today’s Windows-only server.

### macOS (future)

IOKit / SMC tools vary by Apple Silicon vs Intel. Plan a separate `HardwareMonitor.macOS` binary and the same JSON contract once IPC exists on that platform.

---

## Python vs native standalone


|                   | Python + PyInstaller (today)      | Native + LHM lib (recommended standalone)             |
| ----------------- | --------------------------------- | ----------------------------------------------------- |
| Install size      | Large (embedded runtime)          | Smaller with .NET single-file or static Rust          |
| CPU temp accuracy | Depends on WMI / optional LHM app | Strong when LHM lib is in-process                     |
| GPU               | NVIDIA via NVML only              | All GPUs via LHM                                      |
| AV / admin        | Usually fine                      | LHM may prompt for driver access (same as HWiNFO/LHM) |
| Maintenance       | Easy prototyping                  | Per-OS CI build matrices                              |


Keep the Python plugin as a **quick reference**; ship **Windows native** for users who want accurate i9/AMD temps without installing Libre Hardware Monitor separately.

---

## Minimal Windows plugin loop (pseudocode)

```
init sensors (LHM Computer.Open + Update)
loop every 1s:
  sensors.Update()
  json = build_pc_status(cpu, gpu, memory, ...)  // see schema
  frame = EZBF_HEADER(EVENT, len(json)) + utf8(json)
  write(pipe, frame)
```

Pipe connection: `CreateFile("\\.\pipe\ezb-macropad", ...)` — same as `NamedPipeSender` in `temperature_monitor.py`.

---

## Versioning and compatibility

- Bump **only** `ProtocolVersion` in the binary header if the frame layout changes (rare).
- Add JSON fields freely; do not rename/remove fields the macropad already uses (`cpu.temp`, `gpu.temp`, `cmd`, etc.).
- Use `"cmd": 1230` unless the configurator documents another command id for hardware status.

---

## CI / release

- **windows-x64**: build signed or unsigned `.exe`, zip with README
- **linux-x64**: build when socket transport exists
- Integration test: mock pipe server that asserts EZBF magic + valid JSON schema

---

## Summary

- You **do not** need Python; the host integration is **process + protocol**, not language.
- Use **different binaries per OS**, sharing **only** EZBF framing and the JSON schema.
- For **maximum accuracy** in a **fully standalone** Windows plugin, prefer **in-process LibreHardwareMonitorLib** over WMI/ACPI.
- The main gap for Linux/macOS is **transport** in the configurator app, not sensor code.

