using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.WikiImporter;

var mode = args.Length > 0 ? args[0] : "all";
var dataDirectory = args.Length > 1 ? args[1] : Path.Combine("src", "LimbusAssistant", "Data");

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};

using var client = new WikiClient();

if (mode is "all" or "enemies")
{
    await ImportEnemiesAsync();
}
if (mode is "all" or "identities")
{
    await ImportIdentitiesAsync();
}

var reloaded = GameData.Load(Path.GetFullPath(dataDirectory));
Console.WriteLine($"Validation reload: {reloaded.Enemies.Count} enemies, {reloaded.Identities.Count} identities.");
return;

async Task ImportEnemiesAsync()
{
    Console.WriteLine("Listing Category:Enemy…");
    var titles = await client.ListCategoryMembersAsync("Category:Enemy");
    Console.WriteLine($"{titles.Count} enemy pages found.");
    var pages = await client.FetchPagesAsync(titles, (done, total) => Console.WriteLine($"fetched {done}/{total}"));

    var enemies = new List<EnemyData>();
    var unparsed = new List<string>();
    foreach (var (title, wikitext) in pages.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
    {
        var parsed = EnemyParser.Parse(title, wikitext);
        if (parsed.Count == 0)
        {
            unparsed.Add(title);
            continue;
        }
        enemies.AddRange(parsed);
    }

    var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var finalEnemies = new List<EnemyData>();
    foreach (var enemy in enemies.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
    {
        var (name, id) = UniqueNameAndId(enemy.Name, seenNames, seenIds);
        var skills = enemy.Skills
            .Select((skill, index) => skill with { Id = $"{id}-skill-{index + 1}" })
            .ToList();
        finalEnemies.Add(enemy with { Id = id, Name = name, Skills = skills });
    }

    var outputPath = Path.Combine(dataDirectory, "enemies.json");
    File.WriteAllText(outputPath, JsonSerializer.Serialize(finalEnemies, options));
    Console.WriteLine($"Wrote {finalEnemies.Count} enemies ({finalEnemies.Sum(e => e.Skills.Count)} skills) to {outputPath}");
    Console.WriteLine($"Enemy pages without parseable data: {unparsed.Count}");
}

async Task ImportIdentitiesAsync()
{
    Console.WriteLine("Listing Category:Identities…");
    var titles = await client.ListCategoryMembersAsync("Category:Identities");
    Console.WriteLine($"{titles.Count} identity pages found.");
    var pages = await client.FetchPagesAsync(titles, (done, total) => Console.WriteLine($"fetched {done}/{total}"));

    var identities = new List<IdentityData>();
    var unparsed = new List<string>();
    foreach (var (title, wikitext) in pages.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
    {
        var parsed = IdentityParser.Parse(title, wikitext);
        if (parsed is null)
        {
            unparsed.Add(title);
            continue;
        }
        identities.Add(parsed);
    }

    var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var finalIdentities = new List<IdentityData>();
    foreach (var identity in identities.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
    {
        var (name, id) = UniqueNameAndId(identity.Name, seenNames, seenIds);
        var skills = identity.Skills
            .Select((skill, index) => skill with { Id = $"{id}-skill-{index + 1}" })
            .ToList();
        finalIdentities.Add(identity with { Id = id, Name = name, Skills = skills });
    }

    var outputPath = Path.Combine(dataDirectory, "identities.json");
    File.WriteAllText(outputPath, JsonSerializer.Serialize(finalIdentities, options));
    Console.WriteLine($"Wrote {finalIdentities.Count} identities ({finalIdentities.Sum(i => i.Skills.Count)} skills) to {outputPath}");
    Console.WriteLine($"Identity pages without parseable data: {unparsed.Count}");
    foreach (var title in unparsed)
    {
        Console.WriteLine($"  skipped: {title}");
    }
}

static (string Name, string Id) UniqueNameAndId(
    string rawName,
    Dictionary<string, int> seenNames,
    HashSet<string> seenIds)
{
    var count = seenNames.GetValueOrDefault(rawName);
    seenNames[rawName] = count + 1;
    var name = count == 0 ? rawName : $"{rawName} ({count + 1})";
    var id = Slugify(name);
    while (!seenIds.Add(id))
    {
        id += "-x";
    }
    return (name, id);
}

static string Slugify(string name)
{
    var builder = new StringBuilder();
    var lastDash = true;
    foreach (var character in name.ToLowerInvariant())
    {
        if (char.IsAsciiLetterOrDigit(character))
        {
            builder.Append(character);
            lastDash = false;
        }
        else if (!lastDash)
        {
            builder.Append('-');
            lastDash = true;
        }
    }
    return builder.ToString().Trim('-');
}
