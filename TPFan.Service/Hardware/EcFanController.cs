using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        _dllPresent = DetectInpOutDll();
        _elevated = IsRunningAsAdministrator();
    }

    /// <summary>
    /// Probe likely locations for the InpOut32 shim. We cannot use
    /// NativeLibrary.TryLoad without making a successful load a hard
    /// requirement, so we just confirm the file is on disk.
    ///
    /// In single-file publish mode:
    ///   - AppContext.BaseDirectory points to the extraction temp directory
    ///     (where the bundled native libs are unpacked at startup).
    ///   - Environment.ProcessPath points to the actual .exe path.
    ///   - Both dirs are checked, plus a 'native\' subfolder.
    /// </summary>
    private static bool DetectInpOutDll()
    {
        try
        {
            // For single-file apps Environment.ProcessPath gives the real exe path.
            // AppContext.BaseDirectory is the extraction folder (same dir as .exe).
            var baseDir = AppContext.BaseDirectory;
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;

            var candidates = new[]
            {
                Path.Combine(baseDir, $"{InpOutDll}.dll"),
                Path.Combine(baseDir, "native", $"{InpOutDll}.dll"),
                Path.Combine(exeDir, $"{InpOutDll}.dll"),
                Path.Combine(exeDir, "native", $"{InpOutDll}.dll"),
                Path.Combine(Environment.SystemDirectory, $"{InpOutDll}.dll"),
            };
            return candidates.Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    public bool IsAvailable => _dllPresent && _elevated;

    public async Task<bool> SetFanSpeedAsync(int percent)
    {
        if (!IsAvailable) { Console.WriteLine("[EC] SetFanSpeedAsync: NOT AVAILABLE (dll or not elevated)"); return false; }
        if (percent < 0 || percent > 100)
        { Console.WriteLine($"[EC] SetFanSpeedAsync: ignoring out-of-range percent={percent}"); return false; }

        var level = MapPercentToLevel(percent);
        Console.WriteLine($"[EC] SetFanSpeedAsync percent={percent} -> level={level} (IsAvailable={IsAvailable})");
        if (level == _lastLevel) { Console.WriteLine($"[EC] SetFanSpeedAsync: level={level} == _lastLevel, skip"); return true; }

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
            Console.WriteLine($"[EC] SetFanSpeedAsync FAILED: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ResetToAutoAsync()
    {
        if (!IsAvailable) { Console.WriteLine("[EC] ResetToAutoAsync: NOT AVAILABLE"); return false; }
        Console.WriteLine("[EC] ResetToAutoAsync: restoring auto control...");

        try
        {
            return await Task.Run(() =>
            {
                // Per thinkpad_acpi / NBFC: write 0x80 to 0x2F to request
                // BIOS / auto fan control. The EC immediately re-enables its
                // internal thermal curve. Then clear the engagement byte (0x31).
                EcWrite(_options.WriteRegister, _options.AutoLevelValue);
                EcWrite(_options.ModeRegister, _options.AutoModeValue);
                _lastLevel = -1;
                Console.WriteLine("[EC] ResetToAutoAsync: OK (auto level 0x80 written, engagement cleared)");
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EC] ResetToAutoAsync FAILED: {ex.Message}");
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
                var pct = MapLevelToPercent(raw);
                Debug.WriteLine($"[EC] GetFanSpeedPercentAsync raw=0x{raw:X2} -> {pct}%");
                return pct;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EC] GetFanSpeedPercentAsync FAILED: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Reads the actual fan tachometer RPM from ThinkPad EC registers:
    /// Low Byte at 0x84 (LSB) and High Byte at 0x85 (MSB) (Little-Endian).
    /// Real hardware RPM = (EC[0x85] &lt;&lt; 8) | EC[0x84].
    /// </summary>
    public async Task<int?> GetFanRpmAsync()
    {
        if (!IsAvailable) return null;

        try
        {
            return await Task.Run(() =>
            {
                var lsb = EcRead(_options.TachLowRegister);
                var msb = EcRead(_options.TachHighRegister);
                var rpm = (msb << 8) | lsb;
                Console.WriteLine($"[EC.tach] 0x{_options.TachHighRegister:X2}(msb)=0x{msb:X2} 0x{_options.TachLowRegister:X2}(lsb)=0x{lsb:X2} -> RPM={rpm}");
                // Basic sanity check: if the reading is bogus (e.g. 0xFFFF or > 10000), treat as 0/unspun
                if (rpm is > 10000 or 0xFFFF)
                {
                    return (int?)0;
                }
                return (int?)rpm;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EC] GetFanRpmAsync failed: {ex.Message}");
            return null;
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
        var val = (byte)Inp32(DataPort);
        Debug.WriteLine($"[EC.io] Read(0x{offset:X2}) -> 0x{val:X2}");
        return val;
    }

    private void EcWrite(byte offset, byte value)
    {
        WaitForStatus(StatusIbf, expected: 0);
        Out32(CommandPort, EcCmdWrite);
        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, offset);
        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, value);
        Debug.WriteLine($"[EC.io] Write(0x{offset:X2}, 0x{value:X2}) OK");
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
