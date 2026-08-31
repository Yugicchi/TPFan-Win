using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace TPFan.Service.Hardware;

/// <summary>
/// Reads CPU temperature, fan speed &amp; RPM via LibreHardwareMonitor.
/// Works across laptops/desktops without hard-coding EC offsets: the
/// library probes Super I/O, EC and chipset sensors at runtime.
/// </summary>
public sealed class LibreHardwareMonitorSensorService : IDisposable
{
    private readonly Computer _computer;
    private bool _initialized;

    public LibreHardwareMonitorSensorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsGpuEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false
        };
        try
        {
            _computer.Open();
            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LHM open failed: {ex.Message}");
        }
    }

    public bool IsAvailable => _initialized;

    /// <summary>All CPU temperature sensors found (Package, per-core).</summary>
    public float? ReadCpuTemperatureC()
    {
        if (!_initialized) return null;
        Refresh();
        var temps = _computer.Hardware
            .SelectMany(ExpandHardware)
            .Where(h => h.HardwareType is HardwareType.Cpu)
            .SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is not null)
            .ToList();

        // Prefer "Core Max / Package / CPU Package" over individual cores
        var preferred = temps.FirstOrDefault(s =>
            s.Name is "Core Max" or "CPU Package" or "Package");
        var val = (preferred ?? temps.FirstOrDefault())?.Value;
        return val;
    }

    /// <summary>First control/fan sensor that looks like CPU fan (%).</summary>
    public float? ReadFanControlPercent()
    {
        if (!_initialized) return null;
        Refresh();
        var controls = _computer.Hardware
            .SelectMany(ExpandHardware)
            .SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Control && s.Value is not null)
            .ToList();
        // Prefer names like Fan Control, Fan #1
        var best = controls.FirstOrDefault(s =>
            s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
            ?? controls.FirstOrDefault();
        return best?.Value;
    }

    /// <summary>First fan RPM sensor.</summary>
    public float? ReadFanRpm()
    {
        if (!_initialized) return null;
        Refresh();
        var fans = _computer.Hardware
            .SelectMany(ExpandHardware)
            .SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Fan && s.Value is not null)
            .ToList();
        var best = fans.FirstOrDefault(s =>
            s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
            ?? fans.FirstOrDefault();
        return best?.Value;
    }

    private void Refresh()
    {
        foreach (var hw in _computer.Hardware)
            hw.Update();
        foreach (var hw in _computer.Hardware)
            foreach (var sub in hw.SubHardware)
                sub.Update();
    }

    private static System.Collections.Generic.IEnumerable<IHardware> ExpandHardware(IHardware hw)
    {
        yield return hw;
        foreach (var sub in hw.SubHardware)
            foreach (var h in ExpandHardware(sub))
                yield return h;
    }

    public void Dispose() => _computer.Close();
}
