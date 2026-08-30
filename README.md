# TPFan-Win

[![Build Status](https://github.com/Yugicchi/TPFan-Win/workflows/Build%20and%20Test/badge.svg)](https://github.com/Yugicchi/TPFan-Win/actions)
[![Security](https://github.com/Yugicchi/TPFan-Win/workflows/CodeQL%20Analysis/badge.svg)](https://github.com/Yugicchi/TPFan-Win/security)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Lightweight UWP application for ThinkPad T480 fan control.

## Features

- Real-time monitoring - CPU temperature, fan speed, RPM
- Fan curve detection - Mathematical mapping from system
- Manual override - Slider with smart snapping
- Settings persistence - Per-user configuration
- Lightweight - Minimal resource usage

## Requirements

- OS: Windows 10 (1809+) or Windows 11
- Hardware: ThinkPad T480 / T480s
- Runtime: .NET 8.0

## Quick Start

### Download
Download from [Releases](https://github.com/Yugicchi/TPFan-Win/releases).

### Build from Source
```bash
git clone https://github.com/Yugicchi/TPFan-Win.git
cd TPFan-Win
dotnet restore
dotnet build
```

## Documentation

- [Architecture Overview](ARCHITECTURE.md)
- [Setup Guide](SETUP.md)
- [Fan Curve Model](FAN_CURVE_MODEL.md)
- [CI/CD Guide](CI_CD.md)

## Project Structure

```
TPFan-Win/
├── TPFan.Shared/      # Data models
├── TPFan.Service/     # Background service
├── TPFan.UWP/         # User interface
└── .github/           # CI/CD pipelines
```

## Roadmap

- System tray integration
- Hardware testing on T480 (EC override end-to-end)
- Unit tests (mapping / WMI / IPC serialization)
- Curve calibration + custom curves
- MSIX signed release

## License

MIT License - see [LICENSE](LICENSE)

## Support

- [Issues](https://github.com/Yugicchi/TPFan-Win/issues)
- [Discussions](https://github.com/Yugicchi/TPFan-Win/discussions)

Maintained by [@Yugicchi](https://github.com/Yugicchi)
