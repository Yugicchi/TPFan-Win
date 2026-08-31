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
    private bool _dumpedOnce;

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
            .Where(s => s.SensorType == SensorType.Temperature)
            .ToList();
        System.Diagnostics.Debug.WriteLine($"LHM temps (cpu): {temps.Count} sensors: {string.Join(", ", temps.Select(s => $"{s.Name}={s.Value}"))}");

        var withValue = temps.Where(s => s.Value is not null).ToList();
        // Prefer "Core Max / Package / CPU Package" over individual cores
        var preferred = withValue.FirstOrDefault(s =>
            s.Name is "Core Max" or "CPU Package" or "Package");
        var val = (preferred ?? withValue.FirstOrDefault())?.Value;
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
            .Where(s => s.SensorType == SensorType.Control)
            .ToList();
        System.Diagnostics.Debug.WriteLine($"LHM controls: {controls.Count} sensors: {string.Join(", ", controls.Select(s => $"{s.Name}={s.Value}"))}");
        var withValue = controls.Where(s => s.Value is not null).ToList();
        var best = withValue.FirstOrDefault(s =>
            s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
            ?? withValue.FirstOrDefault();
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
            .Where(s => s.SensorType == SensorType.Fan)
            .ToList();
        System.Diagnostics.Debug.WriteLine($"LHM fans: {fans.Count} sensors: {string.Join(", ", fans.Select(s => $"{s.Name}={s.Value}"))}");
        var withValue = fans.Where(s => s.Value is not null).ToList();
        var best = withValue.FirstOrDefault(s =>
            s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
            ?? withValue.FirstOrDefault();
        return best?.Value;
    }

    private void Refresh()
    {
        foreach (var hw in _computer.Hardware)
            hw.Update();
        foreach (var hw in _computer.Hardware)
            foreach (var sub in hw.SubHardware)
                sub.Update();

        // First-time dump so we can see what the library actually
        // exposes on this machine. Helps narrow down why a sensor
        // appears empty (driver issue vs library issue).
        if (!_dumpedOnce)
        {
            _dumpedOnce = true;
            foreach (var hw in _computer.Hardware)
            {
                Dump(hw, 0);
                foreach (var sub in hw.SubHardware)
                    Dump(sub, 1);
            }
        }
    }

    private static void Dump(LibreHardwareMonitor.Hardware.IHardware hw, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var msg = $"{prefix}HW: {hw.HardwareType} {hw.Name} (identifier={hw.Identifier})";
        System.Diagnostics.Debug.WriteLine(msg);
        // Write to stdout as well so `dotnet run --configuration Release` exposes it
        Console.WriteLine(msg);
        foreach (var s in hw.Sensors)
        {
            var sensorMsg = $"{prefix}  SENSOR: {s.SensorType} {s.Name} = {s.Value}";
            System.Diagnostics.Debug.WriteLine(sensorMsg);
            Console.WriteLine(sensorMsg);
        }
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
