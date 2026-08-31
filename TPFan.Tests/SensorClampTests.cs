using TPFan.Service.Hardware;

namespace TPFan.Tests;

/// <summary>
/// Regression harness for the SensorService's last-resort guard logic:
///
///  * Fan percent values below 0 or above 100 should be treated as
///    missing (the T480 EC mirror register bug showed this with a `1829%`
///    readback). Similar reasoning applies to fan RPM values.
///  * The 0°C thermal reading bug showed that the mapping from raw
///    performance counter (`Temperature` in tenths of Celsius) to a displayed
///    Celsius value is non-trivial, so we keep that scalar visible here too.
///
/// The static predicate can be exercised without instantiating the real
/// `LibreHardwareMonitorSensorService` (which pulls native DLLs and requires
/// elevated privileges), yet it still covers the guard that protects the UI
/// from bogus values at the boundary between the service and the IPC model
/// `FanStatus`.
/// </summary>
public class SensorClampTests
{
    // ------------------------------------------------------------------------
    // A. Fan percent clamping
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, true)]
    [InlineData(20f, true)]
    [InlineData(50f, true)]
    [InlineData(100f, true)]
    [InlineData(100.01f, false)]
    [InlineData(-0.01f, false)]
    [InlineData(1829f, false)]
    public void FanPercent_IsConsideredMild_WhenWithinZeroToHundred(float percent, bool expected)
    {
        Assert.Equal(expected, LibreHardwareMonitorSensorService.IsFanPercentValid(percent));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(4500f, true)]
    [InlineData(-1f, false)]
    [InlineData(float.NegativeInfinity, false)]
    public void FanRpm_IsConsideredMild_WhenNonNegative(float rpm, bool expected)
    {
        Assert.Equal(expected, LibreHardwareMonitorSensorService.IsFanRpmValid(rpm));
    }

    // ------------------------------------------------------------------------
    // B. Performance counter → Celsius scalar
    // ------------------------------------------------------------------------

    /// <summary>
    /// The `Win32_PerfFormattedData_Counters_ThermalZoneInformation`
    /// `Temperature` counter reports tenths of a degree Celsius (e.g. raw
    /// `325` = 32.5 °C). Historically the raw value was once divided by 10
    /// and rounded down to `0` or treated as Kelvin and produced wildly
    /// wrong readings. We keep that background here for regression coverage.
    /// </summary>
    [Theory]
    [InlineData(320u, 32f)]
    [InlineData(325u, 32.5f)]
    [InlineData(500u, 50f)]
    [InlineData(0u, 0f)]
    public void ThermalZoneRaw_To_Celsius_DividesByTen(uint raw, float expected)
    {
        var celsius = LibreHardwareMonitorSensorService.ThermalZoneRawToCelsius(raw);
        // Equality with floats is intentional — the scalar is exact.
        Assert.Equal(expected, celsius);
    }
}
