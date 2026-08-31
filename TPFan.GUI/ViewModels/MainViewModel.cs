using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using TPFan.GUI.Services;
using TPFan.Shared.Models;

namespace TPFan.GUI.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly FanServiceClient _client = new();
    private DispatcherTimer? _pollTimer;
    private bool _disposed;

    private FanStatus _currentStatus = new()
    {
        TemperatureCelsius = 32, Rpm = 0, SpeedPercent = 0, IsOverrideActive = false
    };
    private FanCurve? _currentCurve;
    private int _selectedSpeedPercent;
    private bool _isOverrideEnabled;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TemperatureCelsius => (int)Math.Round((double)_currentStatus.TemperatureCelsius);

    public int SpeedPercent => _currentStatus.SpeedPercent;

    public int Rpm => _currentStatus.Rpm;

    public bool IsOverrideEnabled
    {
        get => _isOverrideEnabled;
        set
        {
            if (_isOverrideEnabled == value) return;
            _isOverrideEnabled = value;
            OnPropertyChanged();
            if (value) { _ = ApplyOverrideAsync(); } else { _ = ResetOverrideAsync(); }
        }
    }

    public int SelectedSpeedPercent
    {
        get => _selectedSpeedPercent;
        set
        {
            _selectedSpeedPercent = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string TemperatureDisplay => TemperatureCelsius > 0 ? $"{TemperatureCelsius}°" : "--°";

    public string SpeedPercentDisplay => $"{SpeedPercent}%";

    public string ModeDisplay => _currentStatus.IsOverrideActive ? "Manual" : "Auto";

    public Brush TemperatureColorBrush => TemperatureCelsius switch
    {
        <= 45 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964")),
        <= 65 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCC00")),
        <= 80 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9500")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"))
    };

    public Brush ModeColorBrush => _currentStatus.IsOverrideActive
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9500"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964"));

    public Brush ConnectionColorBrush => IsServiceRunning ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"));

    public string ConnectionStatus => IsServiceRunning ? "Connected to TPFan Service" : "Service unavailable";

    public bool IsServiceRunning { get; private set; }

    public FanCurve? FanCurve => _currentCurve;

    public MainViewModel()
    {
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await PollStatusAsync(), System.Windows.Application.Current.Dispatcher)
        {
            IsEnabled = true
        };
    }

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            IsServiceRunning = await _client.IsServiceRunningAsync();
            // Don't stop polling if service not running - we'll keep trying
            // and the ServiceLauncher will start it

            try { _currentCurve = await _client.GetFanCurveAsync(); } catch { }
            await PollStatusAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Init error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async System.Threading.Tasks.Task PollStatusAsync()
    {
        try
        {
            var status = await _client.GetFanStatusAsync();
            _currentStatus = status;
            IsServiceRunning = true;
            OnPropertyChanged(nameof(TemperatureCelsius));
            OnPropertyChanged(nameof(SpeedPercent));
            OnPropertyChanged(nameof(Rpm));
            OnPropertyChanged(nameof(TemperatureDisplay));
            OnPropertyChanged(nameof(SpeedPercentDisplay));
            OnPropertyChanged(nameof(TemperatureColorBrush));
            OnPropertyChanged(nameof(ModeDisplay));
            OnPropertyChanged(nameof(ModeColorBrush));
            OnPropertyChanged(nameof(IsServiceRunning));
            OnPropertyChanged(nameof(ConnectionColorBrush));
            OnPropertyChanged(nameof(ConnectionStatus));
        }
        catch
        {
            IsServiceRunning = false;
            OnPropertyChanged(nameof(IsServiceRunning));
            OnPropertyChanged(nameof(ConnectionColorBrush));
            OnPropertyChanged(nameof(ConnectionStatus));
        }
    }

    private async System.Threading.Tasks.Task ApplyOverrideAsync()
    {
        try
        {
            await _client.SetFanSpeedOverrideAsync(_selectedSpeedPercent);
            await PollStatusAsync();
        }
        catch { /* best effort */ }
    }

    private async System.Threading.Tasks.Task ResetOverrideAsync()
    {
        try
        {
            await _client.ResetFanOverrideAsync();
            await PollStatusAsync();
        }
        catch { /* best effort */ }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Stop();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}