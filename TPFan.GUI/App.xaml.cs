using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TPFan.GUI.Hardware;
using TPFan.GUI.UI;

namespace TPFan.GUI;

/// <summary>
/// Interaction logic for App.xaml. Single-binary mode: hardware services
/// (sensors, EC fan controller) and system tray all run in this GUI process.
/// No separate service / pipe is required.
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TPFan-Win", "gui.log");

    // In-process hardware services. Static so the exit handlers can reach them
    // even after the App instance is gone during process teardown.
    public static LibreHardwareMonitorSensorService? CurrentSensors { get; private set; }
    public static EcFanController? CurrentEcController { get; private set; }
    public static T480FanProvider? CurrentProvider { get; private set; }
    private static SystemTrayManager? _trayManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check
        var mutex = new System.Threading.Mutex(true, "TPFan.GUI.SingleInstance", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("TPFan-Win GUI is already running.", "TPFan-Win", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        GC.KeepAlive(mutex);

        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(LogFile);
        if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);

        Log("GUI starting (single-binary mode)...");

        try
        {
            // Initialize hardware services
            CurrentEcController = new EcFanController();
            CurrentSensors = new LibreHardwareMonitorSensorService();
            CurrentProvider = new T480FanProvider(CurrentSensors, CurrentEcController);

            Log($"EC fan control: {(CurrentEcController.IsAvailable ? "AVAILABLE" : "unavailable")}");
            Log($"Hardware sensors (LHM): {(CurrentSensors.IsAvailable ? "AVAILABLE" : "unavailable — temperature/RPM/fan % will be 0")}");
            if (!CurrentEcController.IsAvailable)
            {
                Log("  -> ensure inpoutx64.dll is beside the executable and the app");
                Log("     is run as Administrator (and that inpoutx64.sys is installed).");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR initializing hardware services: {ex.Message}");
            Debug.WriteLine($"[App] Hardware init failed: {ex}");
            // Continue — UI can still show "service unavailable"
        }

        // Start system tray on its own STA thread (it owns a hidden WinForms
        // message loop and is independent of the WPF dispatcher).
        try
        {
            if (CurrentProvider is not null)
            {
                _trayManager = new SystemTrayManager(CurrentProvider);
                _trayManager.Start();
                Log("System tray icon started.");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR starting system tray: {ex.Message}");
            Debug.WriteLine($"[App] Tray start failed: {ex}");
        }

        // Show the main window manually now that the provider is available.
        var window = new MainWindow();
        MainWindow = window;
        _trayManager?.SetMainWindow(window); // Direct reference for left-click restore
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("GUI shutting down...");
        try
        {
            CurrentProvider?.Dispose();
        }
        catch { /* best effort */ }
        try
        {
            CurrentEcController?.Dispose();
        }
        catch { /* best effort */ }
        try
        {
            _trayManager?.Dispose();
        }
        catch { /* best effort */ }
        try
        {
            CurrentSensors?.Dispose();
        }
        catch { /* best effort */ }
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(LogFile, entry);
        }
        catch { }
    }
}