using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Services;

public sealed record IslandTheme(string Name, string Background, string Primary, string Secondary, string Accent, string Track);

public static class Themes
{
    public static readonly IslandTheme[] All =
    {
        new("Medianoche", "#000000", "#FFFFFF", "#A0A0A8", "#FFFFFF", "#3A3A3F"),
        new("Spotify",    "#0B0F0C", "#FFFFFF", "#9DB3A4", "#1DB954", "#2A3A2F"),
        new("Océano",     "#06121F", "#E8F4FF", "#7FA8C9", "#38BDF8", "#1C3350"),
        new("Atardecer",  "#1A0B12", "#FFF1E6", "#C9998A", "#FB7185", "#4A2330"),
        new("Violeta",    "#0F0A1E", "#F3EEFF", "#A596C9", "#A78BFA", "#2E2350"),
        new("Ámbar",      "#140E04", "#FFF7E0", "#C2A56B", "#FBBF24", "#4A3A14"),
        new("Menta",      "#04140F", "#E6FFF5", "#7FC9AE", "#34D399", "#14463A"),
        new("Rosa",       "#1A0914", "#FFEAF5", "#C98AB0", "#F472B6", "#4A1E3A"),
        new("Rojo",       "#150606", "#FFECEC", "#C98A8A", "#EF4444", "#4A1C1C"),
        new("Cian",       "#041416", "#E6FEFF", "#7FBFC9", "#22D3EE", "#14434A"),
        new("Lima",       "#0E1404", "#F5FFE0", "#A7C27A", "#A3E635", "#34471A"),
        new("Naranja",    "#150A03", "#FFF1E5", "#C9A07F", "#FB923C", "#4A2D14"),
        new("Carbón",     "#18181B", "#FAFAFA", "#A1A1AA", "#E4E4E7", "#3F3F46"),
        new("Nieve",      "#F4F4F5", "#18181B", "#71717A", "#2563EB", "#D4D4D8"),
    };

    public static IslandTheme Find(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    /// <summary>Aplica el tema. Con animate=true los colores se mezclan suavemente.</summary>
    public static void Apply(IslandTheme t, bool animate = false)
    {
        var r = Application.Current.Resources;
        Set(r, "IslandBackground", t.Background, animate);
        Set(r, "IslandPrimary", t.Primary, animate);
        Set(r, "IslandSecondary", t.Secondary, animate);
        Set(r, "IslandAccent", t.Accent, animate);
        Set(r, "IslandTrack", t.Track, animate);
    }

    private static void Set(ResourceDictionary r, string key, string hex, bool animate)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        // Los pinceles de App.xaml no están congelados, así que se pueden animar en sitio.
        if (r[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            if (animate)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(380))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            else
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = color;
            }
            return;
        }
        var b = new SolidColorBrush(color);
        r[key] = b;
    }
}