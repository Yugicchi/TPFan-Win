namespace TPFan.UWP.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViewModels;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void SnapPointButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Content is string content)
        {
            if (int.TryParse(content, out var speed))
            {
                ViewModel.SelectedSpeedPercent = speed;
            }
        }
    }
}
