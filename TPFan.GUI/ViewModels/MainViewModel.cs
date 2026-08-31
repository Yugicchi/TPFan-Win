using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TPFan.GUI.Hardware;
using TPFan.Shared.Models;

namespace TPFan.GUI.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly T480FanProvider _provider;
    private DispatcherTimer? _pollTimer;
    private bool _disposed;

    private FanStatus _currentStatus = new()
    {
        TemperatureCelsius = 0, Rpm = 0, SpeedPercent = 0, IsOverrideActive = false
    };
    private FanCurve? _currentCurve;
    private int _selectedSpeedPercent;
    private bool _isOverrideEnabled;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(T480FanProvider? provider)
    {
        _provider = provider!;
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await PollStatusAsync(), System.Windows.Application.Current.Dispatcher)
        {
            IsEnabled = true
        };
    }

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
            // Apply immediately if override is active
            if (_isOverrideEnabled) { _ = ApplyOverrideAsync(); }
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

    public System.Windows.Media.Brush TemperatureColorBrush => TemperatureCelsius switch
    {
        <= 45 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964")),
        <= 65 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCC00")),
        <= 80 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9500")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"))
    };

    public System.Windows.Media.Brush ModeColorBrush => _currentStatus.IsOverrideActive
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9500"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964"));

    public System.Windows.Media.Brush ConnectionColorBrush => _provider != null
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"));

    public string ConnectionStatus => _provider != null ? "Hardware connected" : "Hardware unavailable";

    public FanCurve? FanCurve => _currentCurve;

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            if (_provider == null)
            {
                IsLoading = false;
                return;
            }

            try { _currentCurve = await _provider.DetectFanCurveAsync(); }
            catch { /* best effort */ }

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
        if (_provider == null || _disposed) return;
        try
        {
            var status = await _provider.GetFanStatusAsync();
            _currentStatus = status;
            OnPropertyChanged(nameof(TemperatureCelsius));
            OnPropertyChanged(nameof(SpeedPercent));
            OnPropertyChanged(nameof(Rpm));
            OnPropertyChanged(nameof(TemperatureDisplay));
            OnPropertyChanged(nameof(SpeedPercentDisplay));
            OnPropertyChanged(nameof(ModeDisplay));
            OnPropertyChanged(nameof(TemperatureColorBrush));
            OnPropertyChanged(nameof(ModeColorBrush));
            OnPropertyChanged(nameof(ConnectionColorBrush));
            OnPropertyChanged(nameof(ConnectionStatus));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Poll error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ApplyOverrideAsync()
    {
        if (_provider == null) return;
        try
        {
            await _provider.SetFanSpeedOverrideAsync(_selectedSpeedPercent);
            await PollStatusAsync();
        }
        catch { /* best effort */ }
    }

    private async System.Threading.Tasks.Task ResetOverrideAsync()
    {
        if (_provider == null) return;
        try
        {
            await _provider.ResetFanOverrideAsync();
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
        GC.SuppressFinalize(this);
    }
}