using System.Threading.Tasks;

namespace TPFan.Service.Hardware;

/// <summary>
/// Abstraction over the hardware fan controller (typically the Embedded Controller
/// reached via raw I/O ports on ThinkPad T480).
///
/// Implementation is responsible for the low-level protocol. All callers should
/// treat this as a best-effort hardware write — failures must be surfaced as
/// <c>false</c> / sensible defaults rather than thrown exceptions so that the
/// service remains usable in read-only mode when the driver is missing or
/// the user is not elevated.
/// </summary>
public interface IFanController
{
    /// <summary>
    /// True if the underlying transport (e.g. InpOut32 driver) is loaded and
    /// the process has the privileges required to issue EC writes.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Override the firmware auto-control and force a fixed fan speed.
    /// </summary>
    /// <param name="percent">Target speed in the inclusive range 0..100.</param>
    /// <returns>True on success, false if the controller is unavailable or the
    /// call was rejected by the EC.</returns>
    Task<bool> SetFanSpeedAsync(int percent);

    /// <summary>
    /// Release the manual override and return the fan to firmware auto control.
    /// </summary>
    Task<bool> ResetToAutoAsync();

    /// <summary>
    /// Read back the current speed that the EC is actually driving. This is
    /// the value that overrides any WMI % when the override is active.
    /// </summary>
    Task<int> GetFanSpeedPercentAsync();

    /// <summary>
    /// Read the real fan tachometer RPM directly from the EC hardware registers
    /// (0x84 MSB, 0x85 LSB on ThinkPad). Returns null or negative if unavailable.
    /// </summary>
    Task<int?> GetFanRpmAsync();
}
