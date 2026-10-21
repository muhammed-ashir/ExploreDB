using System;
using System.ComponentModel;
using System.Windows;
using ExploreDB.Services;

namespace ExploreDB;

public partial class MainWindow : Window
{
    private readonly QueryStateService? _queryStateService;

    public MainWindow(IServiceProvider services, QueryStateService queryStateService)
    {
        InitializeComponent();
        _queryStateService = queryStateService;

        DarkModeHelper.ApplyDarkTitleBar(this);

        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
            }
        }
        catch { }

        blazorWebView.Services = services;
    }

    public static bool IsExitingConfirmed { get; set; } = false;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (IsExitingConfirmed)
        {
            return;
        }

        // Cancel the immediate window close so the Blazor confirmation modal can be displayed
        e.Cancel = true;

        // Ensure open tabs are persisted when closing the main window
        if (_queryStateService != null)
        {
            try
            {
                _ = _queryStateService.SaveTabsAsync();
            }
            catch { }
        }

        App.TriggerRequestAppExit();
    }
}
