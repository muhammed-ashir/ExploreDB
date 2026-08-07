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
        
        _tabId = tabId;
        _queryStateService = queryStateService;

        // Dynamically add the RootComponent with the TabId parameter
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.PopoutRoot),
            Parameters = new Dictionary<string, object?>
            {
                { "TabId", _tabId }
            }
        });
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
