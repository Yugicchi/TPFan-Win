namespace TPFan.UWP;

using Microsoft.UI.Xaml;
using Views;
using Converters;

sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // Register converters in app resources
        Current.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window();
        window.Content = new MainPage();
        window.Title = "ThinkPad T480 Fan Control";
        window.Activate();
    }
}
