using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tsundosika.LimbusAssistant.Vision;

public static class CalibrationStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LimbusAssistant",
        "calibration.json");

    public static CalibrationProfile Load(string? path = null)
    {
        var filePath = path ?? DefaultPath;
        if (!File.Exists(filePath))
        {
            return CalibrationProfile.Default;
        }
        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<CalibrationProfile>(stream, Options) ?? CalibrationProfile.Default;
        }
        catch (JsonException)
        {
            return CalibrationProfile.Default;
        }
    }

    public static void Save(CalibrationProfile profile, string? path = null)
    {
        var filePath = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(profile, Options));
    }
}
