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

            var sensorService = new LibreHardwareMonitorSensorService();
            Console.WriteLine(
                $"Hardware sensors (LHM): {(sensorService.IsAvailable ? "AVAILABLE" : "unavailable — temperature/RPM/fan % will be 0")}");

            using var fanProvider = new T480FanProvider(sensorService, fanController);
            using var pipeServer = new FanServicePipeServer(fanProvider);
            using var tray = new TPFan.Service.UI.SystemTrayManager(fanProvider);

            pipeServer.Start();
            tray.Start();
            Console.WriteLine("System tray icon started (right-click for menu).");

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

            // Check for ad-hoc debug override test (e.g. --override-test 80)
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--override-test" && i + 1 < args.Length && int.TryParse(args[i + 1], out var targetPct))
                {
                    Console.WriteLine($"\n=== Ad-hoc EC Override Test: target={targetPct}% ===");
                    var ok = await fanProvider.SetFanSpeedOverrideAsync(targetPct);
                    Console.WriteLine($"Override result: {ok}");
                    Console.WriteLine("Holding override for 15 seconds to observe fan spin-up...");
                    for (var s = 1; s <= 15; s++)
                    {
                        await Task.Delay(1000);
                        var liveStatus = await fanProvider.GetFanStatusAsync();
                        Console.WriteLine($"  [{s}s] Temp: {liveStatus.TemperatureCelsius}°C | RPM: {liveStatus.Rpm} | Speed: {liveStatus.SpeedPercent}% | OverrideActive: {liveStatus.IsOverrideActive}");
                    }

                    Console.WriteLine("\nResetting to auto (firmware thermal curve takes back control)...");
                    var resetOk = await fanProvider.ResetFanOverrideAsync();
                    Console.WriteLine($"Reset result: {resetOk}");
                    Console.WriteLine("Observing spin-down over 10 seconds...");
                    for (var s = 1; s <= 10; s++)
                    {
                        await Task.Delay(1000);
                        var liveStatus = await fanProvider.GetFanStatusAsync();
                        Console.WriteLine($"  [Post-reset +{s}s] Temp: {liveStatus.TemperatureCelsius}°C | RPM: {liveStatus.Rpm} | OverrideActive: {liveStatus.IsOverrideActive}");
                    }
                    Console.WriteLine("=== Ad-hoc EC Override Test completed. Exiting. ===\n");
                    return;
                }
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
