namespace TPFan.Service.Hardware;

/// <summary>
/// Embedded Controller (EC) register map used to drive the fan on ThinkPad T480.
///
/// Defaults are derived from the NBFC T480 config (hirschmann/nbfc Configs):
///   WriteRegister = 0x2F   - fan level 0..7 (level = percent / 14 ≈ 0..7)
///   ReadRegister  = 0x2F   - mirrors the current level back
///   ModeRegister  = 0x31   - 0x00 = auto, 0x40 = manual (Lenovo "engagement" byte)
///   ResetRegister = 0x32   - writing 0x00 here hands control back to firmware
///
/// If your BIOS revision exposes the fan at different offsets, override via
/// appsettings.json or environment variables (TPFAN_EC_*). See SETUP.md.
/// </summary>
public class FanControlOptions
{
    /// <summary>EC register written to set the fan level.</summary>
    public byte WriteRegister { get; set; } = 0x2F;

    /// <summary>EC register read back to obtain the current level.</summary>
    public byte ReadRegister { get; set; } = 0x2F;

    /// <summary>EC register that toggles manual vs auto fan control.</summary>
    public byte ModeRegister { get; set; } = 0x31;

    /// <summary>EC register written to fully release manual control.</summary>
    public byte ResetRegister { get; set; } = 0x32;

    /// <summary>Value written to <see cref="ModeRegister"/> to engage manual mode.</summary>
    public byte ManualModeValue { get; set; } = 0x40;

    /// <summary>Value written to <see cref="ModeRegister"/> to return to auto mode.</summary>
    public byte AutoModeValue { get; set; } = 0x00;

    /// <summary>
    /// Number of EC levels the firmware exposes. T480 has 8 (0..7 inclusive),
    /// so 100% maps to level 7.
    /// </summary>
    public int MaxLevel { get; set; } = 7;

    /// <summary>Polling timeout (ms) when waiting on the EC status register.</summary>
    public int EcPollTimeoutMs { get; set; } = 50;

    /// <summary>Polling interval (µs) when busy-waiting on the EC.</summary>
    public int EcPollDelayUs { get; set; } = 50;
}
