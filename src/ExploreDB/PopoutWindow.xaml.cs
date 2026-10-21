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
