# Changelog

## [0.1.0] - 2026-08-30

### Added
- Initial project structure
- Shared models (FanCurve, FanCurvePoint, FanStatus)
- Service layer with WMI provider for T480
- UWP interface with slider and snapping
- Settings persistence
- GitHub Actions CI/CD (5 workflows)

### Known Limitations
- ACPI fan control not implemented (read-only)
- System tray pending
- Requires hardware testing on T480

### Next
- Test on actual T480 hardware
- Implement ACPI fan control
- Add system tray integration
