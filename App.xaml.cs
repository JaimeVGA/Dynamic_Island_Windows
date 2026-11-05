using System.Threading;
using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class App : Application
{
    private Mutex? _mutex;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DynamicIsland.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        base.OnStartup(e);

        var settings = Settings.Load();
        Themes.Apply(Themes.Find(settings.Theme));

        var window = new MainWindow();
        window.Show();

        _tray = new TrayService(settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
