using System.Threading.Tasks;

namespace TPFan.GUI.Hardware;

/// <summary>
/// Minimal contract for fan control via Embedded Controller (InpOut32).
/// </summary>
public interface IFanController
{
    /// <summary>
    /// True if the inpoutx64.dll is present and the process is elevated.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Set fan speed as percentage (0-100).
    /// </summary>
    Task<bool> SetFanSpeedAsync(int percent);

    /// <summary>
    /// Reset fan to automatic firmware control.
    /// </summary>
    Task<bool> ResetToAutoAsync();

    /// <summary>
    /// Read current fan speed as percentage (0-100).
    /// Returns -1 if not available.
    /// </summary>
    Task<int> GetFanSpeedPercentAsync();

    /// <summary>
    /// Read current fan RPM from EC tachometer registers (0x84/0x85).
    /// Returns null if not available.
    /// </summary>
    Task<int?> GetFanRpmAsync();
}