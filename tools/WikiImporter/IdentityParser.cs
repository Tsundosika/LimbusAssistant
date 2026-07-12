using System.Globalization;
using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.WikiImporter;

public static class IdentityParser
{
    const int BaselineLevel = 35;

    static readonly string[] SkillTemplates = ["UptieSkills", "Skill"];

    public static IdentityData? Parse(string pageTitle, string wikitext)
    {
        var page = WikitextTemplate.ExtractAll(wikitext, "IDPage").FirstOrDefault();
        if (page is null)
        {
            return null;
        }
        var sinner = page.Value("sinner");
        var prefix = page.Value("prefix");
        var name = string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(sinner)
            ? pageTitle
            : $"{prefix} {sinner}";
        var defense = BaselineLevel + ParseSignedInt(page.Value("defmod"));
        var skills = new List<SkillData>();
        var skillSources = Enumerable.Range(1, 5).Select(i => page.Value($"skill{i}")).Append(page.Value("defense"));
        foreach (var value in skillSources)
        {
            if (value.Length == 0)
            {
                continue;
            }
            foreach (var template in SkillTemplates.SelectMany(t => WikitextTemplate.ExtractAll(value, t)))
            {
                var skill = EnemyParser.ParseSkill(template);
                if (skill is not null)
                {
                    skills.Add(skill);
                }
            }
        }
        if (skills.Count == 0)
        {
            return null;
        }
        return new IdentityData(
            "",
            name,
            string.IsNullOrWhiteSpace(sinner) ? pageTitle : sinner,
            defense,
            skills);
    }

    static int ParseSignedInt(string text)
    {
        var compact = text.Replace(" ", "");
        return int.TryParse(compact, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
