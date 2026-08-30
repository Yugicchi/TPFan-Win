# Changelog

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
