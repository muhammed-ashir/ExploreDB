using ExploreDB.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ExploreDB;

public partial class SchemaPopoutPage : ContentPage, IDisposable
{
    private readonly ConnectionService _connectionService;

    public SchemaPopoutPage(string startPath, string title, ConnectionService connectionService)
    {
        InitializeComponent();
        
        _connectionService = connectionService;
        _connectionService.OnConnectionChanged += HandleConnectionChanged;

        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(ExploreDB.Components.SchemaPopoutRoutes),
            Parameters = new Dictionary<string, object?>
            {
                { "InitialRoute", startPath }
            }
        });

        Title = title;
        
        // Hide loader after a short delay since Blazor routing happens internally
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
        {
            blazorWebView.WidthRequest = -1;
            blazorWebView.HeightRequest = -1;
            NativeLoader.IsVisible = false;
        });
    }

    private void HandleConnectionChanged()
    {
        Dispatcher.Dispatch(() =>
        {
            if (Window != null)
            {
                Application.Current?.CloseWindow(Window);
            }
        });
    }

    public void Dispose()
    {
        if (_connectionService != null)
        {
            _connectionService.OnConnectionChanged -= HandleConnectionChanged;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Dispose();
    }
}
