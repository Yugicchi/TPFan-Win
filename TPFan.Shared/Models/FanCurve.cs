using System;
using System.Linq;

namespace TPFan.Shared.Models;

/// <summary>
/// Represents the complete fan curve for the system
/// </summary>
public record FanCurve
{
    /// <summary>
    /// Name of the curve (e.g., "Default", "Silent", "Performance")
    /// </summary>
    public string Name { get; init; } = "Default";

    /// <summary>
    /// Curve points sorted by temperature (ascending)
    /// </summary>
    public FanCurvePoint[] Points { get; init; } = [];

    /// <summary>
    /// Current CPU temperature in Celsius
    /// </summary>
    public int CurrentTemperatureCelsius { get; init; }

    /// <summary>
    /// Current fan speed as percentage (0-100)
    /// </summary>
    public int CurrentSpeedPercent { get; init; }

    /// <summary>
    /// Current fan RPM
    /// </summary>
    public int CurrentRpm { get; init; }

    /// <summary>
    /// Timestamp when this curve was read
    /// </summary>
    public DateTime ReadAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Get the snap points available for slider (speed percentages from curve)
    /// </summary>
    public int[] GetSnapPoints() => Points
        .Select(p => p.SpeedPercent)
        .Distinct()
        .OrderBy(s => s)
        .ToArray();

    /// <summary>
    /// Find the closest snap point to a given speed
    /// </summary>
    public int FindClosestSnapPoint(int speedPercent)
    {
        var snapPoints = GetSnapPoints();
        if (snapPoints.Length == 0) return speedPercent;

        return snapPoints.MinBy(s => Math.Abs(s - speedPercent));
    }

    /// <summary>
    /// Interpolate fan speed for a given temperature using the curve
    /// </summary>
    public int InterpolateSpeedForTemperature(int temperatureCelsius)
    {
        if (Points.Length == 0) return 0;
        if (Points.Length == 1) return Points[0].SpeedPercent;

        var lowerPoint = Points.LastOrDefault(p => p.TemperatureCelsius <= temperatureCelsius);
        var upperPoint = Points.FirstOrDefault(p => p.TemperatureCelsius > temperatureCelsius);

        if (lowerPoint == null) return Points[0].SpeedPercent;
        if (upperPoint == null) return Points[^1].SpeedPercent;

        var tempRange = upperPoint.TemperatureCelsius - lowerPoint.TemperatureCelsius;
        var speedRange = upperPoint.SpeedPercent - lowerPoint.SpeedPercent;
        var tempDiff = temperatureCelsius - lowerPoint.TemperatureCelsius;

        var interpolated = lowerPoint.SpeedPercent + (speedRange * tempDiff) / tempRange;
        return (int)Math.Round(interpolated);
    }
}
