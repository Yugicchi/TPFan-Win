using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
    private bool _isManual;
    private bool _isLoading;
    private int _selectedSpeedPercent;
    private readonly HysteresisOptions _hyst = new();
    private float _lastTemp = float.MinValue;
    private int _lastLevel = -1;
    private int _changesThisMinute = 0;
    private DateTime _minuteWindow = DateTime.MinValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public System.Windows.Input.ICommand ResetFanOverrideCommand { get; }
    public System.Windows.Input.ICommand ApplyFanOverrideCommand { get; }
    public System.Windows.Input.ICommand MinimizeToTrayCommand { get; }

    public MainViewModel(T480FanProvider? provider)
    {
        _provider = provider!;
        ResetFanOverrideCommand = new RelayCommand(async () => await ResetOverrideAsync());
        ApplyFanOverrideCommand = new RelayCommand(async () => await ApplyOverrideAsync());
        MinimizeToTrayCommand = new RelayCommand(() =>
        {
            if (Window != null) Window.Hide();
        });

        _pollTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Normal,
            async (_, _) => await PollStatusAsync(),
            System.Windows.Application.Current.Dispatcher)
        {
            IsEnabled = true
        };
    }

    public Window? Window { get; set; }

    // Single source of truth: false=Auto, true=Manual
    public bool IsManual
    {
        get => _isManual;
        set
        {
            if (_isManual == value) return;
            _isManual = value;
            OnPropertyChanged();
            if (value) { _ = ApplyOverrideAsync(); } else { _ = ResetOverrideAsync(); }
        }
    }

    public bool IsOverrideEnabled
    {
        get => _isManual;
        set { IsManual = value; }
    }

    public int TemperatureCelsius => (int)Math.Round((double)_currentStatus.TemperatureCelsius);

    public int SpeedPercent => _currentStatus.SpeedPercent;

    public int Rpm => _currentStatus.Rpm;

    public int[] FanCurveSnapPoints => _currentCurve?.Points
        ?.Select(p => p.SpeedPercent)
        .Distinct()
        .OrderBy(s => s)
        .ToArray() ?? new[] { 0, 25, 50, 75, 100 };

    public int SelectedSpeedPercent
    {
        get => _selectedSpeedPercent;
        set
        {
            // Snap to closest fan curve point; always apply snap even when value unchanged
            // to prevent stale 30% from systray overriding slider
            var snapped = _currentCurve?.FindClosestSnapPoint(value) ?? value;
            _selectedSpeedPercent = snapped;
            OnPropertyChanged();
            if (_isManual) { _ = ApplyOverrideAsync(); }
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

    public Brush ConnectionColorBrush => _provider != null
        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CD964"))
        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B30"));

    public string ConnectionStatus => _provider != null ? "Hardware connected" : "Hardware unavailable";

    public FanCurve? FanCurve => _currentCurve;

    public async Task InitializeAsync()
    {
        Diag.Log($"[Init] START provider={_provider != null}");
        IsLoading = true;
        IsManual = false;
        try
        {
            if (_provider == null)
            {
                Diag.Log("[Init] SKIP: provider is null");
                return;
            }

            try
            {
                Diag.Log("[Init] Detecting fan curve...");
                _currentCurve = await _provider.DetectFanCurveAsync();
                Diag.Log($"[Init] Curve detected: {_currentCurve?.Points?.Length} points, snap points: [{string.Join(", ", FanCurveSnapPoints)}]");
            }
            catch (Exception ex)
            {
                Diag.Log($"[Init] DetectFanCurveAsync error: {ex.Message}");
            }

            Diag.Log("[Init] Polling status...");
            await PollStatusAsync();
            Diag.Log("[Init] DONE");
        }
        catch (Exception ex)
        {
            Diag.Log($"[Init] ERROR: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PollStatusAsync()
    {
        Diag.Log($"[Poll] START provider={_provider != null}, disposed={_disposed}");
        if (_provider == null || _disposed)
        {
            Diag.Log("[Poll] SKIP: provider null or disposed");
            return;
        }
        try
        {
            var status = await _provider.GetFanStatusAsync();
            Diag.Log($"[Poll] Temp={status.TemperatureCelsius:F1}°C RPM={status.Rpm} Speed={status.SpeedPercent}% Override={status.IsOverrideActive}");

            if (Math.Abs(status.TemperatureCelsius - _lastTemp) < _hyst.DeadbandCelsius && _lastLevel >= 0)
            {
                Diag.Log($"[Hyst] Deadband active: {status.TemperatureCelsius:F1}°C within ±{_hyst.DeadbandCelsius}°C of {_lastTemp:F1}°C — skip level change");
            }
            else
            {
                _lastTemp = status.TemperatureCelsius;
            }

            if (DateTime.Now > _minuteWindow.AddMinutes(1))
            {
                _minuteWindow = DateTime.Now;
                _changesThisMinute = 0;
            }
            if (_changesThisMinute >= _hyst.MaxChangesPerMinute)
            {
                Diag.Log($"[Hyst] Rate limit: {_changesThisMinute} changes/min reached — skip");
            }

            _currentStatus = status;
            // Sync slider to override value when manual mode active — force even if same to clear stale snap
            if (status.IsOverrideActive)
            {
                var target = _currentCurve?.FindClosestSnapPoint(status.SpeedPercent) ?? status.SpeedPercent;
                if (_selectedSpeedPercent != target)
                {
                    _selectedSpeedPercent = target;
                    OnPropertyChanged(nameof(SelectedSpeedPercent));
                }
            }
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
            OnPropertyChanged(nameof(FanCurve));
            OnPropertyChanged(nameof(FanCurveSnapPoints));
        }
        catch (Exception ex)
        {
            Diag.Log($"[Poll] ERROR: {ex.Message}");
        }
    }

    private async Task ApplyOverrideAsync()
    {
        if (_provider == null) return;
        try
        {
            await _provider.SetFanSpeedOverrideAsync(_selectedSpeedPercent);
            await PollStatusAsync();
        }
        catch { /* best effort */ }
    }

    private async Task ResetOverrideAsync()
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