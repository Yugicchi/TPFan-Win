# TPFan-Win

[![Build Status](https://github.com/Yugicchi/TPFan-Win/workflows/Build%20and%20Test/badge.svg)](https://github.com/Yugicchi/TPFan-Win/actions)
[![Security](https://github.com/Yugicchi/TPFan-Win/workflows/CodeQL%20Analysis/badge.svg)](https://github.com/Yugicchi/TPFan-Win/security)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Lightweight single-binary WPF application for ThinkPad T480 fan control.

The app reads CPU temperature, fan speed, and RPM via LibreHardwareMonitor
and drives the fan directly through the embedded controller (EC) using
InpOut32. Everything — UI, system tray, sensor polling, EC writes — runs
in one process. There is no separate background service and no named-pipe
IPC.

UX: "buka GUI sudah termasuk buka service" — opening the GUI already
includes the service functionality.

## Features

- Real-time monitoring — CPU temperature, fan speed, RPM
- Direct EC fan override — through ThinkPad Embedded Controller registers
  (0x2F level, 0x31 mode) via InpOut32
- Fan curve detection — mathematical mapping from system behavior
- Manual override — slider with smart snapping
- System tray — live temperature badge, right-click menu for quick
  preset levels, tooltip with `temp°C | RPM | mode`
- Single-binary distribution — one self-contained `TPFan.GUI.exe`
  (~77 MB) with `inpoutx64.dll` bundled inside
- Auto-reset on exit — fan returns to firmware auto control on window
  close, tray exit, Ctrl+C, or `ProcessExit`
- Graceful degradation — without admin or the InpOut32 driver, the app
  continues in read-only mode (sensors still work, EC writes disabled)

## Requirements

- OS: Windows 10 (1809+) or Windows 11
- Hardware: ThinkPad T480 / T480s (other ThinkPads may need different
  EC register offsets — see `FanControlOptions`)
- Runtime: .NET 8.0 (when running framework-dependent) or none (when
  running the self-contained single-file build)
- Administrator privileges — required for EC writes
- InpOut32 driver — install once with `InstallDriver.exe` (ships with
  InpOut32). See [SETUP.md](SETUP.md).

## Quick Start

### Download
Download the latest `TPFan-Win` artifact from the
[Actions page](https://github.com/Yugicchi/TPFan-Win/actions) (single
`TPFan.GUI.exe`).

### Run
1. Right-click `TPFan.GUI.exe` → **Run as Administrator** (required for
   EC writes; without admin, the app still starts in read-only mode).
2. The system tray icon shows the live CPU temperature.
3. Open the main window for the slider, status panel, and fan-curve
   visualization.
4. Use **Exit Service** in the tray menu to close (fan auto-resets to
   firmware control).

### Build from Source

```bash
git clone https://github.com/Yugicchi/TPFan-Win.git
cd TPFan-Win
dotnet restore TPFan.sln
dotnet build TPFan.sln
dotnet test  TPFan.sln

# Single-binary publish
dotnet publish TPFan.GUI/TPFan.GUI.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  --output ./publish/tpfan

./publish/tpfan/TPFan.GUI.exe
```

## Documentation

- [Architecture Overview](ARCHITECTURE.md) — components, data flow, EC
  override path, single-binary distribution
- [Setup Guide](docs/SETUP.md) — driver install, EC register map, hardware
  verification
- [Fan Curve Model](docs/FAN_CURVE_MODEL.md) — how the temperature→speed
  curve is detected and used
- [Changelog](CHANGELOG.md) — release notes
- [Release Process](docs/RELEASE_PROCESS.md) — how to cut a release

## Project Structure

```
TPFan-Win/
├── TPFan.Shared/      # Data models (FanStatus, FanCurve, FanCurvePoint)
├── TPFan.GUI/         # Single-binary WPF app (UI + tray + hardware)
│   ├── App.xaml(.cs)            # Entry point; wires hardware services
│   ├── MainWindow.xaml(.cs)     # WPF UI
│   ├── ViewModels/              # MainViewModel
│   ├── Hardware/                # All hardware access (EC + sensors)
│   ├── UI/                      # SystemTrayManager
│   └── native/                  # inpoutx64.dll (bundled into publish)
├── TPFan.Tests/       # xUnit unit tests
└── .github/           # CI/CD pipelines
```

## Roadmap

- EC override end-to-end testing on actual T480 hardware
- Custom user-defined fan curves
- Polling rate adjustment
- Optional curve auto-tuning based on observed thermals

## License

MIT License — see [LICENSE](LICENSE)

## Support

- [Issues](https://github.com/Yugicchi/TPFan-Win/issues)
- [Discussions](https://github.com/Yugicchi/TPFan-Win/discussions)

Maintained by [@Yugicchi](https://github.com/Yugicchi)
