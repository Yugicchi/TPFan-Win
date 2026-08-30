namespace TPFan.UWP.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Services;
using Shared.Models;

/// <summary>
/// Main ViewModel for fan control
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly FanServiceClient _serviceClient;
    private readonly UserSettingsService _settingsService;

    private FanCurve? _currentCurve;
    private FanStatus? _currentStatus;
    private int _selectedSpeedPercent;
    private bool _isOverrideEnabled;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _serviceClient = new FanServiceClient();
        _settingsService = new UserSettingsService();

        SnapPoints = [];
        LoadSettings();
    }

    public FanCurve? CurrentCurve
    {
        get => _currentCurve;
        private set
        {
            _currentCurve = value;
            OnPropertyChanged();
            UpdateSnapPoints();
        }
    }

    public FanStatus? CurrentStatus
    {
        get => _currentStatus;
        private set
        {
            _currentStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public int SelectedSpeedPercent
    {
        get => _selectedSpeedPercent;
        set
        {
            if (_selectedSpeedPercent != value)
            {
                _selectedSpeedPercent = SnapToClosestPoint(value);
                OnPropertyChanged();
                _settingsService.OverrideSpeedPercent = _selectedSpeedPercent;
            }
        }
    }

    public bool IsOverrideEnabled
    {
        get => _isOverrideEnabled;
        set
        {
            if (_isOverrideEnabled != value)
            {
                _isOverrideEnabled = value;
                OnPropertyChanged();
                _settingsService.IsOverrideEnabled = value;

                if (value)
                {
                    _ = ApplyOverrideAsync();
                }
                else
                {
                    _ = ResetOverrideAsync();
                }
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string StatusText => CurrentStatus?.ToString() ?? "No data";

    public ObservableCollection<int> SnapPoints { get; }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Check service
            var isRunning = await _serviceClient.IsServiceRunningAsync();
            if (!isRunning)
            {
                // Service not running - show placeholder data for UI testing
                LoadPlaceholderData();
                return;
            }

            // Load fan curve
            CurrentCurve = await _serviceClient.GetFanCurveAsync();
            CurrentStatus = await _serviceClient.GetFanStatusAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization error: {ex.Message}");
            LoadPlaceholderData();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadSettings()
    {
        _isOverrideEnabled = _settingsService.IsOverrideEnabled;
        _selectedSpeedPercent = _settingsService.OverrideSpeedPercent;
    }

    private void LoadPlaceholderData()
    {
        // T480 typical fan curve for UI testing
        CurrentCurve = new FanCurve
        {
            Name = "T480 Default",
            Points =
            [
                new FanCurvePoint { TemperatureCelsius = 30, SpeedPercent = 0 },
                new FanCurvePoint { TemperatureCelsius = 40, SpeedPercent = 20 },
                new FanCurvePoint { TemperatureCelsius = 50, SpeedPercent = 30 },
                new FanCurvePoint { TemperatureCelsius = 60, SpeedPercent = 40 },
                new FanCurvePoint { TemperatureCelsius = 70, SpeedPercent = 60 },
                new FanCurvePoint { TemperatureCelsius = 80, SpeedPercent = 80 },
                new FanCurvePoint { TemperatureCelsius = 90, SpeedPercent = 100 }
            ],
            CurrentTemperatureCelsius = 45,
            CurrentSpeedPercent = 25,
            CurrentRpm = 2850
        };

        CurrentStatus = new FanStatus
        {
            TemperatureCelsius = 45,
            SpeedPercent = 25,
            Rpm = 2850
        };
    }

    private void UpdateSnapPoints()
    {
        SnapPoints.Clear();
        if (_currentCurve == null) return;

        foreach (var point in _currentCurve.GetSnapPoints())
        {
            SnapPoints.Add(point);
        }
    }

    private int SnapToClosestPoint(int value)
    {
        if (_currentCurve == null) return value;
        return _currentCurve.FindClosestSnapPoint(value);
    }

    private async Task ApplyOverrideAsync()
    {
        if (_currentCurve == null) return;
        await _serviceClient.SetFanSpeedOverrideAsync(_selectedSpeedPercent);
    }

    private async Task ResetOverrideAsync()
    {
        await _serviceClient.ResetFanOverrideAsync();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
