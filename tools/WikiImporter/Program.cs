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

if (mode is "clean")
{
    CleanData(dataDirectory, options);
    return;
}

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

static void CleanData(string dataDirectory, JsonSerializerOptions writeOptions)
{
    var readOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    var enemiesPath = Path.Combine(dataDirectory, "enemies.json");
    var enemies = JsonSerializer.Deserialize<List<EnemyData>>(File.ReadAllText(enemiesPath), readOptions) ?? [];
    var cleanedEnemies = enemies.Select(CleanEnemy).ToList();
    File.WriteAllText(enemiesPath, JsonSerializer.Serialize(cleanedEnemies, writeOptions));

    var identitiesPath = Path.Combine(dataDirectory, "identities.json");
    var identities = JsonSerializer.Deserialize<List<IdentityData>>(File.ReadAllText(identitiesPath), readOptions) ?? [];
    var cleanedIdentities = identities.Select(CleanIdentity).ToList();
    File.WriteAllText(identitiesPath, JsonSerializer.Serialize(cleanedIdentities, writeOptions));

    Console.WriteLine($"Cleaned {cleanedEnemies.Count} enemies and {cleanedIdentities.Count} identities.");
}

static EnemyData CleanEnemy(EnemyData enemy) => enemy with
{
    Name = EnemyParser.CleanName(enemy.Name),
    DefenseLevel = EnemyParser.ClampDefense(enemy.DefenseLevel),
    Resistances = CleanResistances(enemy.Resistances),
    Skills = enemy.Skills.Select(CleanSkill).ToList(),
};

static IdentityData CleanIdentity(IdentityData identity) => identity with
{
    Name = EnemyParser.CleanName(identity.Name),
    Skills = identity.Skills.Select(CleanSkill).ToList(),
};

static SkillData CleanSkill(SkillData skill) => skill with { Name = EnemyParser.CleanName(skill.Name) };

static ResistanceSet CleanResistances(ResistanceSet resistances)
{
    var physical = new Dictionary<DamageType, double>();
    foreach (var type in new[] { DamageType.Slash, DamageType.Pierce, DamageType.Blunt })
    {
        if (resistances.Physical.TryGetValue(type, out var value))
        {
            physical[type] = value;
        }
    }
    var sins = resistances.Sin.ToDictionary(pair => pair.Key, pair => pair.Value);
    return new ResistanceSet(physical, sins);
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
