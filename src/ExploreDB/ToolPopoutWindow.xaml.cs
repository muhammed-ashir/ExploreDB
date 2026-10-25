using System;
using System.Collections.Generic;
using System.Windows;
using ExploreDB.Components;
using ExploreDB.Services;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace ExploreDB;

public partial class ToolPopoutWindow : Window
{
    private readonly ConnectionService? _connectionService;
    private readonly bool _closeOnConnectionChange;

    public ToolPopoutWindow(string toolId, string startPath, string title, IServiceProvider services, bool closeOnConnectionChange = false)
    {
        InitializeComponent();
        Title = title;
        _closeOnConnectionChange = closeOnConnectionChange;

        if (_closeOnConnectionChange)
        {
            _connectionService = services.GetRequiredService<ConnectionService>();
            _connectionService.OnConnectionChanged += HandleConnectionChanged;
        }

        DarkModeHelper.ApplyDarkTitleBar(this);
        WindowIconHelper.ApplyWindowIcon(this);

        blazorWebView.Services = services;
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(SchemaPopoutRoutes),
            Parameters = new Dictionary<string, object?>
            {
                { "InitialRoute", startPath }
            }
        });

        Closed += (s, e) =>
        {
            if (_connectionService != null)
            {
                _connectionService.OnConnectionChanged -= HandleConnectionChanged;
            }
        };
    }

    private void HandleConnectionChanged()
    {
        Dispatcher.Invoke(Close);
    }
}
