namespace TPFan.UWP.Services;

using Windows.Storage;

/// <summary>
/// Per-user settings persistence
/// </summary>
public class UserSettingsService
{
    private const string SettingsContainerName = "UserSettings";

    private readonly ApplicationDataContainer _settings;

    public UserSettingsService()
    {
        _settings = ApplicationData.Current.LocalSettings
            .CreateContainer(SettingsContainerName, Windows.Storage.ApplicationDataContainerCreateDisposition.Always);
    }

    public bool IsOverrideEnabled
    {
        get => GetValue<bool>(nameof(IsOverrideEnabled));
        set => SetValue(nameof(IsOverrideEnabled), value);
    }

    public int OverrideSpeedPercent
    {
        get => GetValue<int>(nameof(OverrideSpeedPercent), 50);
        set => SetValue(nameof(OverrideSpeedPercent), value);
    }

    public string SelectedPreset
    {
        get => GetValue<string>(nameof(SelectedPreset), "Default");
        set => SetValue(nameof(SelectedPreset), value);
    }

    public bool StartMinimized
    {
        get => GetValue<bool>(nameof(StartMinimized));
        set => SetValue(nameof(StartMinimized), value);
    }

    public bool MinimizeToTray
    {
        get => GetValue<bool>(nameof(MinimizeToTray), true);
        set => SetValue(nameof(MinimizeToTray), value);
    }

    public int UpdateIntervalSeconds
    {
        get => GetValue<int>(nameof(UpdateIntervalSeconds), 2);
        set => SetValue(nameof(UpdateIntervalSeconds), value);
    }

    private T GetValue<T>(string key, T defaultValue = default!)
    {
        if (_settings.Values.TryGetValue(key, out var value) && value != null)
        {
            return (T)value;
        }
        return defaultValue;
    }

    private void SetValue<T>(string key, T value)
    {
        _settings.Values[key] = value;
    }
}
