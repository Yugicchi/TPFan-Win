# Fan Curve Mathematical Model

## Konsep Dasar

Fan curve adalah **fungsi matematis diskrit** yang memetakan temperature CPU ke kecepatan fan:

```
Curve: Temperature (°C) → Fan Speed (%)
```

## Representasi Data

```csharp
// Single point dalam curve
record FanCurvePoint
{
    int TemperatureCelsius    // Input: suhu CPU
    int SpeedPercent         // Output: kecepatan fan (0-100)
    int? EstimatedRpm        // Optional: RPM saat speed ini
}

// Complete curve
record FanCurve
{
    string Name                              // Nama curve
    FanCurvePoint[] Points                   // Array of points (sorted by temp)
    int CurrentTemperatureCelsius            // Suhu saat ini
    int CurrentSpeedPercent                  // Speed saat ini
    int CurrentRpm                           // RPM saat ini
}
```

## Typical T480 Fan Curve

| Temperature (°C) | Fan Speed (%) | Est. RPM |
|------------------|---------------|----------|
| 30               | 0             | 0        |
| 40               | 20            | 1500     |
| 50               | 30            | 2200     |
| 60               | 40            | 2900     |
| 70               | 60            | 3800     |
| 80               | 80            | 4500     |
| 90               | 100           | 5200     |

## Linear Interpolation

Untuk temperature di antara dua points:

```csharp
speed(t) = speed_lower + (speed_upper - speed_lower) * (t - t_lower) / (t_upper - t_lower)
```

Example: t = 55°C
- Lower point: (50, 30)
- Upper point: (60, 40)
- speed(55) = 30 + (40-30) * (55-50) / (60-50) = 30 + 10 * 0.5 = 35%

## Slider Snapping Logic

```csharp
// Get snap points dari curve
SnapPoints = [0, 20, 30, 40, 60, 80, 100]

// User drag slider ke value X
// Find closest snap point
SelectedSpeed = FindClosestSnapPoint(X)

FindClosestSnapPoint(value):
    return snapPoints.MinBy(s => Math.Abs(s - value))
```

## Override Behavior

Ketika user mengaktifkan manual override:

1. **Slider value di-snap** ke snap point terdekat dari curve
2. **Value dikirim ke hardware layer** via direct in-proc call (`MainViewModel` → `T480FanProvider.SetFanSpeedOverrideAsync`)
3. **Hardware layer apply override** via `T480FanProvider.SetFanSpeedOverrideAsync` → `EcFanController.SetFanSpeedAsync` → InpOut32 (`Out32`) → EC port 0x62/0x66 → register `0x2F` (level 0..7); `0x31` dialihkan ke manual mode (`0x40`).
4. **Sistem reports** speed sesuai override; `FanStatus.IsOverrideActive = true` dan `OverrideSpeedPercent` merefleksikan target. Toggle off memanggil `ResetFanOverrideAsync` → `0x32`=0x00 + `0x31`=0x00 (kembalikan auto).

Tanpa driver InpOut32 atau tanpa Administrator, override adalah no-op (returns `false`) dan UI tetap di read-only mode. Lihat SETUP.md bagian "EC Fan Control (T480)".

## Sensor Sources (What Feeds the Curve)

| Signal | Primary source | Fallback | Notes |
|--------|----------------|----------|-------|
| CPU temp | LibreHardwareMonitor (RDMSR) | `root\CIMV2\Win32_PerfFormattedData_Counters_ThermalZoneInformation` (`Temperature/10`, °C) — same counter HWMonitor uses for `\_TZ.THM0` | Falls back when VBS/Hyper-V traps MSR reads; see SETUP.md "Troubleshooting." |
| Fan %  | LibreHardwareMonitor `Control` | `root\WMI\Lenovo_Fan` (Lenovo Vantage driver) | Deliberately **not** EC register `0x2F` — its mirror value echoes `0x00/0x80/0xFF` and produced `1829%`. |
| Fan RPM| LibreHardwareMonitor `Fan`     | `root\WMI\Lenovo_Fan` | Same as fan % |

All fan % values are clamped `0..100`; RPM is clamped `≥0`. Unknown values surface as `0` rather than garbage.

## Curve Detection Algorithm

```csharp
async Task<FanCurve> DetectFanCurveAsync()
{
    // T480 default thresholds (dapat di-calibrate)
    int[] thresholds = [30, 40, 50, 60, 70, 80, 90];
    
    foreach (var threshold in thresholds)
    {
        // Get current state
        var temp = await GetCpuTemperature();
        var speed = InterpolateSpeedForTemperature(threshold);
        var rpm = await GetFanRpm();
        
        points.Add(new FanCurvePoint
        {
            TemperatureCelsius = threshold,
            SpeedPercent = speed,
            EstimatedRpm = rpm
        });
        
        await Task.Delay(100); // Sampling delay
    }
    
    return new FanCurve { Points = points, ... };
}
```

## Use Cases

### 1. Silent Mode
- Override ke 0-20% (hemat baterai, noise minimal)
- CPU mungkin thermal throttle tapi acceptable

### 2. Balanced (Default)
- Auto mode mengikuti curve
- Sistem handle sendiri

### 3. Performance
- Override ke 80-100%
- Maksimal cooling untuk sustained workloads

### 4. Custom
- User pilih snap point yang sesuai
- Auto-snap ke nilai curve yang valid

## Mathematical Properties

- **Monotonic**: Speed tidak pernah turun saat temperature naik
- **Continuous**: Interpolasi linear memberikan nilai kontinyu
- **Bounded**: Speed selalu 0-100%, temperature 0-100°C
- **Discrete**: Snap points adalah subset dari {0, 5, 10, ..., 100}
