namespace TPFan.Shared.Models;

public class HysteresisOptions
{
    public int DeadbandCelsius { get; set; } = 1;
    public int MinHoldSeconds { get; set; } = 1;
    public int MaxChangesPerMinute { get; set; } = 5;
}
