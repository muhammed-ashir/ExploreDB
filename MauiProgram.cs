using Microsoft.Extensions.Logging;
using ExploreDB.Services;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Maui.LifecycleEvents;

namespace ExploreDB;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		try {
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			})
#if WINDOWS
            .ConfigureLifecycleEvents(events =>
            {
                events.AddWindows(wndLifeCycleBuilder =>
                {
                    wndLifeCycleBuilder.OnWindowCreated(window =>
                    {
                        try
                        {
                            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
                            
                            var iconPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Images\\app.ico");
                            appWindow.SetIcon(iconPath);
                        }
                        catch { }
                    });
                });
            });
#endif

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: true);
        }

        builder.Services.AddSingleton<ConnectionService>();
        builder.Services.AddSingleton<SchemaService>();
        builder.Services.AddSingleton<PathfinderService>();
        builder.Services.AddSingleton<QueryStateService>();
        builder.Services.AddSingleton<GitHubUpdateService>();

			return builder.Build();
		} catch (Exception ex) {
			File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ExploreDBCrash.txt"), ex.ToString());
			throw;
		}
	}
}
