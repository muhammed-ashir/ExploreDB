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
    public static event Action? RequestAppExit;
    public static event Action<Guid?>? AppReady;
    
    public static void InvokeAppReady(Guid? tabId = null)
    {
        AppReady?.Invoke(tabId);
    }
    
    private static bool _isMainWindowCreated = false;
#if WINDOWS
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void ApplyDarkTitleBar(Microsoft.UI.Windowing.AppWindow appWindow, IntPtr hwnd)
    {
        try
        {
            if (hwnd != IntPtr.Zero)
            {
                int darkMode = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                }
            }

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                if (titleBar != null)
                {
                    var fg = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                    var inactiveFg = Windows.UI.Color.FromArgb(255, 128, 128, 128);
                    var hoverBg = Windows.UI.Color.FromArgb(255, 45, 50, 56);
                    var pressedBg = Windows.UI.Color.FromArgb(255, 60, 65, 72);

                    titleBar.ButtonForegroundColor = fg;
                    titleBar.ButtonBackgroundColor = transparent;
                    titleBar.ButtonHoverForegroundColor = fg;
                    titleBar.ButtonHoverBackgroundColor = hoverBg;
                    titleBar.ButtonPressedForegroundColor = fg;
                    titleBar.ButtonPressedBackgroundColor = pressedBg;
                    titleBar.ButtonInactiveForegroundColor = inactiveFg;
                    titleBar.ButtonInactiveBackgroundColor = transparent;
                }
            }
        }
        catch { }
    }

    public static void UpdateTitleBarTheme(bool isDark)
    {
        // Title bar and caption buttons are kept permanently dark
    }
#endif

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

                            // Permanently apply dark title bar and seamless caption buttons
                            ApplyDarkTitleBar(appWindow, handle);

                            if (!_isMainWindowCreated)
                            {
                                _isMainWindowCreated = true;

                                // Maximize the main window on startup
                                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                                {
                                    presenter.Maximize();
                                }

                                appWindow.Closing += (sender, args) =>
                                {
                                    args.Cancel = true;
                                    RequestAppExit?.Invoke();
                                };
                            }
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
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<SnippetService>();

			return builder.Build();
		} catch (Exception ex) {
            // Log catastrophic startup failures to Desktop as a last resort
			File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ExploreDBCrash.txt"), ex.ToString());
			throw;
		}
	}
}
