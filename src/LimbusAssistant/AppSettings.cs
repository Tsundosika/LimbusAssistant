using System.IO;
using System.Text.Json;

namespace Tsundosika.LimbusAssistant;

public sealed record AppSettings
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string WindowTitle { get; init; } = "";

    public string ToggleOverlayHotkey { get; init; } = "Ctrl+F8";

    public string ToggleDebugHotkey { get; init; } = "Ctrl+F9";

    public int CaptureIntervalMilliseconds { get; init; } = 100;

    public double MinimumConfidence { get; init; } = 0.5;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LimbusAssistant",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(this, Options));
    }
}
