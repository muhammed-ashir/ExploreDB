using Microsoft.UI.Xaml;

namespace DbExplore.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "DbExploreCrash2.txt"), e.Exception.ToString());
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
