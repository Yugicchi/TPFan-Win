using System;

namespace TPFan.Shared.Models;

/// <summary>
/// Current fan and system status
/// </summary>
public record FanStatus
{
    /// <summary>
    /// Current CPU temperature in Celsius
    /// </summary>
    public int TemperatureCelsius { get; init; }

    /// <summary>
    /// Current fan speed as percentage (0-100)
    /// </summary>
    public int SpeedPercent { get; init; }

    /// <summary>
    /// Current fan RPM
    /// </summary>
    public int Rpm { get; init; }

    /// <summary>
    /// Is manual override active?
    /// </summary>
    public bool IsOverrideActive { get; init; }

    /// <summary>
    /// Manual override speed if active (0-100)
    /// </summary>
    public int? OverrideSpeedPercent { get; init; }

    /// <summary>
    /// Timestamp when status was read
    /// </summary>
    public DateTime ReadAt { get; init; } = DateTime.UtcNow;
}
