using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TPFan.Shared.Models;

namespace TPFan.GUI.Hardware;

/// <summary>
/// Provides fan curve detection and status reading on Windows laptops.
/// Temperature, fan RPM and fan % are read through
/// <see cref="LibreHardwareMonitorSensorService"/> (the same library
/// HWMonitor uses). It probes Super I/O / EC / chipset sensors at
/// runtime so the same code path works for ThinkPad T480, future
/// laptops, and any other PC the user moves to.
///
/// EC write override (when available) is delegated to
/// <see cref="IFanController"/> so the user can still pin a manual
/// fan speed regardless of what firmware path the sensors are on.
/// </summary>
public class T480FanProvider : IDisposable
{
    private readonly LibreHardwareMonitorSensorService _sensors;
    private readonly IFanController? _fanController;
    private int _lastTemperature;
    private int _lastFanSpeed;
    private bool _isOverrideActive;

    public T480FanProvider(
        LibreHardwareMonitorSensorService sensors,
        IFanController? fanController = null)
    {
        _sensors = sensors;
        _fanController = fanController;
    }

    /// <summary>
    /// Get current CPU temperature in Celsius. Returns the last known
    /// value on failure so a transient LHM hiccup does not flicker the
    /// UI down to 0.
    /// </summary>
    public async Task<int> GetCpuTemperatureAsync()
    {
        var t = await Task.Run(() => _sensors.ReadCpuTemperatureC()).ConfigureAwait(false);
        if (t is { } v)
            _lastTemperature = (int)Math.Round(v);
        return _lastTemperature;
    }

    /// <summary>
    /// Get current fan speed as percentage (0-100).
    /// When LHM has no Fan Control sensor, fall back to whatever
    /// override value is currently active.
    /// </summary>
    public async Task<int> GetFanSpeedPercentAsync()
    {
        var p = await Task.Run(() => _sensors.ReadFanControlPercent()).ConfigureAwait(false);
        if (p is { } v)
        {
            _lastFanSpeed = (int)Math.Round(v);
            return _lastFanSpeed;
        }
        // Fallback 1: EC readback (less accurate but better than 0)
        if (_fanController is not null)
        {
            var ec = await _fanController.GetFanSpeedPercentAsync();
            if (ec >= 0) return ec;
        }
        // Fallback 2: active override value
        return _isOverrideActive ? _lastFanSpeed : 0;
    }

    /// <summary>Get current fan RPM.</summary>
    public async Task<int> GetFanRpmAsync()
    {
        // 1. Try LHM or Lenovo WMI first
        var r = await Task.Run(() => _sensors.ReadFanRpm()).ConfigureAwait(false);
        if (r is { } v && v > 0)
            return (int)Math.Round(v);

        // 2. If LHM/WMI has no reading, read real hardware tachometer from ThinkPad EC registers 0x84/0x85!
        if (_fanController is not null)
        {
            var ecRpm = await _fanController.GetFanRpmAsync();
            if (ecRpm is { } realRpm)
                return realRpm;
        }

        // 3. Last fallback: estimate from active override or return 0
        return _isOverrideActive ? EstimateRpmFromPercent(_lastFanSpeed) : 0;
    }

    /// <summary>
    /// Detect the system's fan curve by sampling temperatures and speeds.
    /// Uses T480 typical firmware thresholds; the actual curve is held
    /// in the firmware/EC and the values are mostly used to drive the
    /// slider snap-points in the UI.
    /// </summary>
    public async Task<FanCurve> DetectFanCurveAsync()
    {
        var points = new List<FanCurvePoint>();
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
            await Task.Delay(100);
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

    public async Task<bool> SetFanSpeedOverrideAsync(int speedPercent)
    {
        if (_fanController is null)
        {
            Console.WriteLine("[Provider] SetFanSpeedOverrideAsync: _fanController is NULL");
            return false;
        }
        Console.WriteLine($"[Provider] SetFanSpeedOverrideAsync({speedPercent}%) -> delegating to fan controller...");
        var ok = await _fanController.SetFanSpeedAsync(speedPercent);
        if (ok)
        {
            _isOverrideActive = true;
            _lastFanSpeed = Math.Clamp(speedPercent, 0, 100);
            Console.WriteLine($"[Provider] Override engaged: speedPercent={_lastFanSpeed}%, IsOverrideActive=True");
        }
        else
        {
            Console.WriteLine($"[Provider] Override FAILED for speedPercent={speedPercent}%");
        }
        return ok;
    }

    public async Task<bool> ResetFanOverrideAsync()
    {
        if (_fanController is null)
        {
            Console.WriteLine("[Provider] ResetFanOverrideAsync: _fanController is NULL");
            return false;
        }
        Console.WriteLine("[Provider] ResetFanOverrideAsync: resetting override to auto...");
        var ok = await _fanController.ResetToAutoAsync();
        if (ok)
        {
            _isOverrideActive = false;
            Console.WriteLine("[Provider] Override reset to auto: IsOverrideActive=False");
        }
        else
        {
            Console.WriteLine("[Provider] Reset to auto FAILED");
        }
        return ok;
    }

    private static int EstimateRpmFromPercent(int percent) =>
        (int)Math.Round(percent / 100.0 * 5200);

    private static int InterpolateSpeedForTemperature(int temperature) =>
        temperature switch
        {
            < 30 => 0,
            < 40 => 20,
            < 50 => 30,
            < 60 => 40,
            < 70 => 60,
            < 80 => 80,
            _ => 100
        };

    public void Dispose()
    {
        if (_isOverrideActive && _fanController is not null)
        {
            try { _fanController.ResetToAutoAsync().GetAwaiter().GetResult(); }
            catch { /* Dispose must not throw. */ }
        }
    }
}