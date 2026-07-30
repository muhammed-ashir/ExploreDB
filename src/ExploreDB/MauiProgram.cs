using Microsoft.Extensions.Logging;
using ExploreDB.Services;
using Microsoft.Extensions.Configuration;
using System.IO;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using System;

namespace ExploreDB;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		try {
			var builder = MauiApp.CreateBuilder();

            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(configPath))
            {
                builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: true);
            }

            // Setup Serilog
            int retentionDays = builder.Configuration.GetValue<int>("Logging:LogRetentionDays", 30);
            string logLevelStr = builder.Configuration.GetValue<string>("Logging:LogLevel", "Information");
            Serilog.Events.LogEventLevel logLevel = Enum.TryParse<Serilog.Events.LogEventLevel>(logLevelStr, true, out var level) ? level : Serilog.Events.LogEventLevel.Information;

            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logFilePath = Path.Combine(appDataFolder, "ExploreDB", "logs", "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: retentionDays
                )
                .CreateLogger();

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(dispose: true);

            // Catch any unhandled crashes at the global app level
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.Fatal(args.ExceptionObject as Exception, "FATAL: Unhandled Application Crash");
                Log.CloseAndFlush();
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Log.Fatal(args.Exception, "FATAL: Unobserved Background Task Crash");
            };

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
#endif

        builder.Services.AddSingleton<ConnectionService>();
        builder.Services.AddSingleton<SchemaService>();
        builder.Services.AddSingleton<PathfinderService>();
        builder.Services.AddSingleton<QueryStateService>();
        builder.Services.AddSingleton<GitHubUpdateService>();
        builder.Services.AddSingleton<StoreUpdateService>();
        builder.Services.AddSingleton<HistoryService>();

			return builder.Build();
		} catch (Exception ex) {
            // Log catastrophic startup failures to Desktop as a last resort
			File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ExploreDBCrash.txt"), ex.ToString());
			throw;
		}
	}
}
