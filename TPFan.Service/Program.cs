// Placeholder for service entry point
// Will be implemented with console app or Windows Service template

using System;
using System.Threading.Tasks;
using TPFan.Service.Hardware;
using TPFan.Service.IPC;

namespace TPFan.Service;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("TPFan-Win Service - Starting...");

        try
        {
            var sensorService = new LibreHardwareMonitorSensorService();
            Console.WriteLine(
                $"Hardware sensors (LHM): {(sensorService.IsAvailable ? "AVAILABLE" : "unavailable — temperature/RPM/fan % will be 0")}");

            var fanController = new EcFanController();
            Console.WriteLine(
                $"EC fan control: {(fanController.IsAvailable ? "AVAILABLE" : "unavailable")}");
            if (!fanController.IsAvailable)
            {
                Console.WriteLine(
                    "  -> ensure inpoutx64.dll is beside the executable and the service");
                Console.WriteLine(
                    "     is run as Administrator (and that inpoutx64.sys is installed).");
            }

            using var fanProvider = new T480FanProvider(sensorService, fanController);
            using var pipeServer = new FanServicePipeServer(fanProvider);

            // Ctrl+C handler returns the fan to auto control so an abrupt
            // stop does not leave the fan pinned at the user's last override.
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = false;
                fanProvider.ResetFanOverrideAsync().GetAwaiter().GetResult();
                sensorService.Dispose();
                Console.WriteLine("\nFan override released. Exiting.");
            };

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
