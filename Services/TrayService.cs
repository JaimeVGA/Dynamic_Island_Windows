using System.Drawing;
using System.Windows.Forms;

namespace DynamicIsland.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _themesMenu = new("Tema");
    private readonly ToolStripMenuItem _startupItem = new("Iniciar con Windows");

    public TrayService(Settings settings)
    {
        _settings = settings;

        foreach (var theme in Themes.All)
        {
            var item = new ToolStripMenuItem(theme.Name) { Checked = theme.Name == settings.Theme };
            item.Click += (_, _) => SelectTheme(theme, item);
            _themesMenu.DropDownItems.Add(item);
        }

        _startupItem.Checked = Startup.IsEnabled();
        _startupItem.Click += (_, _) =>
        {
            Startup.Set(!_startupItem.Checked);
            _startupItem.Checked = Startup.IsEnabled();
        };

        var exit = new ToolStripMenuItem("Salir");
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { _themesMenu, _startupItem, new ToolStripSeparator(), exit });

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void SelectTheme(IslandTheme theme, ToolStripMenuItem selected)
    {
        foreach (ToolStripMenuItem i in _themesMenu.DropDownItems) i.Checked = i == selected;
        Themes.Apply(theme);
        _settings.Theme = theme.Name;
        _settings.Save();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
