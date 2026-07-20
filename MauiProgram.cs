using Microsoft.Extensions.Logging;
using ExploreDB.Services;
using Microsoft.Extensions.Configuration;
using System.IO;

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
			});

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
