using Microsoft.Extensions.Logging;
using DbExplore.Services;

namespace DbExplore;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
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

        builder.Services.AddSingleton<ConnectionService>();
        builder.Services.AddSingleton<SchemaService>();
        builder.Services.AddSingleton<PathfinderService>();
        builder.Services.AddSingleton<QueryStateService>();

		return builder.Build();
	}
}
