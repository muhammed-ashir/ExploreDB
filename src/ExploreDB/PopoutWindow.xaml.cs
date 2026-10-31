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
        WindowIconHelper.ApplyWindowIcon(this);

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

        WebView2Helper.Configure(blazorWebView, $"PopoutWindow-{tabId}");
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await WebView2Helper.SafeDisposeAsync(blazorWebView, $"PopoutWindow-{TabId}");
    }
}
