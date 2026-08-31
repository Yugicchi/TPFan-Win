using System;
using System.Windows;

namespace TPFan.GUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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
    }
}