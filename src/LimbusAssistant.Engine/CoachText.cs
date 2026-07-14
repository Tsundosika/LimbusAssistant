namespace Tsundosika.LimbusAssistant.Engine;

public static class CoachText
{
    public static string SinColor(SinType sin, string locale = "en") => locale == "de"
        ? sin switch
        {
            SinType.Wrath => "rot",
            SinType.Lust => "orange",
            SinType.Sloth => "gelb",
            SinType.Gluttony => "grün",
            SinType.Gloom => "hellblau",
            SinType.Pride => "dunkelblau",
            SinType.Envy => "lila",
            _ => "grau",
        }
        : sin switch
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

    public static string Verdict(double winProbability, string locale = "en") => locale == "de"
        ? winProbability switch
        {
            >= 0.75 => "klarer Sieg",
            >= 0.60 => "gut für dich",
            >= 0.45 => "Münzwurf",
            _ => "lieber vermeiden",
        }
        : winProbability switch
        {
            >= 0.75 => "easy win",
            >= 0.60 => "favored",
            >= 0.45 => "coin flip",
            _ => "avoid if you can",
        };

    public static string VerdictIcon(double winProbability) => winProbability switch
    {
        >= 0.60 => "✅",
        >= 0.45 => "⚠️",
        _ => "❌",
    };

    public static string ShortInstruction(BestMoveAdvice move, string locale = "en")
    {
        var skill = $"{move.Sinner}: Skill {move.SkillNumber} ({SinColor(move.Sin, locale)})";
        if (move.IsUnopposed)
        {
            return locale == "de"
                ? $"{skill} → freier Treffer"
                : $"{skill} → free hit";
        }
        return $"{skill} → \"{move.TargetSkillName}\"";
    }

    public static string SkillLook(BestMoveAdvice move, string locale = "en")
    {
        var type = move.DamageType.ToString().ToLowerInvariant();
        return locale == "de"
            ? $"der {SinColor(move.Sin, locale)}e {move.Sin} {type} Skill mit {move.CoinCount} Coin{(move.CoinCount == 1 ? "" : "s")}"
            : $"the {SinColor(move.Sin, locale)} {move.Sin} {type}, {move.CoinCount} coin{(move.CoinCount == 1 ? "" : "s")}";
    }

    public static string? Why(BestMoveAdvice move, string locale = "en")
    {
        if (locale == "de")
        {
            if (move.SinMultiplier >= 2.0)
            {
                return $"Gegner nimmt doppelten Schaden von {move.Sin}";
            }
            if (move.PhysicalMultiplier >= 2.0)
            {
                return $"Gegner nimmt doppelten Schaden von {move.DamageType}";
            }
            if (move.SinMultiplier >= 1.5)
            {
                return $"Gegner ist schwach gegen {move.Sin}";
            }
            if (move.PhysicalMultiplier >= 1.5)
            {
                return $"Gegner ist schwach gegen {move.DamageType}";
            }
            if (move.SinMultiplier <= 0.5)
            {
                return $"Vorsicht, Gegner steckt {move.Sin} locker weg";
            }
            if (move.PhysicalMultiplier <= 0.75)
            {
                return $"Vorsicht, Gegner widersteht {move.DamageType}";
            }
            return null;
        }
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

    public static string Instruction(BestMoveAdvice move, bool plainLanguage, string locale = "en")
    {
        var skillPart = $"{move.Sinner}: Skill {move.SkillNumber} ({move.SkillName}, {SkillLook(move, locale)})";
        if (move.IsUnopposed)
        {
            if (locale == "de")
            {
                var schaden = plainLanguage ? "ein freier Treffer" : $"freier Treffer, ~{move.ExpectedDamageDealt:F0} Schaden";
                return $"{skillPart} auf einen offenen Gegner, {schaden}.";
            }
            var damage = plainLanguage ? "a free hit" : $"free hit, ~{move.ExpectedDamageDealt:F0} damage";
            return $"{skillPart} on any open enemy slot, {damage}.";
        }
        if (locale == "de")
        {
            var wertung = plainLanguage
                ? Verdict(move.WinProbability, locale)
                : $"{move.WinProbability:P0} Siegchance, ~{move.ExpectedDamageDealt:F0} Schaden";
            return $"Zieh {skillPart} auf \"{move.TargetSkillName}\", {wertung}.";
        }
        var odds = plainLanguage
            ? Verdict(move.WinProbability, locale)
            : $"{move.WinProbability:P0} win, deal ~{move.ExpectedDamageDealt:F0}";
        return $"Drag {skillPart} onto \"{move.TargetSkillName}\", {odds}.";
    }

    public static string? Fallback(BestMoveAdvice move, bool plainLanguage, string locale = "en")
    {
        if (move.Alternative is not { } alternative)
        {
            return null;
        }
        var odds = plainLanguage
            ? Verdict(alternative.WinProbability, locale)
            : locale == "de"
                ? $"{alternative.WinProbability:P0} Siegchance"
                : $"{alternative.WinProbability:P0} win";
        return locale == "de"
            ? $"Wenn Skill {move.SkillNumber} diese Runde nicht kam, nimm Skill {alternative.SkillNumber} ({alternative.SkillName}), {odds}."
            : $"If Skill {move.SkillNumber} was not dealt this turn, use Skill {alternative.SkillNumber} ({alternative.SkillName}), {odds}.";
    }

    public static string UnblockedWarning(UnblockedThreat threat, bool plainLanguage, string locale = "en")
    {
        if (locale == "de")
        {
            var schaden = plainLanguage ? "das tut weh" : $"~{threat.ExpectedDamage:F0} Schaden";
            var schutz = threat.SuggestedGuarder is null
                ? "halt durch"
                : $"drück auf {threat.SuggestedGuarder}s Portrait und wähl Defend";
            return $"Niemand blockt \"{threat.SkillName}\", {schaden}. Tipp: {schutz}.";
        }
        var damage = plainLanguage ? "it will hurt" : $"~{threat.ExpectedDamage:F0} damage";
        var guard = threat.SuggestedGuarder is null
            ? "brace for it"
            : $"press {threat.SuggestedGuarder}'s portrait and pick Defend";
        return $"Nobody blocks \"{threat.SkillName}\", {damage}. Tip: {guard}.";
    }
}
