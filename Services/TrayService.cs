using System.Drawing;
using System.Windows.Forms;

namespace DynamicIsland.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayService(Action openSettings)
    {
        var settingsItem = new ToolStripMenuItem("Configuración…");
        settingsItem.Click += (_, _) => openSettings();

        var exit = new ToolStripMenuItem("Salir");
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { settingsItem, new ToolStripSeparator(), exit });

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null) return new Icon(info.Stream, new Size(32, 32));
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
