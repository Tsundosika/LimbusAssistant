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

    public string DebugDumpHotkey { get; init; } = "Ctrl+F10";

    public string CoachAdvanceHotkey { get; init; } = "Ctrl+F11";

    public int CaptureIntervalMilliseconds { get; init; } = 50;

    public double MinimumConfidence { get; init; } = 0.5;

    public IReadOnlyList<TeamMemberSetting> Team { get; init; } = [];

    public bool PlainLanguage { get; init; } = true;

    public bool ShowDetails { get; init; }

    public bool ShowChecklist { get; init; } = true;

    public string Language { get; init; } = "en";

    public bool SoundCues { get; init; } = true;

    public double CoachFontScale { get; init; } = 1.0;

    public string CoachPanelPosition { get; init; } = "left";

    public bool ShownCoachIntro { get; init; }

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
