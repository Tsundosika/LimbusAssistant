using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tsundosika.LimbusAssistant.Engine;

public sealed record GameData(
    IReadOnlyList<IdentityData> Identities,
    IReadOnlyList<EnemyData> Enemies)
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GameData Load(string directory)
    {
        var identities = LoadFile<List<IdentityData>>(Path.Combine(directory, "identities.json")) ?? [];
        var enemies = LoadFile<List<EnemyData>>(Path.Combine(directory, "enemies.json")) ?? [];
        return new GameData(identities, enemies);
    }

    static T? LoadFile<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }
}
