using System;
using System.IO;
using System.Windows;
using ExploreDB.Services;
using ExploreDB.Services.SqlCompare;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ExploreDB;

public partial class App : Application
{
    public static event Action? RequestAppExit;
    public static void TriggerRequestAppExit() => RequestAppExit?.Invoke();
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Build configuration
            var configBuilder = new ConfigurationBuilder();
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(configPath))
            {
                configBuilder.AddJsonFile(configPath, optional: true, reloadOnChange: true);
            }
            var configuration = configBuilder.Build();

            // Configure Serilog
            int retentionDays = configuration.GetValue<int>("Logging:LogRetentionDays", 30);
            string logLevelStr = configuration.GetValue<string>("Logging:LogLevel", "Information");
            Serilog.Events.LogEventLevel logLevel = Enum.TryParse<Serilog.Events.LogEventLevel>(logLevelStr, true, out var level)
                ? level
                : Serilog.Events.LogEventLevel.Information;

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

            // Setup global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.Fatal(args.ExceptionObject as Exception, "FATAL: Unhandled Application Crash");
                Log.CloseAndFlush();
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Log.Fatal(args.Exception, "FATAL: Unobserved Background Task Crash");
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                Log.Error(args.Exception, "Dispatcher Unhandled Exception");
                // Log and keep application alive if possible
                args.Handled = true;
            };

            // Configure Dependency Injection
            var services = new ServiceCollection();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(dispose: true);
            });

            services.AddWpfBlazorWebView();

#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif

            // Register Business Services
            services.AddSingleton<ConnectionService>();
            services.AddSingleton<SchemaService>();
            services.AddSingleton<PathfinderService>();
            services.AddSingleton<QueryStateService>();
            services.AddSingleton<GitHubUpdateService>();
            services.AddSingleton<StoreUpdateService>();
            services.AddSingleton<HistoryService>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<SnippetService>();
            services.AddSingleton<WindowManagerService>();
            services.AddSingleton<DataCleanupService>();
            services.AddSingleton<ISchemaComparisonService, SchemaComparisonService>();
            services.AddSingleton<IHtmlReportGenerator, HtmlReportGenerator>();
            services.AddSingleton<ToolRegistryService>();

            // Register Windows
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();

            // Launch Main Window
            var mainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // Catastrophic crash backup logger
            try
            {
                File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ExploreDBCrash.txt"), ex.ToString());
            }
            catch { }
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
