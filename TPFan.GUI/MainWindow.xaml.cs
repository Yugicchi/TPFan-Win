using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.FanCurve))
                DrawCurve(CurveCanvas);
        };
        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            DrawCurve(CurveCanvas); // initial draw after data loads
        };
        Closed += (_, _) => _vm.Dispose();
    }

    private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        canvas.Children.Clear();
        DrawCurve(canvas);
    }

    private void DrawCurve(Canvas canvas)
    {
        var curve = _vm.FanCurve;
        if (curve == null || curve.Points == null || curve.Points.Length == 0) return;

        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Find min/max for scaling
        var minTemp = curve.Points.Min(p => p.TemperatureCelsius);
        var maxTemp = curve.Points.Max(p => p.TemperatureCelsius);
        var minSpeed = curve.Points.Min(p => p.SpeedPercent);
        var maxSpeed = curve.Points.Max(p => p.SpeedPercent);

        var tempRange = Math.Max(1, maxTemp - minTemp);
        var speedRange = Math.Max(1, maxSpeed - minSpeed);

        var points = new PointCollection();
        foreach (var p in curve.Points)
        {
            var x = (p.TemperatureCelsius - minTemp) / (double)tempRange * w;
            var y = h - (p.SpeedPercent - minSpeed) / (double)speedRange * h;
            points.Add(new Point(x, y));
        }

        // Draw line
        var polyline = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4")),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(polyline);

        // Draw current point if available
        if (_vm.TemperatureCelsius > 0)
        {
            var cx = (_vm.TemperatureCelsius - minTemp) / (double)tempRange * w;
            var cy = h - (_vm.SpeedPercent - minSpeed) / (double)speedRange * h;
            var ellipse = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B00")),
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, cx - 5);
            Canvas.SetTop(ellipse, cy - 5);
            canvas.Children.Add(ellipse);
        }
    }
}