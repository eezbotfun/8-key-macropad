This is a demo plugin for the EezBotFun Configurator application.

The plugin runs as a separate process and periodically sends hardware monitoring
information to the macro pad via the Named Pipe interface, allowing the device
to display real-time system status.

Implementations
---------------
  Windows (recommended)
    EezBotFun Hardware Monitor Plugin — HardwareMonitor.exe (windows/ — run publish.ps1)
    Uses LibreHardwareMonitorLib in-process for CPU/GPU/storage/network sensors.
    User guide: docs/WINDOWS_PLUGIN_HELP.md
    Upstream library: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
    License: Mozilla Public License 2.0 (MPL-2.0)
      https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE

  Python (reference)
    temperature_monitor.py — WMI / NVML based prototype; less accurate than the
    Windows native build. Build with build_exe.ps1 if needed.

  Other platforms
    See docs/STANDALONE_PLUGINS.md for per-OS guidance and the shared JSON/EZBF
    protocol contract (schemas/pc-status-event.example.json).
