namespace TPFan.Shared.Models;

/// <summary>
/// Represents a single point in the fan curve: temperature -> RPM mapping
/// </summary>
public record FanCurvePoint
{
    /// <summary>
    /// Temperature threshold in Celsius
    /// </summary>
    public int TemperatureCelsius { get; init; }

    /// <summary>
    /// Fan speed as percentage (0-100)
    /// </summary>
    public int SpeedPercent { get; init; }

    /// <summary>
    /// Estimated RPM for this speed level (informational)
    /// </summary>
    public int? EstimatedRpm { get; init; }

    public override string ToString() => $"{TemperatureCelsius}C -> {SpeedPercent}%";
}
