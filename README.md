# Camera Lock Monitor

**A lightweight Windows system tray utility that monitors camera availability.**

Camera Lock Monitor helps you see whether your webcam is available, disabled, or in an unknown state — without having to open a video conferencing application.

## Features

- **Automatic camera detection** – discovers camera devices using Windows device information.
- **System tray indicator** – provides a persistent visual status indicator.
- **Color-coded status**
  - 🟢 Green: Camera available
  - 🔴 Red: Camera detected as locked or disabled
  - 🟡 Yellow: Camera status unknown
  - ⚪ Grey: No camera detected
- **Automatic status updates** – monitors device changes and periodically refreshes the status.
- **Windows startup integration** – optionally starts automatically when you sign in.
- **Lightweight** – does not access the camera stream, capture images, or record video.

## Why?

Many laptops include hardware camera privacy switches, keyboard shortcuts, or manufacturer-specific privacy controls.

Windows does not provide a consistent, persistent system tray indicator for these controls.

Camera Lock Monitor aims to make camera availability visible at a glance.

## Compatibility

- Windows 10 / Windows 11
- .NET 10
- x64 (initial release)

Camera detection uses Windows Plug and Play device information.

**Important:** Windows does not expose camera privacy status consistently across all manufacturers. Some devices report their privacy lock state through device status information, while others may only indicate that the camera is unavailable.

As a result, the accuracy of lock detection depends on the hardware and its Windows driver.

The initial version includes specific handling for a tested Samsung laptop camera. Other camera devices use generic Windows device status information.

## Installation

Download the latest release from the [Releases page](../../releases).

1. Download the Windows x64 release.
2. Extract the ZIP archive, if applicable.
3. Run `CameraLockMonitor.exe`.
4. The application appears in the Windows notification area.

The application can be configured to start automatically with Windows through its tray menu.

## Usage

Right-click the system tray icon to access:

- Current camera status
- Check now
- Start with Windows
- Exit

You can also double-click the tray icon to refresh the camera status.

For persistent visibility, configure Windows taskbar notification area settings to always show the Camera Lock Monitor icon.

## Building from source

### Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Internet access for NuGet package restoration

### Build

```powershell
git clone https://github.com/YOUR_USERNAME/CameraLockMonitor.git
cd CameraLockMonitor

dotnet restore
dotnet build -c Release
```

### Publish a standalone executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The published executable will be located in:

```text
bin/Release/net10.0-windows/win-x64/publish/
```

## Privacy

Camera Lock Monitor only reads Windows device information to determine camera availability.

It does not:

- Capture images or video
- Access the camera stream
- Record audio
- Send device information over the network
- Collect analytics or telemetry

## Roadmap

- [ ] Improve camera detection across manufacturers
- [ ] Display individual camera devices and their status
- [ ] Add camera selection
- [ ] Improve hardware privacy switch detection
- [ ] Create a Windows installer
- [ ] Automate GitHub releases using GitHub Actions

## Contributing

Contributions, bug reports, hardware compatibility reports, and feature suggestions are welcome.

If camera lock detection does not work correctly on your device, please consider opening an issue with:

- Laptop or webcam manufacturer and model
- Windows version
- Camera device status reported by Windows
- Whether the device has a physical privacy switch or keyboard shortcut

Please avoid including personal device identifiers, serial numbers, or other unique hardware information in public reports.

## License

See [LICENSE](LICENSE).