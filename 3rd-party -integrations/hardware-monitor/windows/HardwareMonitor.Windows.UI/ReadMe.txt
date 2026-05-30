This is a demo plugin for the EezBotFun Configurator application.

The plugin runs as a separate process and periodically sends hardware monitoring
information to the macro pad via the Named Pipe interface, allowing the device
to display real-time system status.

Implementations
---------------
  Windows (recommended)
    C# service + settings UI in windows/
    Uses LibreHardwareMonitorLib in-process for CPU/GPU/storage/network sensors.
    See windows/README.md for build, install, and sensor details.
    Upstream library: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
    License: Mozilla Public License 2.0 (MPL-2.0)
      https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE