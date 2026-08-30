// Placeholder for service entry point
// Will be implemented with console app or Windows Service template

namespace TPFan.Service;

using Hardware;
using IPC;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TPFan-Win Service - Starting...");

        try
        {
            using var fanProvider = new T480FanProvider();
            using var pipeServer = new FanServicePipeServer(fanProvider);

            // Read initial status
            var status = await fanProvider.GetFanStatusAsync();
            Console.WriteLine($"Current temperature: {status.TemperatureCelsius}°C");
            Console.WriteLine($"Current fan speed: {status.SpeedPercent}%");
            Console.WriteLine($"Current fan RPM: {status.Rpm}");

            // Detect fan curve
            Console.WriteLine("\nDetecting fan curve...");
            var curve = await fanProvider.DetectFanCurveAsync();
            Console.WriteLine($"Fan curve points detected: {curve.Points.Length}");
            foreach (var point in curve.Points)
            {
                Console.WriteLine($"  {point}");
            }

            // Keep service running
            Console.WriteLine("\nService running. Press Ctrl+C to exit.");
            await Task.Delay(-1); // Run indefinitely
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
