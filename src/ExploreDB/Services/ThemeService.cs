using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Microsoft.Maui.Storage;

namespace ExploreDB.Services;

public class ThemeService
{
    private const string ThemeKey = "AppTheme";
    private string _currentTheme = "dark";
    private IJSRuntime? _js;

    public string CurrentTheme => _currentTheme;
    public bool IsDark => _currentTheme == "dark";

    public event Action? OnThemeChanged;

    public ThemeService()
    {
        try
        {
            _currentTheme = Preferences.Default.Get<string>(ThemeKey, "dark");
            if (_currentTheme != "light" && _currentTheme != "dark")
            {
                _currentTheme = "dark";
            }
        }
        catch
        {
            _currentTheme = "dark";
        }
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
            Preferences.Default.Set<string>(ThemeKey, _currentTheme);
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
