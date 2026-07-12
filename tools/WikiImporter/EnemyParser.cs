using System.Globalization;
using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.WikiImporter;

public static class EnemyParser
{
    const int BaselineLevel = 35;

    static readonly string[] HumanTemplates = ["ENPage", "ENInfo", "EnemiesInfo"];
    static readonly string[] AbnormalityTemplates = ["ABPage", "ABInfo", "AbnormalitiesInfo"];
    static readonly string[] PartsTemplates = ["ABPage/Parts", "AbnormalitiesParts"];

    public static List<EnemyData> Parse(string pageTitle, string wikitext)
    {
        var enemies = new List<EnemyData>();
        foreach (var template in HumanTemplates)
        {
            foreach (var block in WikitextTemplate.ExtractAll(wikitext, template))
            {
                var enemy = FromHumanEnemy(pageTitle, block);
                if (enemy is not null)
                {
                    enemies.Add(enemy);
                }
            }
        }
        foreach (var template in AbnormalityTemplates)
        {
            foreach (var block in WikitextTemplate.ExtractAll(wikitext, template))
            {
                var enemy = FromAbnormality(pageTitle, block);
                if (enemy is not null)
                {
                    enemies.Add(enemy);
                }
            }
        }
        return enemies;
    }

    static EnemyData? FromHumanEnemy(string pageTitle, WikitextTemplate block)
    {
        var skills = ParseSkills(block);
        if (skills.Count == 0)
        {
            return null;
        }
        var name = FirstNonEmpty(block.Value("name"), PageBaseName(pageTitle));
        var defense = BaselineLevel + ParseSignedInt(block.Value("defmod"));
        var stagger = ParseInt(block.Value("stagger1")) ?? 0;
        return new EnemyData("", name, defense, stagger, ParseResistances(block), skills);
    }

    static EnemyData? FromAbnormality(string pageTitle, WikitextTemplate block)
    {
        var skills = ParseSkills(block);
        if (skills.Count == 0)
        {
            return null;
        }
        var name = FirstNonEmpty(block.Value("name"), PageBaseName(pageTitle));
        var mainPart = Enumerable.Range(1, 8)
            .Select(i => block.Value($"abnoparts{i}"))
            .Where(value => value.Length > 0)
            .SelectMany(value => PartsTemplates.SelectMany(template => WikitextTemplate.ExtractAll(value, template)))
            .FirstOrDefault();
        var defense = BaselineLevel;
        var stagger = 0;
        var resistances = EmptyResistances;
        if (mainPart is not null)
        {
            defense = ParseInt(mainPart.Value("defense"))
                ?? BaselineLevel + ParseSignedInt(mainPart.Value("defmod"));
            stagger = ParseInt(mainPart.Value("stagger1")) ?? 0;
            resistances = ParseResistances(mainPart);
        }
        return new EnemyData("", name, defense, stagger, resistances, skills);
    }

    static List<SkillData> ParseSkills(WikitextTemplate block)
    {
        var skills = new List<SkillData>();
        for (var i = 1; i <= 12; i++)
        {
            var value = block.Value($"skill{i}");
            if (value.Length == 0)
            {
                continue;
            }
            foreach (var template in WikitextTemplate.ExtractAll(value, "Skill"))
            {
                var skill = ParseSkill(template);
                if (skill is not null)
                {
                    skills.Add(skill);
                }
            }
        }
        return skills;
    }

    public static SkillData? ParseSkill(WikitextTemplate template)
    {
        if (!Enum.TryParse<DamageType>(template.Value("type"), true, out var damageType))
        {
            return null;
        }
        if (!Enum.TryParse<SinType>(template.Value("sin"), true, out var sin))
        {
            if (damageType is not (DamageType.Guard or DamageType.Evade))
            {
                return null;
            }
            sin = SinType.Wrath;
        }
        var basePower = ParseInt(template.Value("spower"));
        var coinPower = ParseSignedIntOrNull(template.Value("cpower"));
        if (basePower is null || coinPower is null)
        {
            return null;
        }
        var coins = ParseInt(template.Value("coin")) ?? 1;
        var offense = (ParseInt(template.Value("baseatk")) ?? BaselineLevel)
            + ParseSignedInt(template.Value("atkmod"));
        var name = FirstNonEmpty(template.Value("name"), "Unnamed Skill");
        return new SkillData("", name, basePower.Value, coinPower.Value, coins, sin, damageType, offense);
    }

    static ResistanceSet EmptyResistances => new(
        new Dictionary<DamageType, double>(),
        new Dictionary<SinType, double>());

    static ResistanceSet ParseResistances(WikitextTemplate block)
    {
        var physical = new Dictionary<DamageType, double>();
        foreach (var damageType in Enum.GetValues<DamageType>())
        {
            var value = ParseDouble(block.Value(damageType.ToString().ToLowerInvariant()));
            if (value is not null)
            {
                physical[damageType] = value.Value;
            }
        }
        var sins = new Dictionary<SinType, double>();
        foreach (var sin in Enum.GetValues<SinType>())
        {
            var value = ParseDouble(block.Value(sin.ToString().ToLowerInvariant()));
            if (value is not null)
            {
                sins[sin] = value.Value;
            }
        }
        return new ResistanceSet(physical, sins);
    }

    static string PageBaseName(string pageTitle) => pageTitle.Split('/')[0];

    static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    static int? ParseInt(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    static int ParseSignedInt(string text) => ParseSignedIntOrNull(text) ?? 0;

    static int? ParseSignedIntOrNull(string text)
    {
        var compact = text.Replace(" ", "");
        return int.TryParse(compact, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    static double? ParseDouble(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
