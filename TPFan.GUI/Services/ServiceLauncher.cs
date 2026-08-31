using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TPFan.GUI.Services;

/// <summary>
/// Locates and launches the TPFan background service process that the GUI
/// talks to over Named Pipes. The service binary is expected to live in
/// the same directory as the GUI executable (self-contained publish
/// output), or to be on PATH.
///
/// On startup the GUI:
///   1. Probes the Named Pipe (best-effort) to see if a service is already up.
///   2. If not, looks for TPFan.Service.exe next to itself and spawns it.
///   3. Waits up to <see cref="ServiceReadyTimeoutMs"/> for the pipe to appear.
///
/// The launcher does NOT escalate itself. The GUI must already be running
/// elevated for EC write to work — the service inherits the same token via
/// the simple Process.Start path. (If unelevated, the service still starts
/// in read-only mode using WMI for sensors.)
/// </summary>
public static class ServiceLauncher
{
    private const string ServiceExeName = "TPFan.Service.exe";
    private static readonly string PipeName = "TPFan.Pipe";

    public static TimeSpan ServiceReadyTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public static TimeSpan ProbeInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    private static Process? _launchedProcess;

    /// <summary>
    /// Returns true if the service pipe is reachable, false otherwise.
    /// Never throws — a failed probe simply means the service is not up.
    /// </summary>
    public static async Task<bool> IsServiceRunningAsync()
    {
        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                ".", PipeName, System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensure the service is running: probe the pipe, start the service
    /// binary if not, and wait until the pipe is reachable or the timeout
    /// elapses. Returns true on success.
    /// </summary>
    public static async Task<bool> EnsureServiceRunningAsync()
    {
        if (await IsServiceRunningAsync().ConfigureAwait(false))
        {
            return true;
        }

        var servicePath = ResolveServicePath();
        if (servicePath == null || !File.Exists(servicePath))
        {
            Debug.WriteLine($"[Launcher] Service binary not found next to GUI.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = servicePath,
                UseShellExecute = true,       // required for Verb="runas"
                Verb = "runas",               // request UAC elevation
                CreateNoWindow = false,       // required for runas
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(servicePath) ?? Environment.CurrentDirectory,
            };

            // Elevate the service so EC writes work via InpOut32.
            _launchedProcess = Process.Start(startInfo);
            if (_launchedProcess == null)
            {
                Debug.WriteLine("[Launcher] Process.Start returned null.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launcher] Failed to start service: {ex.Message}");
            return false;
        }

        // Wait for the pipe to come up
        var deadline = DateTime.UtcNow + ServiceReadyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsServiceRunningAsync().ConfigureAwait(false))
            {
                return true;
            }
            await Task.Delay(ProbeInterval).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// Look for TPFan.Service.exe next to the GUI's executable. The publish
    /// workflow lays both binaries into ./publish/gui so this should always
    /// resolve in production.
    /// </summary>
    private static string? ResolveServicePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, ServiceExeName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Best-effort shutdown of the service we started. Called on window
    /// close so we don't leave a stray process behind. If the service was
    /// already running before the GUI opened, we leave it alone.
    /// </summary>
    public static void ShutdownLaunchedService()
    {
        try
        {
            if (_launchedProcess != null && !_launchedProcess.HasExited)
            {
                _launchedProcess.CloseMainWindow();
                if (!_launchedProcess.WaitForExit(2000))
                {
                    _launchedProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best effort
        }
    }
}
