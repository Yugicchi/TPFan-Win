using System.Windows;
using TPFan.GUI.ViewModels;

namespace TPFan.GUI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            var provider = App.CurrentProvider;
            _vm = new MainViewModel(provider);
            DataContext = _vm;
            _vm.Window = this;
            Loaded += async (_, _) => { try { await _vm.InitializeAsync(); } catch (Exception ex) { App.Log("Loaded init error: " + ex.Message); } };
            Closed += (_, _) => { try { _vm.Dispose(); } catch { } };
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    Hide();
                }
            };
        }
        catch (Exception ex)
        {
            App.Log("MainWindow ctor error: " + ex.Message);
            throw;
        }
    }
}