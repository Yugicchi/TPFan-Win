using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace TPFan.GUI.Hardware;

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
            Debug.WriteLine($"LHM open failed: {ex.Message}");
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
        Diag.Log($"[LHM] CPU temps: {temps.Count} sensors: {string.Join(", ", temps.Select(s => $"{s.Name}={s.Value}"))}");

        var withValue = temps.Where(s => s.Value is not null).ToList();
        // Prefer "Core Max / Package / CPU Package" over individual cores
        var preferred = withValue.FirstOrDefault(s =>
            s.Name is "Core Max" or "CPU Package" or "Package");
        var lhm = (preferred ?? withValue.FirstOrDefault())?.Value;
        if (lhm is not null)
        {
            Diag.Log($"[LHM] Using LHM sensor: {preferred?.Name ?? withValue.FirstOrDefault()?.Name ?? "unknown"} = {lhm:0.0}°C");
            return lhm;
        }

        // VBS / Hyper-V blocks the MSR reads that LHM relies on for Intel CPU
        // temps, so on those machines every LHM CPU temperature sensor is null.
        // Fall back to the ACPI thermal zone — the standard kernel counter
        // `Win32_PerfFormattedData_Counters_ThermalZoneInformation.Temperature`
        // is reported in tenths of a degree Celsius (e.g. 366 -> 36.6 °C) and
        // does not need MSR. This is the same source HWMonitor falls back to
        // for the "\\_TZ.THM0" line in its report.
        var ac = ReadAcpiThermalZoneCelsius();
        if (ac is not null)
        {
            Diag.Log(
                "[LHM] CPU temperatures empty (likely VBS / Hyper-V blocking MSR) — " +
                $"falling back to ACPI thermal-zone: {ac:0.0}°C.");
            return ac;
        }
        Diag.Log("[LHM] ACPI thermal zone also returned null — no temperature source available.");
        return null;
    }

    /// <summary>
    /// Best-effort ACPI thermal-zone read for use as a fallback when LHM has
    /// no usable CPU temperature (e.g. VBS is blocking MSR). Returns the
    /// hottest active zone in degrees Celsius, or <c>null</c> if the WMI
    /// query fails (typically because the process is not elevated).
    /// </summary>
    private static float? ReadAcpiThermalZoneCelsius()
    {
        try
        {
            // IMPORTANT: Win32_PerfFormattedData_* lives in root\CIMV2, not
            // root\WMI. (root\WMI hosts the separate MSAcpi_ThermalZone* class
            // which requires a different query and different scaling.) Using
            // the wrong scope silently returns 0 rows.
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT Name, Temperature FROM " +
                "Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            float hottest = float.NaN;
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw = obj["Temperature"];
                if (raw is null) continue;
                var t = ThermalZoneRawToCelsius(Convert.ToUInt32(raw));
                if (float.IsNaN(hottest) || t > hottest) hottest = t;
            }
            if (!float.IsNaN(hottest))
            {
                Debug.WriteLine($"LHM fallback -> ACPI thermal zone: {hottest:0.0} °C");
                return hottest;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LHM ACPI fallback failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Public guard used by the unit tests: a fan percent value is only
    /// displayable when it lies in the inclusive <c>0..100</c> range. The
    /// T480 EC mirror register bug that produced <c>1829 %</c> was caused
    /// by skipping this check; production code must consume this
    /// predicate (or a duplicate inline check) before publishing a value
    /// to the UI.
    /// </summary>
    public static bool IsFanPercentValid(float percent) =>
        !float.IsNaN(percent) && percent >= 0f && percent <= 100f;

    /// <summary>Mirror of <see cref="IsFanPercentValid"/> for fan RPM.</summary>
    public static bool IsFanRpmValid(float rpm) =>
        !float.IsNaN(rpm) && !float.IsInfinity(rpm) && rpm >= 0f;

    /// <summary>
    /// Convert the raw <c>Temperature</c> counter value (tenths of a
    /// degree Celsius) to Celsius. Hoisted to a public static method so
    /// the unit tests can lock the scalar — historically this was a
    /// silent bug (treating the raw value as Kelvin produced &gt;90 °C
    /// on a 51 °C CPU).
    /// </summary>
    public static float ThermalZoneRawToCelsius(uint rawTenths) => rawTenths / 10f;

    /// <summary>
    /// Best-effort fan duty cycle percentage. Order:
    ///   1. LibreHardwareMonitor (any Control sensor with a "Fan" in the name).
    ///   2. Lenovo WMI in <c>root\WMI</c> (LenovoFan / IdeaFan / FanDevice)
    ///      — present when Lenovo Vantage, Energy Management, or the
    ///      <c>LnvWmiEvent</c> driver is installed. Returns 0..100.
    ///
    /// We deliberately do <b>not</b> fall back to the EC readback register:
    /// on T480 the mirror register at 0x2F echoes <c>0x00</c> / <c>0x80</c>
    /// / <c>0xFF</c> depending on timing and not the level we wrote, which
    /// produced bogus values like "1829 %" in the UI.
    /// </summary>
    public float? ReadFanControlPercent()
    {
        if (_initialized)
        {
            Refresh();
            var controls = _computer.Hardware
                .SelectMany(ExpandHardware)
                .SelectMany(h => h.Sensors)
                .Where(s => s.SensorType == SensorType.Control)
                .ToList();
            Diag.Log($"[LHM] Controls: {controls.Count} sensors: {string.Join(", ", controls.Select(s => $"{s.Name}={s.Value}"))}");
            var withValue = controls.Where(s => s.Value is not null).ToList();
            var best = withValue.FirstOrDefault(s =>
                s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
                ?? withValue.FirstOrDefault();
            if (best?.Value is { } v && v is >= 0f and <= 100f) return v;
        }

        var lenovo = ReadLenovoFanPercent();
        if (lenovo is { } lp && lp is >= 0f and <= 100f) return lp;

        // EC readback is intentionally NOT used: see XML doc above.
        return null;
    }

    /// <summary>First fan RPM sensor.</summary>
    public float? ReadFanRpm()
    {
        if (_initialized)
        {
            Refresh();
            var fans = _computer.Hardware
                .SelectMany(ExpandHardware)
                .SelectMany(h => h.Sensors)
                .Where(s => s.SensorType == SensorType.Fan)
                .ToList();
            Diag.Log($"[LHM] Fans: {fans.Count} sensors: {string.Join(", ", fans.Select(s => $"{s.Name}={s.Value}"))}");
            var withValue = fans.Where(s => s.Value is not null).ToList();
            var best = withValue.FirstOrDefault(s =>
                s.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
                ?? withValue.FirstOrDefault();
            if (best?.Value is { } v && v >= 0f) return v;
        }

        return ReadLenovoFanRpm();
    }

    /// <summary>
    /// Probe Lenovo's WMI fan class. The namespace and class name vary by
    /// driver version; we try the documented ones in order and accept the
    /// first one that returns numeric data. Returns <c>null</c> if the
    /// driver isn't installed (very common — Lenovo Vantage is optional).
    /// </summary>
    private static float? ReadLenovoFanPercent()
    {
        foreach (var (ns, cls) in new[]
                 {
                     (@"root\WMI", "Lenovo_Fan"),
                     (@"root\WMI", "IdeaFan"),
                     (@"root\WMI", "LEN_FANSTATUS"),
                 })
        {
            var v = QueryNumericField(ns, cls, new[] { "FanSpeedPercent", "Percentage", "Speed" });
            if (v is { } p && p is >= 0f and <= 100f) return p;
        }
        return null;
    }

    private static float? ReadLenovoFanRpm()
    {
        foreach (var (ns, cls) in new[]
                 {
                     (@"root\WMI", "Lenovo_Fan"),
                     (@"root\WMI", "IdeaFan"),
                     (@"root\WMI", "LEN_FANSTATUS"),
                 })
        {
            var v = QueryNumericField(ns, cls, new[] { "FanSpeedRpm", "RPM", "Speed", "CurrentSpeed" });
            if (v is { } r && r >= 0f) return r;
        }
        return null;
    }

    private static float? QueryNumericField(string scope, string className, string[] fieldNames)
    {
        try
        {
            // SELECT * is fine here: Lenovo WMI classes are tiny (one row),
            // and we don't know which of the candidate field names exists.
            using var searcher = new ManagementObjectSearcher(scope, $"SELECT * FROM {className}");
            foreach (ManagementObject obj in searcher.Get())
            {
                foreach (var f in fieldNames)
                {
                    var raw = obj[f];
                    if (raw is null) continue;
                    try
                    {
                        return Convert.ToSingle(raw);
                    }
                    catch
                    {
                        // Wrong type for this field; try the next.
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // Class not registered in this scope — the driver isn't installed.
            // Expected and harmless; keep trying the next class.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WMI probe {scope}\\{className} failed: {ex.Message}");
        }
        return null;
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
        Diag.Log(msg);
        foreach (var s in hw.Sensors)
        {
            var sensorMsg = $"{prefix}  SENSOR: {s.SensorType} {s.Name} = {s.Value}";
            Diag.Log(sensorMsg);
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