# ThinkPad T480 Fan Control - Architecture Overview

## Project Structure

```
TPFan-Win/
├── TPFan.Shared/          # Shared models & contracts
│   ├── Models/
│   │   ├── FanCurve.cs                 # Mathematical curve data
│   │   ├── FanCurvePoint.cs            # Single temp→speed point
│   │   └── FanStatus.cs                # Current fan status
│   └── Contracts/
│       └── IFanServiceContract.cs      # IPC interface
│
├── TPFan.Service/         # Background service (Win32)
│   ├── Hardware/
│   │   └── T480FanProvider.cs          # WMI fan reading
│   ├── IPC/
│   │   └── FanServicePipeServer.cs     # Named pipe server
│   ├── Program.cs                      # Service entry point
│   └── ARCHITECTURE.md                 # Service docs
│
├── TPFan.UWP/             # UWP UI
│   ├── Views/
│   │   ├── MainPage.xaml               # Main UI
│   │   └── MainPage.xaml.cs
│   ├── ViewModels/
│   │   └── MainViewModel.cs            # UI logic
│   ├── Services/
│   │   ├── FanServiceClient.cs         # IPC client
│   │   └── UserSettingsService.cs      # Settings persistence
│   ├── App.xaml(.cs)                   # App entry
│   └── Package.appxmanifest            # UWP manifest
│
├── TPFan.sln              # Solution file
└── README.md                           # Project README
```

## Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                      User Interface                          │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  MainPage.xaml                                       │   │
│  │  - Display fan status (temp, speed, RPM)             │   │
│  │  - Slider with snap points from curve                │   │
│  │  - Override enable/disable toggle                    │   │
│  └────────────────────┬────────────────────────────────┘   │
│                       │ Data Binding                         │
│  ┌────────────────────▼────────────────────────────────┐   │
│  │  MainViewModel                                       │   │
│  │  - FanCurve, FanStatus, SnapPoints                   │   │
│  │  - SelectedSpeedPercent (with snapping)              │   │
│  └────────────────────┬────────────────────────────────┘   │
└───────────────────────┼─────────────────────────────────────┘
                        │ IPC (Named Pipes)
┌───────────────────────▼─────────────────────────────────────┐
│                   Background Service                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  FanServicePipeServer                                │   │
│  │  - Implements IFanServiceContract                    │   │
│  │  - JSON message serialization                        │   │
│  └────────────────────┬────────────────────────────────┘   │
│                       │ Method Calls                         │
│  ┌────────────────────▼────────────────────────────────┐   │
│  │  T480FanProvider                                     │   │
│  │  - GetCpuTemperatureAsync() via WMI                  │   │
│  │  - GetFanSpeedPercentAsync() via WMI                 │   │
│  │  - DetectFanCurveAsync() → mathematical mapping      │   │
│  └────────────────────┬────────────────────────────────┘   │
└───────────────────────┼─────────────────────────────────────┘
                        │ WMI Queries
┌───────────────────────▼─────────────────────────────────────┐
│                   Windows System                             │
│  - Win32_TemperatureProbe (CPU temp)                         │
│  - Win32_Fan (fan speed, RPM)                                │
│  - ACPI/EC (fan control - future)                            │
└─────────────────────────────────────────────────────────────┘
```

## Fan Curve Mathematical Model

The fan curve is a discrete function:

```
Curve: Temperature[] → Speed[]

Example for T480:
┌──────────────┬────────────┐
│ Temp (°C)    │ Speed (%)  │
├──────────────┼────────────┤
│     30       │     0      │
│     40       │    20      │
│     50       │    30      │
│     60       │    40      │
│     70       │    60      │
│     80       │    80      │
│     90       │   100      │
└──────────────┴────────────┘
```

Interpolation between points uses linear interpolation:

```
speed(t) = speed_lower + (speed_upper - speed_lower) * (t - t_lower) / (t_upper - t_lower)
```

## Slider Snapping

Slider has snap points from the detected curve:

```
SnapPoints = [0, 20, 30, 40, 60, 80, 100]  // From curve

When user drags slider to value X:
  SelectedSpeed = FindClosestSnapPoint(X)
```

This ensures override values align with system-defined curve breakpoints.

## Settings Persistence

Per-user settings stored in:

```
%LOCALAPPDATA%\Packages\TPFan.UWP_xxx\LocalState\
```

Settings:
- `IsOverrideEnabled` - Manual override active
- `OverrideSpeedPercent` - Last selected speed
- `SelectedPreset` - Preset name (for future presets)
- `StartMinimized` - Start in tray
- `MinimizeToTray` - Minimize behavior
- `UpdateIntervalSeconds` - Status refresh rate

## Next Steps

1. **System Tray Integration**
   - Add tray icon with status indicator
   - Right-click menu for quick presets
   - Hover tooltip with current temp/speed

2. **Curve Calibration**
   - Stress test to verify detected curve accuracy
   - Allow user to adjust curve points
   - Save custom curves

3. **Testing on T480 Hardware**
   - Verify WMI queries work on actual hardware
   - Calibrate temperature thresholds
   - Test fan control stability (incl. EC override end-to-end)

## Fan Override — EC Path (T480)

The new hot path for `SetFanSpeedOverrideAsync` is:

```
MainPage.xaml → MainViewModel (slider snap)
    → FanServiceClient (Named Pipe, JSON)
    → FanServicePipeServer.SetFanSpeedOverrideAsync
        → T480FanProvider.SetFanSpeedOverrideAsync (validates, tracks _isOverrideActive)
            → IFanController / EcFanController.SetFanSpeedAsync
                → InpOut32 P/Invoke (Out32/Inp32) via inpoutx64.dll + inpoutx64.sys
                → EC command port 0x66 / data port 0x62 (ACPI EC protocol)
                → EC offset 0x2F (level), 0x31 (mode), 0x32 (reset)
            → level ↔ percent mapping (FanControlOptions.MaxLevel=7, 0..100)
        → FanStatus.IsOverrideActive / OverrideSpeedPercent updated
```

Read path (temperature/RPM) still goes via WMI `Win32_TemperatureProbe` / `Win32_Fan`
and does not depend on the EC driver — the service can therefore run in read-only
mode when not elevated or when the driver is absent. See SETUP.md "EC Fan Control
(T480)".

### ACPI Fan Control (implemented via EC)

- `IFanController` — abstraction boundary for EC access.
- `EcFanController` — InpOut32 implementation + T480 EC register constants
  (0x2F/0x31/0x32, MaxLevel=7). Maps 0..100 percent ↔ 0..7 EC levels.
- `FanControlOptions` — overrides per-BIOS EC offsets if the T480 revision
  exposes the fan at a different offset.
- `FanServicePipeServer` now delegates instead of returning a stub, and
  validates `0..100`.
- `Program.cs` creates `EcFanController`, logs `IsAvailable`, and hands Ctrl+C
  back to firmware auto control (`ResetToAutoAsync` from `Dispose`).

## Build & Run

```bash
# Restore dependencies
dotnet restore TPFan.sln

# Build all projects
dotnet build TPFan.sln

# Run service (for testing)
cd TPFan.Service
dotnet run

# Package UWP app
# Open in Visual Studio → Create App Packages
```

## Requirements

- Windows 10 1809+ (build 17763+)
- .NET 8.0 Runtime
- ThinkPad T480 hardware (for actual fan control)
- Administrator privileges (for ACPI control)

## References

- [tpfan-ui](https://github.com/dmitry-s93/tpfan-ui) - Reference implementation
- [ThinkPad ACPI Extras](https://www.kernel.org/doc/html/latest/admin-guide/laptops/thinkpad-acpi.html) - Linux driver docs
- [WMI Fan Class](https://docs.microsoft.com/en-us/windows/win32/cimwin32prov/win32-fan) - Windows WMI docs
