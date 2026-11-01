using System;
using System.IO;
using System.Text.Json;

namespace ExploreDB.Services;

public class UserSettings
{
    public bool AutoGenerateAliases { get; set; } = true;
    public bool UseAsKeyword { get; set; } = true;
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

    public void UpdateSettings(bool autoGenerateAliases, bool useAsKeyword)
    {
        Settings.AutoGenerateAliases = autoGenerateAliases;
        Settings.UseAsKeyword = useAsKeyword;
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
