using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace TPFan.GUI.Hardware;

/// <summary>
/// Drives the ThinkPad T480 fan via raw I/O port writes to the Embedded Controller.
///
/// The transport is InpOut32 (Highrez / Phil Gibbons). It ships two files:
///   - <c>inpoutx64.dll</c>  : user-mode shim that marshals <c>Inp32</c> / <c>Out32</c>
///   - <c>inpoutx64.sys</c>  : signed kernel-mode driver that performs the actual
///     <c>in</c>/<c>out</c> instructions; the shim talks to it via a device IOCTL
///
/// EC protocol reference (ACPI Embedded Controller spec, 6.5):
///   - Command port   : 0x66 (writes a command opcode)
///   - Data port      : 0x62 (carries the offset or data byte)
///   - Status bits    : 0x66 bit 1 = IBF (input buffer full — must wait for 0 before writing)
///                              bit 0 = OBF (output buffer full — must wait for 1 before reading)
///   - READ op        : write 0x80 to 0x66, then write offset to 0x62, then read from 0x62 once OBF asserts.
///   - WRITE op       : write 0x81 to 0x66, then write offset to 0x62, then write value to 0x62,
///                      then wait for IBF to clear.
///
/// SAFETY NOTES (from incident 2026-09-02 18:03–18:23):
///   - Event ID 15 (ACPI Warning) at 18:03:30–18:03:40:
///       "EC returned data when none was requested" — unsynchronised burst writes
///       collided with the Windows ACPI driver's own EC access.
///   - Event ID 13 (ACPI Error) at 18:12:36:
///       "EC did not respond within the specified timeout period" — EC hung.
///   - Unexpected shutdown at 18:23:08 — BIOS killed power after EC freeze.
///
///   Three mitigations now enforced:
///   [1] Global named mutex (_ecGlobalMutex) — only one accessor at a time.
///       The Windows ACPI driver does NOT hold this mutex, but it prevents
///       concurrent access from multiple threads within our process, and
///       the mandatory delay between bytes (mitigation [2]) reduces the
///       probability of racing ACPI's own polling cycle.
///   [2] Inter-byte delay — 20 ms after every Out32/Inp32, giving the EC
///       microcontroller time to process the byte before the next arrives.
///       Recommended range: 10–50 ms. 20 ms chosen as a safe default.
///   [3] Strict IBF/OBF handshake — every byte is gated on the appropriate
///       status flag.  WaitForStatus enforces this; never skip it.
/// </summary>
public class EcFanController : IFanController, IDisposable
{
    private const string InpOutDll = "inpoutx64";
    private const short CommandPort = 0x66;
    private const short DataPort = 0x62;
    private const byte StatusIbf = 0x02;
    private const byte StatusObf = 0x01;
    private const byte EcCmdRead = 0x80;
    private const byte EcCmdWrite = 0x81;

    // Mitigation [1]: process-wide serialisation.
    // Named so that future tooling (e.g. a watchdog service) could also acquire it.
    private static readonly Mutex _ecGlobalMutex = new(false, "Global\\TPFan-Win.EcAccess");

    // Mitigation [2]: minimum gap between consecutive I/O byte operations.
    private const int InterByteDelayMs = 20;

    private readonly FanControlOptions _options;
    private readonly bool _dllPresent;
    private readonly bool _elevated;
    private int _lastLevel = -1;
    private bool _disposed;

    public EcFanController(FanControlOptions? options = null)
    {
        _options = options ?? new FanControlOptions();
        _dllPresent = DetectInpOutDll();
        _elevated = IsRunningAsAdministrator();
    }

    /// <summary>
    /// Probe likely locations for the InpOut32 shim.
    /// In single-file publish mode AppContext.BaseDirectory is the extraction
    /// temp directory; Environment.ProcessPath is the actual .exe path.
    /// </summary>
    private static bool DetectInpOutDll()
    {
        try
        {
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
            foreach (var path in candidates)
            {
                Diag.Log($"[EC] Checking DLL: {path} -> exists={File.Exists(path)}");
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
                {
                    Diag.Log($"[EC] DLL loaded successfully: {path}");
                    return true;
                }
            }
            Diag.Log($"[EC] No {InpOutDll}.dll found in any search path — fan override will not work.");
            return false;
        }
        catch (Exception ex)
        {
            Diag.Log($"[EC] DetectInpOutDll exception: {ex.Message}");
            return false;
        }
    }

    public bool IsAvailable => _dllPresent && _elevated;

    public async Task<bool> SetFanSpeedAsync(int percent)
    {
        if (!IsAvailable)
        {
            Diag.Log("[EC] SetFanSpeedAsync: NOT AVAILABLE (dll missing or not elevated)");
            return false;
        }
        if (percent < 0 || percent > 100)
        {
            Diag.Log($"[EC] SetFanSpeedAsync: ignoring out-of-range percent={percent}");
            return false;
        }

        var level = MapPercentToLevel(percent);
        Diag.Log($"[EC] SetFanSpeedAsync percent={percent} -> level={level}");
        if (level == _lastLevel)
        {
            Diag.Log($"[EC] SetFanSpeedAsync: level={level} unchanged, skip");
            return true;
        }

        return await Task.Run(() => AcquireAndRun(() =>
        {
            // 1. Engage manual mode so the firmware stops fighting us.
            EcWrite(_options.ModeRegister, _options.ManualModeValue);

            // 2. Drive the fan to the requested level.
            EcWrite(_options.WriteRegister, (byte)level);
            _lastLevel = level;
            Diag.Log($"[EC] SetFanSpeedAsync: OK level={level}");
            return true;
        })).ConfigureAwait(false);
    }

    public async Task<bool> ResetToAutoAsync()
    {
        if (!IsAvailable)
        {
            Diag.Log("[EC] ResetToAutoAsync: NOT AVAILABLE");
            return false;
        }
        Diag.Log("[EC] ResetToAutoAsync: restoring auto control...");

        return await Task.Run(() => AcquireAndRun(() =>
        {
            // Per thinkpad_acpi / NBFC: write 0x80 to the fan register to request
            // BIOS / auto fan control, then clear the engagement byte.
            EcWrite(_options.WriteRegister, _options.AutoLevelValue);
            EcWrite(_options.ModeRegister, _options.AutoModeValue);
            _lastLevel = -1;
            Diag.Log("[EC] ResetToAutoAsync: OK (auto level written, engagement cleared)");
            return true;
        })).ConfigureAwait(false);
    }

    public async Task<int> GetFanSpeedPercentAsync()
    {
        if (!IsAvailable) return -1;

        return await Task.Run(() => AcquireAndRun(() =>
        {
            var raw = EcRead(_options.ReadRegister);
            var pct = MapLevelToPercent(raw);
            if (pct > 100) pct = 100; // mirror register bug guard
            Diag.Log($"[EC] GetFanSpeedPercentAsync raw=0x{raw:X2} -> {pct}%");
            return pct;
        }, fallback: -1)).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the actual fan tachometer RPM from ThinkPad EC registers 0x84 (LSB) / 0x85 (MSB).
    /// </summary>
    public async Task<int?> GetFanRpmAsync()
    {
        if (!IsAvailable) return null;

        return await Task.Run(() => AcquireAndRun<int?>(() =>
        {
            var lsb = EcRead(_options.TachLowRegister);
            var msb = EcRead(_options.TachHighRegister);
            var rpm = (msb << 8) | lsb;
            Diag.Log($"[EC.tach] 0x{_options.TachHighRegister:X2}(msb)=0x{msb:X2} 0x{_options.TachLowRegister:X2}(lsb)=0x{lsb:X2} -> RPM={rpm}");
            // Bogus sanity check: 0xFFFF or > 10000 means unspun / noise
            if (rpm is > 10000 or 0xFFFF) return 0;
            return rpm;
        }, fallback: null)).ConfigureAwait(false);
    }

    // ---- Mapping helpers ----------------------------------------------------------

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

    // ---- Global mutex wrapper -----------------------------------------------------

    /// <summary>
    /// Acquires the global EC mutex, runs <paramref name="action"/>, releases the mutex.
    /// Returns <paramref name="fallback"/> if the mutex cannot be acquired within 2 s or
    /// if <paramref name="action"/> throws.
    /// </summary>
    private T AcquireAndRun<T>(Func<T> action, T fallback = default!)
    {
        bool acquired = false;
        try
        {
            // Wait at most 2 s for the mutex — avoids a deadlock if something else holds it.
            acquired = _ecGlobalMutex.WaitOne(2000);
            if (!acquired)
            {
                Diag.Log("[EC] AcquireAndRun: mutex timeout — skipping operation to avoid EC hang");
                return fallback;
            }
            return action();
        }
        catch (AbandonedMutexException)
        {
            // Previous holder crashed; we now own it — treat as acquired.
            acquired = true;
            try { return action(); }
            catch (Exception ex) { Diag.Log($"[EC] AcquireAndRun (after abandoned mutex): {ex.Message}"); return fallback; }
        }
        catch (Exception ex)
        {
            Diag.Log($"[EC] AcquireAndRun: {ex.Message}");
            return fallback;
        }
        finally
        {
            if (acquired)
            {
                try { _ecGlobalMutex.ReleaseMutex(); }
                catch (Exception ex) { Diag.Log($"[EC] ReleaseMutex failed: {ex.Message}"); }
            }
        }
    }

    // ---- Low-level EC protocol ----------------------------------------------------

    private byte EcRead(byte offset)
    {
        WaitForStatus(StatusIbf, expected: 0);
        Out32(CommandPort, EcCmdRead);
        Thread.Sleep(InterByteDelayMs);   // [2] inter-byte settling

        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, offset);
        Thread.Sleep(InterByteDelayMs);   // [2]

        WaitForStatus(StatusObf, expected: 1);
        var val = (byte)Inp32(DataPort);
        Thread.Sleep(InterByteDelayMs);   // [2] let EC clear OBF before next op
        Diag.Log($"[EC.io] Read(0x{offset:X2}) -> 0x{val:X2}");
        return val;
    }

    private void EcWrite(byte offset, byte value)
    {
        WaitForStatus(StatusIbf, expected: 0);
        Out32(CommandPort, EcCmdWrite);
        Thread.Sleep(InterByteDelayMs);   // [2]

        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, offset);
        Thread.Sleep(InterByteDelayMs);   // [2]

        WaitForStatus(StatusIbf, expected: 0);
        Out32(DataPort, value);
        Thread.Sleep(InterByteDelayMs);   // [2] wait for EC to process the write
        Diag.Log($"[EC.io] Write(0x{offset:X2}, 0x{value:X2}) OK");
    }

    /// <summary>
    /// Spin-poll the EC status port until the <paramref name="mask"/> bit equals
    /// <paramref name="expected"/>, or until the deadline expires.
    /// Throws <see cref="TimeoutException"/> on deadline — caller's try/catch converts
    /// this to a log entry and a safe no-op rather than an unhandled exception.
    /// </summary>
    private void WaitForStatus(byte mask, byte expected)
    {
        // Use a hard deadline to prevent permanent EC hang from starving our timeout.
        // EcPollTimeoutMs is typically 500 ms.
        var deadline = Environment.TickCount + _options.EcPollTimeoutMs;
        while (Environment.TickCount < deadline)
        {
            var status = (byte)Inp32(CommandPort);
            if ((status & mask) == expected) return;
            Thread.SpinWait(_options.EcPollDelayUs);
        }
        throw new TimeoutException(
            $"EC status timeout waiting for 0x{mask:X2}={expected} on port 0x{CommandPort:X}");
    }

    // ---- P/Invoke -----------------------------------------------------------------

    [DllImport(InpOutDll, EntryPoint = "Inp32", SetLastError = true)]
    private static extern uint Inp32(short port);

    [DllImport(InpOutDll, EntryPoint = "Out32", SetLastError = true)]
    private static extern void Out32(short port, uint data);

    // ---- IDisposable --------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _ecGlobalMutex.Dispose(); }
        catch { }
        GC.SuppressFinalize(this);
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
