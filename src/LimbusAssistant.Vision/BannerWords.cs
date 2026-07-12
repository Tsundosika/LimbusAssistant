namespace Tsundosika.LimbusAssistant.Vision;

public static class BannerWords
{
    static readonly HashSet<string> NonSkillWords = new(StringComparer.Ordinal)
    {
        "keywords",
        "keyword",
        "overwhelmed",
        "staggered",
        "stagger",
        "panic",
        "agitated",
        "frenzied",
        "terrified",
        "engrossed",
        "dazed",
        "confused",
        "unyielding",
        "vengeful",
        "anguish",
        "angst",
        "atrophied",
        "alaya",
    };

    public static bool IsNonSkillBanner(string text)
    {
        var normalized = new string(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
        if (normalized.Length == 0)
        {
            return false;
        }
        return NonSkillWords.Contains(normalized)
            || normalized.StartsWith("skilleffect", StringComparison.Ordinal)
            || normalized.EndsWith("rds", StringComparison.Ordinal) && normalized.Length <= 5;
    }
}
