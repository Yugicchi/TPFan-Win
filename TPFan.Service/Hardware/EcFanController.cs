using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace TPFan.Service.Hardware;

/// <summary>
/// Drives the ThinkPad T480 fan via raw I/O port writes to the Embedded Controller.
///
/// The transport is InpOut32 (Highrez / Phil Gibbons). It ships two files:
///   - <c>inpoutx64.dll</c>  : user-mode shim that marshals <c>Inp32</c> / <c>Out32</c>
///   - <c>inpoutx64.sys</c>  : signed kernel-mode driver that performs the actual
///     <c>in</c>/<c>out</c> instructions; the shim talks to it via a device IOCTL
///
/// Both files must be in the same directory as the executable. Install the driver
/// once with the included <c>InstallDriver.exe</c> (or <c>sc create</c> equivalent);
/// see SETUP.md for details.
///
/// EC protocol reference (ACPI Embedded Controller spec, 6.5):
///   - Command port   : 0x66 (writes a command opcode)
///   - Data port      : 0x62 (carries the offset or data byte)
///   - Status bits    : 0x66 bit 1 = IBF (input buffer full - we must wait for 0
///                              before writing), bit 0 = OBF (output buffer
///                              full - we must wait for 1 before reading)
///   - READ op        : write 0x80 to 0x66, then write offset to 0x62, then read
///                      from 0x62 once OBF asserts.
///   - WRITE op       : write 0x81 to 0x66, then write offset to 0x62, then
///                      write value to 0x62 and wait for IBF to clear.
///
/// All public methods are guarded by <see cref="IsAvailable"/>; if the driver
/// is missing or the process is not elevated, the controller transparently
/// reports unavailability rather than throwing.
/// </summary>
public class EcFanController : IFanController
{
    private const string InpOutDll = "inpoutx64";
    private const short CommandPort = 0x66;
    private const short DataPort = 0x62;
    private const byte StatusIbf = 0x02;
    private const byte StatusObf = 0x01;
    private const byte EcCmdRead = 0x80;
    private const byte EcCmdWrite = 0x81;

    private readonly FanControlOptions _options;
    private readonly bool _dllPresent;
    private readonly bool _elevated;
    private int _lastLevel = -1;

    public EcFanController(FanControlOptions? options = null)
    {
        _options = options ?? new FanControlOptions();
        _dllPresent = NativeLibrary.Exists(InpOutDll) || File.Exists($"{InpOutDll}.dll");
        _elevated = IsRunningAsAdministrator();
    }

    public bool IsAvailable => _dllPresent && _elevated;

    public async Task<bool> SetFanSpeedAsync(int percent)
    {
        if (!IsAvailable) return false;
        if (percent < 0 || percent > 100)
        {
            Debug.WriteLine($"EcFanController: ignoring out-of-range percent={percent}");
            return false;
        }

        var level = MapPercentToLevel(percent);
        if (level == _lastLevel) return true; // Nothing to do; avoid unnecessary EC traffic.

        try
        {
            return await Task.Run(() =>
            {
                // 1. Engage manual mode so the firmware stops fighting us.
                EcWrite(_options.ModeRegister, _options.ManualModeValue);

                // 2. Drive the fan to the requested level.
                EcWrite(_options.WriteRegister, (byte)level);
                _lastLevel = level;
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EcFanController.SetFanSpeedAsync failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ResetToAutoAsync()
    {
        if (!IsAvailable) return false;

        try
        {
            return await Task.Run(() =>
            {
                // Per NBFC T480 config: writing 0x00 to the reset register is the
                // canonical "return to firmware auto control" gesture. Belt and
                // braces: also clear the manual mode byte.
                EcWrite(_options.ResetRegister, 0x00);
                EcWrite(_options.ModeRegister, _options.AutoModeValue);
                _lastLevel = -1;
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EcFanController.ResetToAutoAsync failed: {ex.Message}");
            return false;
        }
    }

    public async Task<int> GetFanSpeedPercentAsync()
    {
        if (!IsAvailable) return -1;

        try
        {
            return await Task.Run(() =>
            {
                var raw = EcRead(_options.ReadRegister);
                return MapLevelToPercent(raw);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EcFanController.GetFanSpeedPercentAsync failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Translate a 0..100 percent target into an EC level byte. The T480
    /// firmware exposes 8 discrete levels (0..7) so 100% ≈ level 7.
    /// </summary>
    internal int MapPercentToLevel(int percent)
    {
        if (percent <= 0) return 0;
        if (percent >= 100) return _options.MaxLevel;
        return (int)Math.Round(percent / 100.0 * _options.MaxLevel);
    }

    internal int MapLevelToPercent(byte level)
    {
        if (_options.MaxLevel <= 0) return 0;
        return (int)Math.Round(level * 100.0 / _options.MaxLevel);
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ----- Low-level EC protocol --------------------------------------------------

    private byte EcRead(byte offset)
    {
        WaitForStatus(StatusIbf, expected: 0);
        Out32(CommandPort, EcCmdRead);
        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, offset);
        WaitForStatus(StatusObf, expected: 1);
        return (byte)Inp32(DataPort);
    }

    private void EcWrite(byte offset, byte value)
    {
        WaitForStatus(StatusIbf, expected: 0);
        Out32(CommandPort, EcCmdWrite);
        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, offset);
        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, value);
    }

    private void WaitForStatus(byte mask, byte expected)
    {
        var deadline = Environment.TickCount + _options.EcPollTimeoutMs;
        while (Environment.TickCount < deadline)
        {
            var status = (byte)Inp32(CommandPort);
            if ((status & mask) == expected) return;
            // Spin a few microseconds; Thread.Sleep(0) yields the timeslice,
            // which is cheap on Windows because our loop budget is small.
            Thread.SpinWait(_options.EcPollDelayUs);
        }
        throw new TimeoutException(
            $"EC status timeout waiting for 0x{mask:X2}={expected} on port 0x{CommandPort:X}");
    }

    // ----- P/Invoke ----------------------------------------------------------------

    [DllImport(InpOutDll, EntryPoint = "Inp32", SetLastError = true)]
    private static extern uint Inp32(short port);

    [DllImport(InpOutDll, EntryPoint = "Out32", SetLastError = true)]
    private static extern void Out32(short port, uint data);
}
