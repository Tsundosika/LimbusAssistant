using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.WikiImporter;

var outputPath = args.Length > 0 ? args[0] : Path.Combine("src", "LimbusAssistant", "Data", "enemies.json");

using var client = new WikiClient();
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
    var count = seenNames.GetValueOrDefault(enemy.Name);
    seenNames[enemy.Name] = count + 1;
    var name = count == 0 ? enemy.Name : $"{enemy.Name} ({count + 1})";
    var id = Slugify(name);
    while (!seenIds.Add(id))
    {
        id += "-x";
    }
    var skills = enemy.Skills
        .Select((skill, index) => skill with { Id = $"{id}-skill-{index + 1}" })
        .ToList();
    finalEnemies.Add(enemy with { Id = id, Name = name, Skills = skills });
}

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};
File.WriteAllText(outputPath, JsonSerializer.Serialize(finalEnemies, options));

Console.WriteLine($"Wrote {finalEnemies.Count} enemies ({finalEnemies.Sum(e => e.Skills.Count)} skills) to {outputPath}");
Console.WriteLine($"Pages without parseable enemy data: {unparsed.Count}");
foreach (var title in unparsed)
{
    Console.WriteLine($"  skipped: {title}");
}

var reloaded = GameData.Load(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
Console.WriteLine($"Validation reload: {reloaded.Enemies.Count} enemies, {reloaded.Identities.Count} identities.");

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
