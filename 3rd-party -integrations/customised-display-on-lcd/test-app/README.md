# CusProtocolTester (Avalonia)

Desktop utility to exercise the device **USB CDC `cus` framed protocol** described in:

[host_usb_cdc_customised.md](../protocol/host_usb_cdc_customised.md)

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows, Linux, or macOS (Avalonia desktop)

## Visual Studio

Open **`CusProtocolTester.sln`** in Visual Studio 2022 (with the **.NET desktop development** workload). Set **CusProtocolTester** as the startup project and press F5 to debug.

## Run (CLI)

From this directory:

```bash
dotnet run -c Release
```

Or build and launch the executable under `bin/Release/net8.0/` (platform-specific launcher).

## Usage

1. Connect the device and identify its virtual serial port (e.g. `COM5` on Windows, `/dev/ttyACM0` on Linux).
2. Click **Refresh**, choose the port, set **Baud** (default `115200`; USB CDC often ignores baud).
3. **Open** the port.
4. Edit geometry (default **h** matches the **480×200** drawable canvas on a 320-tall display), colors, **text** (each send with non-empty text adds a stacked panel; empty text adds nothing), **clear_canvas** (erase all stacked panels), **align**, **long_mode**, **activate**, and optional **border** / **border-color** / **border-radius**. The **JSON payload** box shows the UTF-8 JSON that will be wrapped in the frame (pretty-printed for readability; the wire payload is minified). It updates when you leave a field, change a combo, or click **Refresh preview** / **Send framed message**.  
   **Send framed message** emits **one write**: `cus` + big-endian 16-bit payload length + compact UTF-8 JSON.
5. Optionally paste JSON into **Raw JSON** and use **Send raw JSON** (same framing; length must stay within firmware limits).

The log reports byte counts and warnings when the payload exceeds the typical device reassembly budget (**2043** bytes); see the protocol doc for full limits.

## Protocol reference

| Item | Value |
|------|--------|
| Magic | `cus` (0x63 0x75 0x73) |
| Length | Big-endian `uint16` (payload byte count) |
| Payload | UTF-8 JSON |

Field names and semantics match §2 of the host guide linked above.
