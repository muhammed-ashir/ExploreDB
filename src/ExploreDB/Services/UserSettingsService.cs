using System;
using System.IO;
using System.Text.Json;

namespace ExploreDB.Services;

public class UserSettings
{
    public bool AutoGenerateAliases { get; set; } = true;
    public bool UseAsKeyword { get; set; } = true;
    public bool KeepInClauseOnSingleLine { get; set; } = true;
    public string DisplayName { get; set; } = "";
    public string LicenseKey { get; set; } = "";
}

public class UserSettingsService
{
    private const string SettingsFileName = "user_settings.json";
    private readonly string _filePath;

    public UserSettings Settings { get; private set; } = new();
    public bool IsSettingsModalOpen { get; set; }

    public event Action? OnSettingsChanged;
    public event Action? OnSettingsModalStateChanged;

    public UserSettingsService()
    {
        _filePath = Path.Combine(AppEnvironmentService.AppDataDirectory, SettingsFileName);
        LoadSettings();
    }

    public void OpenSettingsModal()
    {
        IsSettingsModalOpen = true;
        OnSettingsModalStateChanged?.Invoke();
    }

    public void CloseSettingsModal()
    {
        IsSettingsModalOpen = false;
        OnSettingsModalStateChanged?.Invoke();
    }

    public void UpdateSettings(bool autoGenerateAliases, bool useAsKeyword, bool? keepInClauseOnSingleLine = null)
    {
        Settings.AutoGenerateAliases = autoGenerateAliases;
        Settings.UseAsKeyword = useAsKeyword;
        if (keepInClauseOnSingleLine.HasValue) Settings.KeepInClauseOnSingleLine = keepInClauseOnSingleLine.Value;
        SaveSettings();
        OnSettingsChanged?.Invoke();
    }

    public void UpdateKeepInClauseOnSingleLine(bool keepInClauseOnSingleLine)
    {
        Settings.KeepInClauseOnSingleLine = keepInClauseOnSingleLine;
        SaveSettings();
        OnSettingsChanged?.Invoke();
    }

    public void UpdateDisplayName(string displayName)
    {
        Settings.DisplayName = displayName;
        SaveSettings();
        OnSettingsChanged?.Invoke();
    }

    public void UpdateLicenseKey(string licenseKey)
    {
        Settings.LicenseKey = licenseKey;
        SaveSettings();
        OnSettingsChanged?.Invoke();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(AppEnvironmentService.AppDataDirectory);
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                }
            }
        }
        catch { }
    }
}
