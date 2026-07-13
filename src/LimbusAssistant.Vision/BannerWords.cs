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
        if (NonSkillWords.Contains(normalized)
            || normalized.StartsWith("skilleffect", StringComparison.Ordinal)
            || normalized.EndsWith("rds", StringComparison.Ordinal) && normalized.Length <= 5)
        {
            return true;
        }
        return normalized.Length >= 6 && NonSkillWords.Any(word =>
            word.Length >= 6
            && Math.Abs(word.Length - normalized.Length) <= 1
            && WithinOneEdit(normalized, word));
    }

    static bool WithinOneEdit(string a, string b)
    {
        if (a == b)
        {
            return true;
        }
        if (a.Length == b.Length)
        {
            var mismatches = 0;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i] && ++mismatches > 1)
                {
                    return false;
                }
            }
            return true;
        }
        var (shorter, longer) = a.Length < b.Length ? (a, b) : (b, a);
        if (longer.Length - shorter.Length != 1)
        {
            return false;
        }
        var shortIndex = 0;
        var skipped = false;
        for (var longIndex = 0; longIndex < longer.Length; longIndex++)
        {
            if (shortIndex < shorter.Length && shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
            }
            else if (skipped)
            {
                return false;
            }
            else
            {
                skipped = true;
            }
        }
        return true;
    }
}
