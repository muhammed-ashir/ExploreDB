namespace ExploreDB;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		MainPage = new MainPage();
	}

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Title = "ExploreDB";

        // When the main window is destroyed, explicitly quit the entire application
        // so that any spawned popout windows are also cleanly closed.
        window.Destroying += (s, e) =>
        {
            if (window.Page is MainPage)
            {
                Environment.Exit(0);
            }
        };

        return window;
    }
}
