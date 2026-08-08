using ExploreDB.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ExploreDB;

public partial class PopoutPage : ContentPage
{
    private readonly Guid _tabId;
    public Guid TabId => _tabId;
    private readonly QueryStateService _queryStateService;

    public PopoutPage(Guid tabId, QueryStateService queryStateService)
    {
        InitializeComponent();
        MauiProgram.AppReady += HandleAppReady;
        
        _tabId = tabId;
        _queryStateService = queryStateService;
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(ExploreDB.Components.PopoutRoot),
            Parameters = new Dictionary<string, object?>
            {
                { "TabId", tabId }
            }
        });
    }

    private void HandleAppReady(Guid? tabId)
    {
        if (tabId != _tabId) return; // Only process this specific popout window's ready event

        Dispatcher.Dispatch(() =>
        {
            blazorWebView.WidthRequest = -1;
            blazorWebView.HeightRequest = -1;
            NativeLoader.IsVisible = false;
        });
        MauiProgram.AppReady -= HandleAppReady;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        
        // Ensure title is set
        var tab = _queryStateService.Tabs.FirstOrDefault(t => t.Id == _tabId);
        if (tab != null && Window != null)
        {
            var windowTitle = string.IsNullOrEmpty(tab.FilePath) ? tab.Name : Path.GetFileName(tab.FilePath);
            if (windowTitle.Length > 100) windowTitle = windowTitle.Substring(0, 97) + "...";
            Window.Title = windowTitle;
        }
    }

    public bool IsDocking { get; set; } = false;

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        var tab = _queryStateService.Tabs.FirstOrDefault(t => t.Id == _tabId);
        if (tab != null)
        {
            if (IsDocking)
            {
                tab.IsPoppedOut = false;
                _queryStateService.ActiveTabId = tab.Id;
            }
            else
            {
                if (tab.QueryCts != null && !tab.QueryCts.IsCancellationRequested)
                {
                    try { tab.QueryCts.Cancel(); } catch { }
                }
                _queryStateService.Tabs.Remove(tab);
            }
            _queryStateService.NotifyTabUpdated(tab.Id);
            _ = _queryStateService.SaveTabsAsync();
        }
    }
}
