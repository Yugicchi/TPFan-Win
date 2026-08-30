# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability, please:

1. **Do NOT** open a public issue
2. Email the maintainer directly
3. Include:
   - Description of vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

## Security Considerations

### Hardware Access
This application requires:
- WMI access for reading temperature/fan data
- Potential ACPI/EC access for fan control
- Administrator privileges for certain operations

### Risks
- **Thermal damage**: Incorrect fan control could cause overheating
- **Hardware stress**: Constant fan override may reduce fan lifespan
- **System stability**: Interfering with hardware control can cause instability

### Mitigations
- Fail-safe defaults (auto mode if service crashes)
- Temperature monitoring with auto-shutdown
- User confirmation for potentially dangerous operations
- Detailed logging for troubleshooting

## Best Practices

1. **Never** force 100% fan speed for extended periods
2. **Always** monitor temperatures when using manual override
3. **Test** on non-critical systems first
4. **Backup** important data before testing new versions

## Disclaimer

This software is provided "as is" without warranty. Use at your own risk. The maintainers are not responsible for any hardware damage or data loss.
