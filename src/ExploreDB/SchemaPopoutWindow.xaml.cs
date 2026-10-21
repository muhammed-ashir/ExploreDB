using System;
using System.Collections.Generic;
using System.Windows;
using ExploreDB.Components;
using ExploreDB.Services;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace ExploreDB;

public partial class SchemaPopoutWindow : Window
{
    private readonly ConnectionService _connectionService;

    public SchemaPopoutWindow(string startPath, string title, IServiceProvider services)
    {
        InitializeComponent();
        Title = title;

        _connectionService = services.GetRequiredService<ConnectionService>();
        _connectionService.OnConnectionChanged += HandleConnectionChanged;

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
            _connectionService.OnConnectionChanged -= HandleConnectionChanged;
        };
    }

    private void HandleConnectionChanged()
    {
        Dispatcher.Invoke(Close);
    }
}
