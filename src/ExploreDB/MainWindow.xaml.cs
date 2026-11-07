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
        WindowIconHelper.ApplyWindowIcon(this);

        blazorWebView.Services = services;
        WebView2Helper.Configure(blazorWebView, "MainWindow");
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

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await WebView2Helper.SafeDisposeAsync(blazorWebView, "MainWindow");
    }
}
