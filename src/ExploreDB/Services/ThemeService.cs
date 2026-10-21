using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace ExploreDB.Services;

public class ThemeService
{
    private readonly string _themeFilePath = Path.Combine(AppEnvironmentService.AppDataDirectory, "theme.txt");
    private string _currentTheme = "dark";
    private IJSRuntime? _js;

    public string CurrentTheme => _currentTheme;
    public bool IsDark => _currentTheme == "dark";

    public event Action? OnThemeChanged;

    public ThemeService()
    {
        try
        {
            if (File.Exists(_themeFilePath))
            {
                var saved = File.ReadAllText(_themeFilePath).Trim();
                if (saved == "light" || saved == "dark")
                {
                    _currentTheme = saved;
                    return;
                }
            }
        }
        catch { }
        _currentTheme = "dark";
    }

    public async Task InitializeAsync(IJSRuntime js)
    {
        _js = js;
        await ApplyThemeAsync(_currentTheme);
    }

    public async Task ToggleThemeAsync()
    {
        var target = IsDark ? "light" : "dark";
        await SetThemeAsync(target);
    }

    public async Task SetThemeAsync(string theme)
    {
        if (theme != "light" && theme != "dark") theme = "dark";
        
        _currentTheme = theme;
        try
        {
            File.WriteAllText(_themeFilePath, _currentTheme);
        }
        catch { }

        await ApplyThemeAsync(_currentTheme);
        OnThemeChanged?.Invoke();
    }

    private async Task ApplyThemeAsync(string theme)
    {
        if (_js != null)
        {
            try
            {
                await _js.InvokeVoidAsync("setAppTheme", theme);
            }
            catch { }
        }
    }
}
