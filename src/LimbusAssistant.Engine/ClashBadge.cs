namespace Tsundosika.LimbusAssistant.Engine;

public static class ClashBadge
{
    public const double BetterTargetMargin = 0.08;

    public static (string Icon, string Word) Verdict(double winProbability, string locale = "en")
    {
        if (locale == "de")
        {
            return winProbability switch
            {
                >= 0.75 => ("✅", "NIMM ES"),
                >= 0.60 => ("✅", "GUT"),
                >= 0.45 => ("⚠️", "MÜNZWURF"),
                _ => ("❌", "SUCH WAS BESSERES"),
            };
        }
        return winProbability switch
        {
            >= 0.75 => ("✅", "TAKE IT"),
            >= 0.60 => ("✅", "GOOD"),
            >= 0.45 => ("⚠️", "COIN FLIP"),
            _ => ("❌", "FIND BETTER"),
        };
    }

    public static (string Icon, string Word) EnemyAttack(string locale = "en") =>
        locale == "de" ? ("🛡️", "GEGNER-ANGRIFF") : ("🛡️", "THEIR ATTACK");

    public static string? BetterTargetHint(
        double currentWinProbability,
        double bestWinProbability,
        string bestTargetName,
        string locale = "en")
    {
        if (bestWinProbability - currentWinProbability < BetterTargetMargin)
        {
            return null;
        }
        var icon = Verdict(bestWinProbability, locale).Icon;
        return locale == "de"
            ? $"Besser: zieh es auf \"{bestTargetName}\" {icon}"
            : $"Better: put it on \"{bestTargetName}\" {icon}";
    }

    public static string MatchesPlan(string locale = "en") =>
        locale == "de" ? "Passt zum Plan ✓" : "Matches the plan ✓";

    public static string AnswerWith(string answerLabel, string locale = "en") =>
        locale == "de"
            ? $"Antwort: {answerLabel}"
            : $"Answer with {answerLabel}";
}
