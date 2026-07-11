namespace Tsundosika.LimbusAssistant.Engine;

public sealed class Recommender
{
    readonly ClashCalculator _calculator = new();

    public IReadOnlyList<ClashSuggestion> Rank(
        IEnumerable<ClashCandidate> allies,
        IEnumerable<EnemyThreat> threats,
        int topCount = 6)
    {
        var threatList = threats.ToList();
        var suggestions = new List<ClashSuggestion>();
        foreach (var ally in allies)
        {
            foreach (var threat in threatList)
            {
                suggestions.Add(Evaluate(ally, threat));
            }
        }
        return suggestions
            .OrderByDescending(suggestion => suggestion.Score)
            .Take(topCount)
            .ToList();
    }

    public ClashSuggestion Evaluate(ClashCandidate ally, EnemyThreat threat)
    {
        var allySkill = ToClashSkill(ally.Skill, ally.Sanity);
        var enemySkill = ToClashSkill(threat.Skill, threat.Sanity);
        var outcome = _calculator.Calculate(allySkill, enemySkill);
        var rawPower = ExpectedAttackPower.OnClashWin(allySkill, outcome.WinStates);
        var context = new DamageContext(
            threat.Enemy.Resistances.SinFor(ally.Skill.Sin),
            threat.Enemy.Resistances.PhysicalFor(ally.Skill.DamageType),
            threat.Stagger,
            ally.Skill.OffenseLevel,
            threat.Enemy.DefenseLevel,
            threat.ClashCount);
        var expectedDamage = DamageCalculator.FinalDamage(rawPower, context);
        var score = outcome.EffectiveWinProbability * expectedDamage;
        return new ClashSuggestion(ally, threat, outcome.EffectiveWinProbability, expectedDamage, score);
    }

    static ClashSkill ToClashSkill(SkillData skill, int sanity) =>
        new(skill.BasePower, skill.CoinPower, skill.CoinCount, sanity);
}
