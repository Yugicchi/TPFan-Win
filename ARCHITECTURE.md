# ThinkPad T480 Fan Control - Architecture Overview

## Project Structure

```
TPFan-Win/
├── TPFan.Shared/          # Shared models
│   └── Models/
│       ├── FanCurve.cs                 # Mathematical curve data
│       ├── FanCurvePoint.cs            # Single temp→speed point
│       └── FanStatus.cs                # Current fan status
│
├── TPFan.GUI/             # Single-binary WPF app (everything in-process)
│   ├── App.xaml(.cs)                   # Entry point; wires hardware services
│   ├── MainWindow.xaml(.cs)            # WPF main window + fan-curve canvas
│   ├── ViewModels/
│   │   └── MainViewModel.cs            # Polls T480FanProvider directly
│   ├── Hardware/                       # All hardware access lives here
│   │   ├── IFanController.cs           # EC fan-control interface
│   │   ├── EcFanController.cs          # InpOut32 P/Invoke + EC protocol
│   │   ├── FanControlOptions.cs        # EC register map (T480)
│   │   ├── LibreHardwareMonitorSensorService.cs  # CPU temp / fan / RPM
│   │   └── T480FanProvider.cs          # High-level provider (curve, override)
│   ├── UI/
│   │   └── SystemTrayManager.cs        # NotifyIcon with live temp badge
│   ├── native/
│   │   └── inpoutx64.dll               # Bundled in publish/tpfan/ output
│   └── GlobalUsings.cs                 # Resolves WPF/WinForms ambiguities
│
├── TPFan.Tests/           # xUnit unit tests
│   ├── EcFanMappingTests.cs            # percent ↔ level mapping
│   └── SensorClampTests.cs             # Sensor-value validation
│
├── TPFan.sln              # Solution file
└── README.md
```

## Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                        TPFan.GUI.exe (single process)        │
│                                                              │
│  ┌─────────────────────────┐    ┌──────────────────────────┐ │
│  │  WPF MainWindow         │    │  SystemTrayManager        │ │
│  │  - Slider, status,      │    │  - NotifyIcon on STA thr. │ │
│  │    curve canvas         │    │  - Right-click menu       │ │
│  │  - DataBinding to VM    │    │  - Live temp badge        │ │
│  └────────┬────────────────┘    └──────────┬───────────────┘ │
│           │                                  │                │
│           │  Direct in-proc call             │                │
│           ▼                                  ▼                │
│  ┌────────────────────────────────────────────────────────┐  │
│  │            T480FanProvider (in-process)                 │ │
│  │  - GetFanStatusAsync() (temp / RPM / speed / override)  │ │
│  │  - SetFanSpeedOverrideAsync(percent)                    │ │
│  │  - ResetFanOverrideAsync()                              │ │
│  │  - DetectFanCurveAsync()                                │ │
│  └─────┬──────────────────────────────────────┬────────────┘ │
│        │                                       │              │
│        ▼                                       ▼              │
│  ┌─────────────────────┐         ┌──────────────────────────┐ │
│  │ LibreHardwareMonitor│         │  EcFanController         │ │
│  │ SensorService       │         │  (IFanController)        │ │
│  │ - CPU temperature   │         │  - P/Invoke Out32/Inp32  │ │
│  │ - Fan RPM           │         │  - EC protocol 0x66/0x62 │ │
│  │ - Fan percent       │         │  - Registers 0x2F/0x31   │ │
│  └──────────┬──────────┘         └────────────┬─────────────┘ │
└─────────────┼────────────────────────────────┼────────────────┘
              │                                │
              ▼                                ▼
   ┌───────────────────────┐       ┌────────────────────────┐
   │  LibreHardwareMonitor │       │  InpOut32 user-mode    │
   │  (CPU, super-IO,      │       │  shim → kernel driver  │
   │   EC mirror)          │       │  inpoutx64.sys         │
   └───────────────────────┘       │  → EC ports 0x66/0x62  │
                                   └────────────────────────┘
```

No named-pipe IPC. The WPF window and the system tray call
`T480FanProvider` methods directly — they live in the same process.

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

## Fan Override — EC Path (T480)

The hot path for `SetFanSpeedOverrideAsync` is:

```
MainWindow.xaml  ── data binding ──►  MainViewModel
    │
    │  (in-proc method call — no IPC)
    ▼
T480FanProvider.SetFanSpeedOverrideAsync(percent)
    │
    │  validate + record _isOverrideActive / _overridePercent
    ▼
IFanController  →  EcFanController.SetFanSpeedAsync(percent)
    │
    │  MapPercentToLevel(percent) ── using FanControlOptions.MaxLevel=7
    ▼
InpOut32 P/Invoke (Out32 / Inp32) via inpoutx64.dll + inpoutx64.sys
    │
    │  EC command port 0x66, data port 0x62 (ACPI EC protocol)
    ▼
EC offset 0x2F (level 0..7), 0x31 (engagement: 0x00=auto, 0x40=manual)
    → fan spins at the requested level
```

Read path (temperature/RPM) uses `LibreHardwareMonitorSensorService` (LHM
with WMI/ACPI fallback). It does **not** depend on the InpOut32 driver —
so the app runs in read-only mode when not elevated or when the driver
is absent. See [SETUP.md](SETUP.md) "EC Fan Control (T480)".

### ACPI Fan Control (implemented via EC)

- `IFanController` — abstraction boundary for EC access.
- `EcFanController` — InpOut32 implementation + T480 EC register constants
  (0x2F/0x31/0x32, MaxLevel=7). Maps 0..100 percent ↔ 0..7 EC levels.
- `FanControlOptions` — overrides per-BIOS EC offsets if the T480
  revision exposes the fan at a different offset.
- `T480FanProvider` delegates `SetFanSpeedOverrideAsync` /
  `ResetFanOverrideAsync` to the injected `IFanController` and tracks
  the active override on the returned `FanStatus`.
- `App.xaml.cs` constructs `EcFanController` + `LibreHardwareMonitorSensorService`
  + `T480FanProvider` at startup, and static `ProcessExit` / `CancelKeyPress`
  handlers call `ResetFanOverrideAsync()` to return the fan to firmware auto
  control when the GUI closes.

## Single-Binary Distribution

```
dotnet publish TPFan.GUI/TPFan.GUI.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

Produces one `TPFan.GUI.exe` (~174 MB self-contained) that bundles:

- The WPF UI
- `LibreHardwareMonitorLib` + sensor service
- The `EcFanController` (InpOut32 P/Invoke)
- The system-tray manager
- `inpoutx64.dll` (self-extracted to a temp folder on startup)

UX: "buka GUI sudah termasuk buka service" — opening the GUI already
includes the service functionality.

## Build & Run

```bash
# Restore dependencies
dotnet restore TPFan.sln

# Build all projects
dotnet build TPFan.sln

# Run unit tests
dotnet test TPFan.sln

# Publish single binary
dotnet publish TPFan.GUI/TPFan.GUI.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  --output ./publish/tpfan

# Run the published binary (Administrator required for EC writes)
./publish/tpfan/TPFan.GUI.exe
```

## Requirements

- Windows 10 1809+ (build 17763+)
- .NET 8.0 Runtime (when running framework-dependent) or none (self-contained)
- ThinkPad T480 hardware (for actual fan control)
- Administrator privileges (for EC writes)
- InpOut32 driver installed once (signed `inpoutx64.sys`) — see SETUP.md

## References

- [tpfan-ui](https://github.com/dmitry-s93/tpfan-ui) - Reference implementation
- [ThinkPad ACPI Extras](https://www.kernel.org/doc/html/latest/admin-guide/laptops/thinkpad-acpi.html) - Linux driver docs
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - Cross-platform hardware monitoring
- [InpOut32](https://www.highrez.co.uk/downloads/inpout32/) - User-mode I/O port driver
