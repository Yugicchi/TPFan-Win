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
    private readonly HysteresisOptions _hyst = new();
    private float _lastTemp = float.MinValue;
    private int _lastLevel = -1;
    private int _changesThisMinute = 0;
    private DateTime _minuteWindow = DateTime.MinValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(T480FanProvider? provider)
    {
        _provider = provider!;
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, async (_, _) => await PollStatusAsync(), System.Windows.Application.Current.Dispatcher)
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
            // Snap to closest fan curve point
            var snapped = _currentCurve?.FindClosestSnapPoint(value) ?? value;
            if (_selectedSpeedPercent == snapped) return;
            _selectedSpeedPercent = snapped;
            OnPropertyChanged();
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
        System.Console.WriteLine($"[InitializeAsync] START provider={_provider != null}");
        IsLoading = true;
        try
        {
            if (_provider == null)
            {
                System.Console.WriteLine("[InitializeAsync] SKIP: provider is null");
                IsLoading = false;
                return;
            }

            try
            {
                System.Console.WriteLine("[InitializeAsync] Detecting fan curve...");
                _currentCurve = await _provider.DetectFanCurveAsync();
                System.Console.WriteLine($"[InitializeAsync] Curve detected: {_currentCurve?.Points?.Length} points");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[InitializeAsync] DetectFanCurveAsync error: {ex.Message}");
            }

            System.Console.WriteLine("[InitializeAsync] Polling status...");
            await PollStatusAsync();
            System.Console.WriteLine("[InitializeAsync] DONE");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[InitializeAsync] ERROR: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async System.Threading.Tasks.Task PollStatusAsync()
    {
        System.Console.WriteLine($"[PollStatusAsync] START provider={_provider != null}, disposed={_disposed}");
        System.Diagnostics.Debug.WriteLine($"[PollStatusAsync] START provider={_provider != null}");
        if (_provider == null || _disposed)
        {
            System.Console.WriteLine("[PollStatusAsync] SKIP: provider null or disposed");
            return;
        }
        try
        {
            Diag.Log($"[Poll] START");
            var status = await _provider.GetFanStatusAsync();
            Diag.Log($"[Poll] Temp={status.TemperatureCelsius:F1}°C RPM={status.Rpm} Speed={status.SpeedPercent}% Override={status.IsOverrideActive}");

            // Hysteresis / anti-hunting: don't react to noise within deadband
            if (Math.Abs(status.TemperatureCelsius - _lastTemp) < _hyst.DeadbandCelsius && _lastLevel >= 0)
            {
                Diag.Log($"[Hyst] Deadband active: {status.TemperatureCelsius:F1}°C within ±{_hyst.DeadbandCelsius}°C of {_lastTemp:F1}°C — skip level change");
            }
            else
            {
                _lastTemp = status.TemperatureCelsius;
            }

            // Rate limit changes (anti-hunting): max 3/min
            if (DateTime.Now > _minuteWindow.AddMinutes(1))
            {
                _minuteWindow = DateTime.Now;
                _changesThisMinute = 0;
            }
            if (_changesThisMinute >= _hyst.MaxChangesPerMinute)
            {
                Diag.Log($"[Hyst] Rate limit: {_changesThisMinute} changes/min reached — skip");
            }
            System.Console.WriteLine($"[PollStatusAsync] Status: Temp={status.TemperatureCelsius}, Speed={status.SpeedPercent}, RPM={status.Rpm}, Override={status.IsOverrideActive}");
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
            OnPropertyChanged(nameof(FanCurve)); // trigger canvas redraw
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[PollStatusAsync] ERROR: {ex.Message}");
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