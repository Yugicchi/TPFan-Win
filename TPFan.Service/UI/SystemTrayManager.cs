using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TPFan.Service.Hardware;
using TPFan.Shared.Models;

namespace TPFan.Service.UI;

/// <summary>
/// Manages the Windows System Tray (Notification Area) icon for TPFan-Win.
/// Displays live temperature badge on the tray icon, updates tooltip,
/// and provides a right-click context menu for quick fan overrides.
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    private readonly T480FanProvider _fanProvider;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _statusMenuItem;
    private ToolStripMenuItem? _autoMenuItem;
    private ToolStripMenuItem? _overrideMenu;
    private Thread? _uiThread;
    private System.Threading.Timer? _refreshTimer;
    private bool _disposed;
    private static T480FanProvider? _providerRef;
    // Hidden Form provides a real HWND for Invoke/BeginInvoke from worker threads.
    // A bare Control without a parent never gets a handle, so its BeginInvoke
    // would silently drop posted delegates.
    private Form? _trayForm;

    private static SystemTrayManager? _instance; // singleton reference for static exit handler

    public SystemTrayManager(T480FanProvider fanProvider)
    {
        _fanProvider = fanProvider;
        _instance = this;
    }

    public void Start()
    {
        var readyEvent = new ManualResetEventSlim(false);

        _uiThread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Allow cross-thread property access so RefreshStatus can update UI directly
            // from the timer callback thread without needing BeginInvoke.
            Control.CheckForIllegalCrossThreadCalls = false;

            InitializeTray();
            readyEvent.Set();

            Application.Run();
        })
        {
            IsBackground = true,
            Name = "TPFanTrayUI"
        };

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        readyEvent.Wait();

        // Start background polling timer (every 2 seconds)
        _refreshTimer = new System.Threading.Timer(
            _ => RefreshStatus(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2));

        Console.WriteLine("[Tray] System tray icon initialized.");
    }

    private static void ResetFanOnExit()
    {
        try
        {
            var provider = _providerRef;
            if (provider != null)
            {
                Console.WriteLine("[Tray] Resetting fan to auto on exit...");
                // Run synchronously on the thread that called us so we block until done.
                provider.ResetFanOverrideAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Tray] Reset-to-auto on exit failed: {ex.Message}");
        }
    }

    static SystemTrayManager()
    {
        // Fires for normal process exit (Environment.Exit, return from Main, etc.)
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ResetFanOnExit();

        // Fires for Ctrl+C / Ctrl+Break
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // prevent immediate termination; let cleanup run
            ResetFanOnExit();
        };
    }

    private void InitializeTray()
    {
        _contextMenu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem("TPFan: Initializing...") { Enabled = false };
        _contextMenu.Items.Add(_statusMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        _autoMenuItem = new ToolStripMenuItem("Auto Mode (Firmware Curve)", null, (s, e) =>
        {
            _contextMenu?.Close();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _fanProvider.ResetFanOverrideAsync();
                    RefreshStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Tray] Auto-mode error: {ex.Message}");
                }
            });
        })
        { Checked = true };
        _contextMenu.Items.Add(_autoMenuItem);

        _overrideMenu = new ToolStripMenuItem("Manual Overrides");
        AddOverrideOption(_overrideMenu, "Level 0 (0% - Fan Off)", 0);
        AddOverrideOption(_overrideMenu, "Level 1 (20% - Quiet)", 20);
        AddOverrideOption(_overrideMenu, "Level 3 (40% - Medium)", 40);
        AddOverrideOption(_overrideMenu, "Level 5 (60% - High)", 60);
        AddOverrideOption(_overrideMenu, "Level 6 (80% - Performance)", 80);
        AddOverrideOption(_overrideMenu, "Level 7 (100% - Max Governed)", 100);

        _contextMenu.Items.Add(_overrideMenu);
        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Service", null, (s, e) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _fanProvider.ResetFanOverrideAsync();
                }
                catch { /* best effort */ }
                Application.ExitThread();
                Environment.Exit(0);
            });
        });
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTempIcon(0),
            Text = "TPFan-Win: Starting...",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        // Hidden form provides a real HWND for Invoke/BeginInvoke so worker threads
        // can marshal updates onto the STA message loop that owns the tray.
        // Accessing .Handle forces handle creation; we hide it from taskbar/alt-tab.
        _trayForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            Size = new Size(1, 1),
            Opacity = 0,
            Text = string.Empty
        };
        _ = _trayForm.Handle; // force handle creation; discarded intentionally
    }

    private void AddOverrideOption(ToolStripMenuItem parent, string label, int percent)
    {
        var item = new ToolStripMenuItem(label, null, (s, e) =>
        {
            // Close menu immediately so the user sees a clean state; the async
            // fan command runs off the STA thread.
            _contextMenu?.Close();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _fanProvider.SetFanSpeedOverrideAsync(percent);
                    RefreshStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Tray] Override error: {ex.Message}");
                }
            });
        });
        parent.DropDownItems.Add(item);
    }

    private void RefreshStatus()
    {
        if (_notifyIcon == null || _disposed) return;

        // Skip refresh while a context menu is open — modifying Text/Icon of the
        // owner while the menu is rendering causes it to freeze.
        if (_contextMenu != null && _contextMenu.Visible) return;

        try
        {
            var status = _fanProvider.GetFanStatusAsync().GetAwaiter().GetResult();
            var temp = (int)Math.Round((double)status.TemperatureCelsius);
            var modeStr = status.IsOverrideActive ? $"Manual ({status.SpeedPercent}%)" : "Auto";

            // Build the Icon on the worker thread (no UI dependency, no STA requirement).
            var newIcon = CreateTempIcon(temp);

            UpdateUiState(status, temp, modeStr, newIcon);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tray] Refresh error: {ex.Message}");
        }
    }

    private void UpdateUiState(FanStatus status, int temp, string modeStr, Icon newIcon)
    {
        if (_disposed || _notifyIcon == null) { newIcon.Dispose(); return; }

        // Tooltip text (max 63 chars on Windows)
        var tooltip = $"TPFan: {temp}°C | {status.Rpm} RPM | {modeStr}";
        if (tooltip.Length > 63) tooltip = tooltip[..63];
        _notifyIcon.Text = tooltip;

        // Dynamic badge icon showing temperature — swap first, then dispose old.
        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = newIcon;
        oldIcon?.Dispose();

        if (_statusMenuItem != null)
        {
            _statusMenuItem.Text = $"Status: {temp}°C | {status.Rpm} RPM | {modeStr}";
        }

        if (_autoMenuItem != null)
        {
            _autoMenuItem.Checked = !status.IsOverrideActive;
        }

        if (_overrideMenu != null)
        {
            foreach (ToolStripItem item in _overrideMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem mi)
                {
                    mi.Checked = status.IsOverrideActive && mi.Text.Contains($"{status.SpeedPercent}%");
                }
            }
        }
    }

    /// <summary>
    /// Generates a clean 16x16 / 32x32 bitmap icon rendered with the current temperature in bold digits.
    /// Colors transition from green (cool) to yellow (warm) to red (hot).
    /// </summary>
    private static Icon CreateTempIcon(int temp)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        // Color based on temperature
        Color textColor = temp switch
        {
            <= 45 => Color.FromArgb(76, 217, 100),   // Cool green
            <= 65 => Color.FromArgb(255, 204, 0),   // Warm yellow
            <= 80 => Color.FromArgb(255, 149, 0),   // Orange
            _ => Color.FromArgb(255, 59, 48)        // Hot red
        };

        // Dark rounded pill background
        using var bgBrush = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);

        // Draw temperature string
        var text = temp > 0 ? $"{temp}" : "--";
        using var font = new Font(FontFamily.GenericSansSerif, text.Length > 2 ? 11 : 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(textColor);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), sf);

        // Icon.FromHandle returns a temporary unmanaged icon — the HICON is
        // invalidated once the source HBITMAP is destroyed (end of using block).
        // We must duplicate it so the managed Icon owns a stable reference.
        var hIcon = bitmap.GetHicon();
        var tempIcon = Icon.FromHandle(hIcon);
        var icon = (Icon)tempIcon.Clone();
        DestroyIcon(hIcon); // release the temp unmanaged handle

        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer?.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _contextMenu?.Dispose();
        _trayForm?.Dispose();

        if (_uiThread != null && _uiThread.IsAlive)
        {
            Application.ExitThread();
        }

        GC.SuppressFinalize(this);
    }
}
