namespace ExploreDB;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
        MauiProgram.AppReady += HandleAppReady;
	}

    private void HandleAppReady(Guid? tabId)
    {
        if (tabId != null) return; // Only process the main window's ready event

        Dispatcher.Dispatch(() =>
        {
            blazorWebView.WidthRequest = -1;
            blazorWebView.HeightRequest = -1;
            NativeLoader.IsVisible = false;
        });
        MauiProgram.AppReady -= HandleAppReady;
    }
}
