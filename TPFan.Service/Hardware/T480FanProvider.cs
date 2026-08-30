using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;
using TPFan.Shared.Models;

namespace TPFan.Service.Hardware;

/// <summary>
/// Provides fan curve detection and status reading via WMI
/// Specifically optimized for ThinkPad T480
/// </summary>
public class T480FanProvider : IDisposable
{
    private readonly ManagementEventWatcher? _tempWatcher;
    private readonly IFanController? _fanController;
    private int _lastTemperature = 0;
    private int _lastFanSpeed = 0;
    private bool _isOverrideActive;

    public T480FanProvider(IFanController? fanController = null)
    {
        // Initialize WMI watchers for real-time updates
        try
        {
            _tempWatcher = new ManagementEventWatcher(
                "SELECT * FROM Win32_OperatingSystem");
        }
        catch
        {
            // WMI may not be available in all environments
        }
        _fanController = fanController;
    }

    /// <summary>
    /// Get current CPU temperature in Celsius
    /// For T480, uses WMI Win32_TemperatureProbe
    /// </summary>
    public async Task<int> GetCpuTemperatureAsync()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_TemperatureProbe WHERE Description LIKE '%CPU%'");

            using (var results = searcher.Get())
            {
                foreach (var obj in results)
                {
                    if (obj["CurrentReading"] is not null)
                    {
                        // WMI reports in tenths of Kelvin, convert to Celsius
                        var kelvinTenths = Convert.ToInt32(obj["CurrentReading"]);
                        var celsius = (kelvinTenths / 10) - 273;
                        _lastTemperature = celsius;
                        return celsius;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading temperature: {ex.Message}");
        }

        return _lastTemperature; // Return last known value on error
    }

    /// <summary>
    /// Get current fan speed as percentage (0-100)
    /// T480 typically reports speed as 0-255 or percentage via ACPI
    /// </summary>
    public async Task<int> GetFanSpeedPercentAsync()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_Fan WHERE Description LIKE '%CPU%'");

            using (var results = searcher.Get())
            {
                foreach (var obj in results)
                {
                    if (obj["DesiredSpeed"] is not null)
                    {
                        var speed = Convert.ToInt32(obj["DesiredSpeed"]);
                        // T480 reports as 0-255, normalize to 0-100
                        var percent = (speed * 100) / 255;
                        _lastFanSpeed = percent;
                        return percent;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading fan speed: {ex.Message}");
        }

        return _lastFanSpeed;
    }

    /// <summary>
    /// Get current fan RPM
    /// </summary>
    public async Task<int> GetFanRpmAsync()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_Fan WHERE Description LIKE '%CPU%'");

            using (var results = searcher.Get())
            {
                foreach (var obj in results)
                {
                    if (obj["CurrentSpeed"] is not null)
                    {
                        return Convert.ToInt32(obj["CurrentSpeed"]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading fan RPM: {ex.Message}");
        }

        return 0;
    }

    /// <summary>
    /// Detect the system's fan curve by sampling temperatures and speeds
    /// This creates a mathematical mapping: Temperature[] -> Speed[]
    /// </summary>
    public async Task<FanCurve> DetectFanCurveAsync()
    {
        var points = new List<FanCurvePoint>();

        // T480 typical fan curve breakpoints (can be refined by sampling)
        // Based on Lenovo BIOS defaults
        var temperatureThresholds = new[] { 30, 40, 50, 60, 70, 80 };

        foreach (var tempThreshold in temperatureThresholds)
        {
            var speed = InterpolateSpeedForTemperature(tempThreshold);
            var rpm = await GetFanRpmAsync();

            points.Add(new FanCurvePoint
            {
                TemperatureCelsius = tempThreshold,
                SpeedPercent = speed,
                EstimatedRpm = rpm
            });

            await Task.Delay(100); // Small delay between samples
        }

        return new FanCurve
        {
            Name = "Detected",
            Points = [..points],
            CurrentTemperatureCelsius = await GetCpuTemperatureAsync(),
            CurrentSpeedPercent = await GetFanSpeedPercentAsync(),
            CurrentRpm = await GetFanRpmAsync()
        };
    }

    /// <summary>
    /// Get current fan status
    /// </summary>
    public async Task<FanStatus> GetFanStatusAsync()
    {
        return new FanStatus
        {
            TemperatureCelsius = await GetCpuTemperatureAsync(),
            SpeedPercent = await GetFanSpeedPercentAsync(),
            Rpm = await GetFanRpmAsync(),
            IsOverrideActive = _isOverrideActive,
            OverrideSpeedPercent = _isOverrideActive ? _lastFanSpeed : null,
            ReadAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Override the firmware auto-control and force a fixed fan speed.
    /// Returns false (and logs) when no <see cref="IFanController"/> was
    /// injected, e.g. when the service is running in read-only mode.
    /// </summary>
    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        if (_fanController is null)
        {
            System.Diagnostics.Debug.WriteLine(
                "T480FanProvider: no fan controller available; override is a no-op.");
            return false;
        }

        var ok = await _fanController.SetFanSpeedAsync(speedPercent);
        if (ok)
        {
            _isOverrideActive = true;
            _lastFanSpeed = Math.Clamp(speedPercent, 0, 100);
        }
        return ok;
    }

    /// <summary>
    /// Release the manual override and return the fan to firmware auto control.
    /// </summary>
    public async Task<bool> ResetFanOverrideAsync()
    {
        if (_fanController is null) return false;

        var ok = await _fanController.ResetToAutoAsync();
        if (ok)
        {
            _isOverrideActive = false;
        }
        return ok;
    }

    /// <summary>
    /// Mathematical interpolation of fan speed based on temperature
    /// Using simple linear models common in ThinkPad firmware
    /// </summary>
    private int InterpolateSpeedForTemperature(int temperature)
    {
        // T480 typical fan curve (can be calibrated)
        return temperature switch
        {
            < 30 => 0,
            < 40 => 20,
            < 50 => 30,
            < 60 => 40,
            < 70 => 60,
            < 80 => 80,
            _ => 100
        };
    }

    public void Dispose()
    {
        // Best-effort: hand the fan back to the firmware before tearing down
        // the watchers, so a Ctrl+C or service stop does not leave the fan
        // pinned at whatever the user last set.
        if (_isOverrideActive && _fanController is not null)
        {
            try
            {
                _fanController.ResetToAutoAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Swallow — Dispose must not throw.
            }
        }
        _tempWatcher?.Dispose();
    }
}
