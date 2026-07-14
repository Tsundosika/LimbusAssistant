namespace Tsundosika.LimbusAssistant.Engine;

public static class CoachText
{
    public static string SinColor(SinType sin) => sin switch
    {
        SinType.Wrath => "red",
        SinType.Lust => "orange",
        SinType.Sloth => "yellow",
        SinType.Gluttony => "green",
        SinType.Gloom => "light blue",
        SinType.Pride => "dark blue",
        SinType.Envy => "purple",
        _ => "gray",
    };

    public static string Verdict(double winProbability) => winProbability switch
    {
        >= 0.75 => "easy win",
        >= 0.60 => "favored",
        >= 0.45 => "coin flip",
        _ => "avoid if you can",
    };

    public static string SkillLook(BestMoveAdvice move) =>
        $"the {SinColor(move.Sin)} {move.Sin} {move.DamageType.ToString().ToLowerInvariant()}, " +
        $"{move.CoinCount} coin{(move.CoinCount == 1 ? "" : "s")}";

    public static string? Why(BestMoveAdvice move)
    {
        if (move.SinMultiplier >= 2.0)
        {
            return $"enemy takes double damage from {move.Sin}";
        }
        if (move.PhysicalMultiplier >= 2.0)
        {
            return $"enemy takes double damage from {move.DamageType}";
        }
        if (move.SinMultiplier >= 1.5)
        {
            return $"enemy is weak to {move.Sin}";
        }
        if (move.PhysicalMultiplier >= 1.5)
        {
            return $"enemy is weak to {move.DamageType}";
        }
        if (move.SinMultiplier <= 0.5)
        {
            return $"careful, enemy shrugs off {move.Sin}";
        }
        if (move.PhysicalMultiplier <= 0.75)
        {
            return $"careful, enemy resists {move.DamageType}";
        }
        return null;
    }

    public static string Instruction(BestMoveAdvice move, bool plainLanguage)
    {
        var skillPart = $"{move.Sinner}: Skill {move.SkillNumber} ({move.SkillName}, {SkillLook(move)})";
        if (move.IsUnopposed)
        {
            var damage = plainLanguage ? "a free hit" : $"free hit, ~{move.ExpectedDamageDealt:F0} damage";
            return $"{skillPart} on any open enemy slot, {damage}.";
        }
        var odds = plainLanguage
            ? Verdict(move.WinProbability)
            : $"{move.WinProbability:P0} win, deal ~{move.ExpectedDamageDealt:F0}";
        return $"Drag {skillPart} onto \"{move.TargetSkillName}\", {odds}.";
    }

    public static string? Fallback(BestMoveAdvice move, bool plainLanguage)
    {
        if (move.Alternative is not { } alternative)
        {
            return null;
        }
        var odds = plainLanguage
            ? Verdict(alternative.WinProbability)
            : $"{alternative.WinProbability:P0} win";
        return $"If Skill {move.SkillNumber} was not dealt this turn, use Skill {alternative.SkillNumber} ({alternative.SkillName}), {odds}.";
    }

    public static string UnblockedWarning(UnblockedThreat threat, bool plainLanguage)
    {
        var damage = plainLanguage ? "it will hurt" : $"~{threat.ExpectedDamage:F0} damage";
        var guard = threat.SuggestedGuarder is null
            ? "brace for it"
            : $"consider Skill {threat.GuardSkillNumber} ({threat.GuardSkillName}) on {threat.SuggestedGuarder} to soften it";
        return $"Nobody blocks \"{threat.SkillName}\", {damage}. {guard}.";
    }
}
