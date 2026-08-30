using System.Threading.Tasks;
using TPFan.Shared.Models;

namespace TPFan.Shared.Contracts;

/// <summary>
/// Contract for communication between UWP app and background service
/// </summary>
public interface IFanServiceContract
{
    /// <summary>
    /// Get the current fan curve from the system
    /// </summary>
    Task<FanCurve> GetFanCurveAsync();

    /// <summary>
    /// Get current fan and system status
    /// </summary>
    Task<FanStatus> GetFanStatusAsync();

    /// <summary>
    /// Set manual fan speed override
    /// </summary>
    Task<bool> SetFanSpeedOverrideAsync(int speedPercent);

    /// <summary>
    /// Reset fan to automatic control
    /// </summary>
    Task<bool> ResetFanOverrideAsync();

    /// <summary>
    /// Check if service is running
    /// </summary>
    Task<bool> IsServiceRunningAsync();
}
