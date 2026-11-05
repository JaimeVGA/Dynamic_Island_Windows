using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DynamicIsland.Services;

public enum IslandMode
{
    /// <summary>PÃ­ldora compacta con la hora (comportamiento original).</summary>
    Classic,
    /// <summary>Barra muy delgada arriba; crece al pasar el mouse.</summary>
    Bar,
    /// <summary>Barra ancha con carÃ¡tula, tÃ­tulo y hora.</summary>
    Wide
}

public sealed class Settings
{
    public string Theme { get; set; } = "Medianoche";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMode Mode { get; set; } = IslandMode.Classic;

    public bool Use24Hour { get; set; } = false;

    /// <summary>Si está activo, el modo cambia solo según haya o no música sonando.</summary>
    public bool AutoSwitch { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMode ModePlaying { get; set; } = IslandMode.Wide;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMode ModeIdle { get; set; } = IslandMode.Classic;

    /// <summary>Modo cuando hay una app maximizada en el monitor principal.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMode ModeMaximized { get; set; } = IslandMode.Bar;

    /// <summary>Prioridad: app maximizada > música sonando > escritorio.</summary>
    public IslandMode EffectiveMode(bool isPlaying, bool hasMaximized)
        => !AutoSwitch ? Mode
         : hasMaximized ? ModeMaximized
         : isPlaying ? ModePlaying
         : ModeIdle;

    /// <summary>Se dispara (en el hilo de UI) tras cambiar y guardar un ajuste.</summary>
    public event Action? Changed;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    /// <summary>Guarda y avisa a quien escuche.</summary>
    public void Commit()
    {
        Save();
        Changed?.Invoke();
    }
}

public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe != null) k.SetValue(ValueName, $"\"{exe}\"");
        }
        else k.DeleteValue(ValueName, false);
    }
}

