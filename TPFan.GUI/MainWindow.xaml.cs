using System.Windows;
using TPFan.GUI.ViewModels;

namespace TPFan.GUI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        var provider = App.CurrentProvider;
        _vm = new MainViewModel(provider);
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
        Closed += (_, _) => _vm.Dispose();
    }
}
