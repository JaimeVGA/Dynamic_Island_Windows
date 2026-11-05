using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private readonly Settings _settings;
    private bool _loading = true;

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        ModeClassic.IsChecked = settings.Mode == IslandMode.Classic;
        ModeBar.IsChecked = settings.Mode == IslandMode.Bar;
        ModeWide.IsChecked = settings.Mode == IslandMode.Wide;
        Use24HourBox.IsChecked = settings.Use24Hour;
        StartupBox.IsChecked = Startup.IsEnabled();

        AutoBox.IsChecked = settings.AutoSwitch;
        BuildAutoRows();
        UpdateAutoState();

        AutoBox.Click += (_, _) => { _settings.AutoSwitch = AutoBox.IsChecked == true; UpdateAutoState(); _settings.Commit(); };
        ModeClassic.Checked += (_, _) => SetMode(IslandMode.Classic);
        ModeBar.Checked += (_, _) => SetMode(IslandMode.Bar);
        ModeWide.Checked += (_, _) => SetMode(IslandMode.Wide);
        Use24HourBox.Click += (_, _) => { _settings.Use24Hour = Use24HourBox.IsChecked == true; _settings.Commit(); };
        StartupBox.Click += (_, _) => { Startup.Set(StartupBox.IsChecked == true); StartupBox.IsChecked = Startup.IsEnabled(); };

        BuildThemes();
        _loading = false;
    }

    public static void ShowSingle(Settings settings)
    {
        if (_instance == null)
        {
            _instance = new SettingsWindow(settings);
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private void BuildAutoRows()
    {
        AddAutoRow("Con una app maximizada", () => _settings.ModeMaximized, m => _settings.ModeMaximized = m, "grpMax");
        AddAutoRow("Cuando suena música", () => _settings.ModePlaying, m => _settings.ModePlaying = m, "grpPlay");
        AddAutoRow("En el escritorio (sin música ni apps maximizadas)", () => _settings.ModeIdle, m => _settings.ModeIdle = m, "grpIdle");
    }

    private void AddAutoRow(string label, Func<IslandMode> get, Action<IslandMode> set, string group)
    {
        AutoPanel.Children.Add(new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            FontSize = 12, Margin = new Thickness(0, 8, 0, 6)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (mode, text) in new[] { (IslandMode.Classic, "Clásico"), (IslandMode.Bar, "Barra delgada"), (IslandMode.Wide, "Barra ancha") })
        {
            var chip = new RadioButton
            {
                Content = text, GroupName = group, IsChecked = get() == mode,
                Style = (Style)FindResource("Chip")
            };
            chip.Checked += (_, _) =>
            {
                if (_loading) return;
                set(mode);
                _settings.Commit();
            };
            row.Children.Add(chip);
        }
        AutoPanel.Children.Add(row);
    }
    /// <summary>Con el modo automático activo, el selector manual no aplica.</summary>
    private void UpdateAutoState()
    {
        bool auto = _settings.AutoSwitch;
        AutoPanel.IsEnabled = auto;
        AutoPanel.Opacity = auto ? 1 : 0.4;
        ManualPanel.IsEnabled = !auto;
        ManualPanel.Opacity = auto ? 0.4 : 1;
    }

    private void SetMode(IslandMode mode)
    {
        if (_loading) return;
        _settings.Mode = mode;
        _settings.Commit();
    }

    private void BuildThemes()
    {
        ThemePanel.Children.Clear();
        foreach (var theme in Themes.All)
        {
            bool selected = theme.Name == _settings.Theme;
            var card = new Border
            {
                Width = 150, Height = 64, Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                Background = Brush(theme.Background),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? Brush("#60A5FA") : Brush("#3F3F46"),
                Cursor = Cursors.Hand,
                ToolTip = theme.Name
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            row.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brush(theme.Accent), Margin = new Thickness(0, 0, 10, 0) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = theme.Name, Foreground = Brush(theme.Primary), FontWeight = FontWeights.SemiBold, FontSize = 13 });
            text.Children.Add(new Border { Height = 3, Width = 40, CornerRadius = new CornerRadius(2), Background = Brush(theme.Track), Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            row.Children.Add(text);
            card.Child = row;

            card.MouseLeftButtonUp += (_, _) =>
            {
                _settings.Theme = theme.Name;
                _settings.Commit();
                BuildThemes();
            };
            ThemePanel.Children.Add(card);
        }
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));
}
