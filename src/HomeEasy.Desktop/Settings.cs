using System.IO;
using System.Text.Json;

namespace HomeEasy.Desktop;

public sealed class Settings
{
    public string Host { get; set; } = "";
    public string Mac { get; set; } = "";
    public bool CloseToTray { get; set; } = true;
    /// <summary>When the off timer started from this app should fire (UTC); null when none.</summary>
    public DateTime? TimerEndsUtc { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HomeEasyLocal", "settings.json");

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
