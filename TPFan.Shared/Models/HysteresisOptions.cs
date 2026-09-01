namespace TPFan.Shared.Models;

public class HysteresisOptions
{
    public int DeadbandCelsius { get; set; } = 2;
    public int MinHoldSeconds { get; set; } = 2;
    public int MaxChangesPerMinute { get; set; } = 3;
}
