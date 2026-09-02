# Changelog

## [0.4.0] - 2026-09-02

### Added
- EC dirty-shutdown mitigation (hysteresis + safe shutdown sequence)
- Hysteresis tuning: temperature 1°C / time 1 s / minimum 5 per 5 min
- System tray restore on startup (reconnects after process restart)
- Visual revisions: 3-row grid layout, divider, radio-corner styling, slider ZIndex 10
- Fan curve verification (6 points: [20, 30, 40, 60, 80, 100]%)
- Documentation moved to `docs/` (
`SETUP.md`, `FAN_CURVE_MODEL.md`, `RELEASE_PROCESS.md`)
- Known bugs tracked via GitHub issue (see `docs/README.md`)

### Changed
- `TPFan.GUI.csproj` / `TPFan.Shared.csproj` version updated to `0.4.0`
- Project build properties aligned with single-binary distribution (`SelfContained=true`, `PublishSingleFile=true`)

### Fixed
- `DetectFanCurveAsync` verified
- `ResetFanOnExit` on exit
- Single-binary publish (174 MB self-contained with `inpoutx64.dll` bundled)

## [0.3.0] - 2026-09-01

### Added
- Single-binary architecture: merged `TPFan.Service` into `TPFan.GUI` — one `TPFan.GUI.exe` (~77 MB self-contained) runs all service functionality (EC fan control, sensor polling, system tray) as background tasks in-process
- No more Named Pipe IPC — `MainViewModel` calls `T480FanProvider` directly for fan status/override
- `App.xaml.cs` initializes hardware services and starts system tray on its own STA thread in-process
- `GlobalUsings.cs` resolves WinForms/WPF namespace ambiguities (`Brush`, `Color`, `ColorConverter`, `Point`, `Application`)

### Changed
- `EcFanController` / `LibreHardwareMonitorSensorService` / `T480FanProvider` / `SystemTrayManager` moved from `TPFan.Service` to `TPFan.GUI.Hardware` / `TPFan.GUI.UI`
- `TPFan.Service` project removed entirely from solution and disk
- `FanServiceClient`, `ServiceLauncher`, `IFanServiceContract` deleted (IPC layer removed)
- CI workflow (`build.yml`) simplified to build/publish only `TPFan.GUI`
- `inpoutx64.dll` bundled inside single-file binary via `IncludeNativeLibrariesForSelfExtract=true`

### Fixed
- EC write no longer fails due to elevation separation — GUI process itself runs elevated and writes directly
- Fan auto-reset on exit via static `ProcessExit` / `CancelKeyPress` handlers in `App` and `SystemTrayManager`

### Notes
- UX: "buka GUI sudah termasuk buka service" — opening the GUI already includes the service functionality
- Requires running as Administrator for EC writes; without admin, degrades gracefully to read-only monitoring

## [0.2.0] - 2026-08-31

### Added
- ACPI/EC fan control write for ThinkPad T480 (`EcFanController` + `IFanController` + `FanControlOptions` via InpOut32). Override slider now actually drives the fan.
- `T480FanProvider` now exposes `SetFanSpeedOverrideAsync` / `ResetFanOverrideAsync` delegating to `IFanController`; tracks `IsOverrideActive` / `OverrideSpeedPercent` surfaced via `FanStatus`.
- `FanServicePipeServer` validates and delegates fan write/reset to the provider (previously a `Task.Delay` stub).
- `Program.cs` wires `EcFanController`, logs `IsAvailable`, and best-effort resets the fan to firmware auto control on Ctrl+C / `Dispose`.

### Changed
- `SETUP.md`: added "EC Fan Control (T480)" (driver install, DLL placement, admin requirement, bus protocol, EC register table, verification with RWEverything).
- `FAN_CURVE_MODEL.md`: updated Override Behavior section to describe EC hot path.
- `ARCHITECTURE.md`: added "Fan Override — EC Path (T480)" and collapsed prior "Implement ACPI Fan Control" next-step into the new section.
- `TPFan.Service.csproj`: recognizes optional `native/inpoutx64.dll` for copy to output.

### Notes
- Requires `inpoutx64.dll` + signed `inpoutx64.sys` from Highrez (InpOut32) beside the service exe, and an elevated process. Without them, the service degrades gracefully to read-only monitoring.

## [0.1.0] - 2026-08-30

### Added
- Initial project structure
- Shared models (FanCurve, FanCurvePoint, FanStatus)
- Service layer with WMI provider for T480
- UWP interface with slider and snapping
- Settings persistence
- GitHub Actions CI/CD (5 workflows)

### Known Limitations
- System tray pending
- Requires hardware testing on T480 (WMI read already exercised, EC write needs T480 + driver)

### Next
- Test EC write end-to-end on actual T480 hardware
- Add system tray integration
