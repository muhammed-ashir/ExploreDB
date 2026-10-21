using System;
using System.Collections.Generic;
using System.Windows;
using ExploreDB.Components;
using Microsoft.AspNetCore.Components.WebView.Wpf;

namespace ExploreDB;

public partial class PopoutWindow : Window
{
    public Guid TabId { get; }
    public bool IsDocking { get; set; } = false;

    public PopoutWindow(Guid tabId, IServiceProvider services)
    {
        InitializeComponent();
        TabId = tabId;

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
            ComponentType = typeof(PopoutRoot),
            Parameters = new Dictionary<string, object?>
            {
                { "TabId", tabId }
            }
        });
    }
}
