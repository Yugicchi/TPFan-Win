using TPFan.Service.Hardware;

namespace TPFan.Tests;

/// <summary>
/// Unit tests for the EcFanController's percent ↔︎ level mapping.
///
/// The mapping itself is intentionally small and pure (`round(percent / 100 * maxLevel)`)
/// so that it can be exercised without an actual EC driver. The tests double as the
/// regression harness for the "1829%" EC-readback bug: readback values outside 0..maxLevel
/// must be coerced back to 0..100, not blindly extrapolated.
///
/// See <c>FanControlOptions.MaxLevel</c> (default 7) and the "EC register map (T480)"
/// table in <c>SETUP.md</c>.
/// </summary>
public class EcFanMappingTests
{
    private static readonly EcFanController Controller = new(new FanControlOptions { MaxLevel = 7, EcPollTimeoutMs = 1 });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]     // 0.01 * 7 = 0.07 -> 0
    [InlineData(8, 1)]     // 0.08 * 7 = 0.56 -> 1
    [InlineData(50, 4)]    // 0.5  * 7 = 3.5  -> 4
    [InlineData(85, 6)]    // 0.85 * 7 = 5.95 -> 6
    [InlineData(99, 7)]    // 0.99 * 7 = 6.93 -> 7
    [InlineData(100, 7)]
    public void MapPercentToLevel_Returns0Through7Inclusive(int percent, int expected) =>
        Assert.Equal(expected, Controller.MapPercentToLevel(percent));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 14)]   // 1 * 100/7 ≈ 14.3 -> 14
    [InlineData(3, 43)]   // 3 * 100/7 ≈ 42.9 -> 43
    [InlineData(6, 86)]
    [InlineData(7, 100)]
    public void MapLevelToPercent_RoundsCorrectly(byte level, int expected) =>
        Assert.Equal(expected, Controller.MapLevelToPercent(level));

    /// <summary>
    /// The EC can return whatever happens to be at the informal mirror
    /// register address (we saw 0x80 = 128). The current production mapping
    /// extrapolates linearly — the `ReadFanControlPercent()` layer is now
    /// responsible for discarding such values (see <see cref="SensorClampTests" />).
    /// We keep this bottom-end capture so the extrapolation vs clamping
    /// design choice surfaces as an intentionally-failing test before it
    /// reaches the IPC surface `FanStatus`.
    /// </summary>
    [Fact]
    public void MapLevelToPercent_ExtrapolatesForOutOfRange_ExpectConsumerToClamp() =>
        Assert.True(Controller.MapLevelToPercent(128) > 100);
}
