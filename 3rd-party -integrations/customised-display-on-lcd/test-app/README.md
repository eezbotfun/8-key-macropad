To make debugging easier, I wrote a test program for sending protocol text to the Macropad.

![CUS USB CDC tester — Grid mode](../images/message-send-tester.png)

## Download (Windows)

Pre-built Windows x64 build (no Visual Studio needed):

- [CusProtocolTester-win-x64-net8.0.zip](prebuild-bin-file/CusProtocolTester-win-x64-net8.0.zip)

1. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if you do not already have it.
2. Download the zip, extract it, and run `CusProtocolTester.exe`.
3. Select the device COM port, click **Open**, then use **Grid mode** / **Absolute mode** / **Raw JSON**.

Protocol details: [host_usb_cdc_customised.md](../protocol/host_usb_cdc_customised.md)

## Build from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet run -c Release
```

Or open `CusProtocolTester.sln` in Visual Studio 2022 and press F5.
