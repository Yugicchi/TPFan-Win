using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using TPFan.GUI.Services;

namespace TPFan.GUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TPFan-Win", "gui.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single instance
        var mutex = new System.Threading.Mutex(true, "TPFan.GUI.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("TPFan-Win GUI is already running.", "TPFan-Win", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        GC.KeepAlive(mutex);

        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(LogFile);
        if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);

        Log("GUI starting...");

        // Start service async — don't block the UI thread!
        // The ViewModel will handle reconnection when the service pipe is ready.
        _ = Task.Run(EnsureServiceAsync).ContinueWith(t =>
        {
            if (t.Result)
                Log("Service connected or started successfully.");
            else
                Log("WARNING: Could not start TPFan.Service. GUI will keep trying to connect.");
        });

        // Window will show immediately via StartupUri
    }

    private static async Task<bool> EnsureServiceAsync()
    {
        try
        {
            if (await ServiceLauncher.IsServiceRunningAsync())
            {
                Log("Service already running (Named Pipe found).");
                return true;
            }

            Log("Service not running — attempting to start TPFan.Service.exe...");
            return await ServiceLauncher.EnsureServiceRunningAsync();
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            Debug.WriteLine($"[App] EnsureServiceAsync failed: {ex}");
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("GUI shutting down...");
        ServiceLauncher.ShutdownLaunchedService();
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
