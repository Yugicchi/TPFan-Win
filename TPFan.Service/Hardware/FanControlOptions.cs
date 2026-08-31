namespace TPFan.Service.Hardware;

/// <summary>
/// Embedded Controller (EC) register map used to drive the fan on ThinkPad T480.
///
/// Defaults are derived from the ThinkPad EC reference (thinkpad_acpi / NBFC):
///   WriteRegister = 0x2F   - fan control byte. Values:
///                              0x00 = fan off (manual),
///                              0x01..0x07 = manual levels 1..7,
///                              0x40 = disengaged (full speed),
///                              0x80 = BIOS / auto (firmware curve takes over).
///   ModeRegister  = 0x31   - "engagement" byte. 0x00 = firmware auto,
///                              0x40 = user manual override locked on.
///   ReadRegister  = 0x2F   - same byte; 0x2F is the canonical mirror that the
///                              EC exposes to the host.
///   TachHighRegister = 0x84 - 16-bit tachometer MSB for Fan 1 (paired with 0x85).
///   TachLowRegister  = 0x85 - 16-bit tachometer LSB. RPM = (0x84 << 8) | 0x85.
///
/// NBFC also references 0x32 as a "reset" register; on T480 writing 0x00
/// there force-stops the fan but does not re-engage the auto curve gracefully
/// (the fan goes to 0 and stays). We do not touch 0x32 — dropping the level
/// to 0 in 0x2F and then setting 0x31 = 0x00 is enough to release manual
/// control on T480.
///
/// If your BIOS revision exposes the fan at different offsets, override via
/// appsettings.json or environment variables (TPFAN_EC_*). See SETUP.md.
/// </summary>
public class FanControlOptions
{
    /// <summary>EC register that holds the fan level / mode byte.</summary>
    public byte WriteRegister { get; set; } = 0x2F;

    /// <summary>EC register read back to obtain the current level.</summary>
    public byte ReadRegister { get; set; } = 0x2F;

    /// <summary>EC register that toggles manual vs auto fan control.</summary>
    public byte ModeRegister { get; set; } = 0x31;

    /// <summary>Reserved for compatibility. Not used on T480.</summary>
    public byte ResetRegister { get; set; } = 0x32;

    /// <summary>EC register that holds the low byte of the 16-bit RPM reading (LSB).</summary>
    public byte TachLowRegister { get; set; } = 0x84;

    /// <summary>EC register that holds the high byte of the 16-bit RPM reading (MSB).</summary>
    public byte TachHighRegister { get; set; } = 0x85;

    /// <summary>Value written to <see cref="ModeRegister"/> to engage manual mode.</summary>
    public byte ManualModeValue { get; set; } = 0x40;

    /// <summary>Value written to <see cref="ModeRegister"/> to return to auto mode.</summary>
    public byte AutoModeValue { get; set; } = 0x00;

    /// <summary>
    /// Value written to <see cref="WriteRegister"/> to request BIOS / firmware
    /// auto fan control. On T480 (and all ThinkPad ECs per thinkpad_acpi)
    /// this is <c>0x80</c>. When the host writes 0x80 to 0x2F, the EC
    /// immediately re-enables its internal thermal curve.
    /// </summary>
    public byte AutoLevelValue { get; set; } = 0x80;

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
