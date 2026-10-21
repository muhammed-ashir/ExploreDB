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

        blazorWebView.Services = services;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Ensure open tabs are persisted when closing the main window
        if (_queryStateService != null)
        {
            try
            {
                _ = _queryStateService.SaveTabsAsync();
            }
            catch { }
        }
    }
}
