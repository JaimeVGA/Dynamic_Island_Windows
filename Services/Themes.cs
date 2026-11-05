using System.Windows;
using System.Windows.Media;

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
    };

    public static IslandTheme Find(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    public static void Apply(IslandTheme t)
    {
        var r = Application.Current.Resources;
        Set(r, "IslandBackground", t.Background);
        Set(r, "IslandPrimary", t.Primary);
        Set(r, "IslandSecondary", t.Secondary);
        Set(r, "IslandAccent", t.Accent);
        Set(r, "IslandTrack", t.Track);
    }

    private static void Set(ResourceDictionary r, string key, string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        r[key] = b;
    }
}
