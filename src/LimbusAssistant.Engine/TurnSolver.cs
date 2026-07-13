namespace Tsundosika.LimbusAssistant.Engine;

public sealed class TurnSolver
{
    const int MaxThreats = 12;

    static readonly SkillData NoSkill = new("", "", 0, 0, 0, SinType.Wrath, DamageType.Guard, 0);

    readonly ClashCalculator _calculator = new();

    public TurnPlan Solve(IReadOnlyList<TurnUnit> units, EnemyData enemy, int clashCount = 0) =>
        Solve(units, new[] { enemy }, clashCount);

    public TurnPlan Solve(IReadOnlyList<TurnUnit> units, IReadOnlyList<EnemyData> enemies, int clashCount = 0)
    {
        if (units.Count == 0 || enemies.Count == 0)
        {
            return new TurnPlan([], 0);
        }
        var threats = enemies
            .SelectMany(enemy => enemy.Skills.Select(skill => new EnemyThreat(enemy, skill, ClashCount: clashCount)))
            .Take(MaxThreats)
            .ToList();
        var averageDefense = (int)Math.Round(units.Average(unit => unit.Identity.DefenseLevel));
        var freeThreatDamage = threats
            .Select(threat => UnopposedThreatDamage(threat, averageDefense))
            .ToList();

        var clashOptions = new TurnAssignment[units.Count, threats.Count];
        var unopposedOptions = new TurnAssignment[units.Count];
        for (var u = 0; u < units.Count; u++)
        {
            for (var t = 0; t < threats.Count; t++)
            {
                clashOptions[u, t] = BestClash(units[u], threats[t]);
            }
            unopposedOptions[u] = BestUnopposed(units[u], enemies, clashCount);
        }

        var maskCount = 1 << threats.Count;
        var value = new double[units.Count + 1][];
        var previousMask = new int[units.Count + 1][];
        var choice = new int[units.Count + 1][];
        for (var u = 0; u <= units.Count; u++)
        {
            value[u] = Enumerable.Repeat(double.NegativeInfinity, maskCount).ToArray();
            previousMask[u] = new int[maskCount];
            choice[u] = new int[maskCount];
        }
        value[0][0] = 0;

        for (var u = 0; u < units.Count; u++)
        {
            for (var mask = 0; mask < maskCount; mask++)
            {
                if (double.IsNegativeInfinity(value[u][mask]))
                {
                    continue;
                }
                Relax(u + 1, mask, value[u][mask] + unopposedOptions[u].ExpectedValue, mask, -1);
                for (var t = 0; t < threats.Count; t++)
                {
                    if ((mask & (1 << t)) != 0)
                    {
                        continue;
                    }
                    Relax(u + 1, mask | (1 << t), value[u][mask] + clashOptions[u, t].ExpectedValue, mask, t);
                }
            }
        }

        var bestMask = 0;
        var bestTotal = double.NegativeInfinity;
        for (var mask = 0; mask < maskCount; mask++)
        {
            if (double.IsNegativeInfinity(value[units.Count][mask]))
            {
                continue;
            }
            var penalty = 0.0;
            for (var t = 0; t < threats.Count; t++)
            {
                if ((mask & (1 << t)) == 0)
                {
                    penalty += freeThreatDamage[t];
                }
            }
            var total = value[units.Count][mask] - penalty;
            if (total > bestTotal)
            {
                bestTotal = total;
                bestMask = mask;
            }
        }

        var assignments = new TurnAssignment[units.Count];
        var currentMask = bestMask;
        for (var u = units.Count; u > 0; u--)
        {
            var picked = choice[u][currentMask];
            assignments[u - 1] = picked < 0 ? unopposedOptions[u - 1] : clashOptions[u - 1, picked];
            currentMask = previousMask[u][currentMask];
        }
        return new TurnPlan(assignments, bestTotal);

        void Relax(int level, int mask, double total, int fromMask, int pickedThreat)
        {
            if (total > value[level][mask])
            {
                value[level][mask] = total;
                previousMask[level][mask] = fromMask;
                choice[level][mask] = pickedThreat;
            }
        }
    }

    TurnAssignment BestClash(TurnUnit unit, EnemyThreat threat) =>
        unit.Identity.Skills.Count == 0
            ? new TurnAssignment(unit, NoSkill, threat, 0, 0, 0, 0)
            : unit.Identity.Skills
                .Select(skill => EvaluateClash(unit, skill, threat))
                .MaxBy(assignment => assignment.ExpectedValue)!;

    public TurnAssignment EvaluateClash(TurnUnit unit, SkillData skill, EnemyThreat threat)
    {
        var allySkill = new ClashSkill(skill.BasePower, skill.CoinPower, skill.CoinCount, unit.Sanity);
        var enemySkill = new ClashSkill(threat.Skill.BasePower, threat.Skill.CoinPower, threat.Skill.CoinCount, threat.Sanity);
        var outcome = _calculator.Calculate(allySkill, enemySkill);
        var dealtContext = new DamageContext(
            threat.Enemy.Resistances.SinFor(skill.Sin),
            threat.Enemy.Resistances.PhysicalFor(skill.DamageType),
            threat.Stagger,
            skill.OffenseLevel,
            threat.Enemy.DefenseLevel,
            threat.ClashCount);
        var dealt = skill.DamageType is DamageType.Guard or DamageType.Evade
            ? 0
            : DamageCalculator.FinalDamage(
                ExpectedAttackPower.OnClashWin(allySkill, outcome.WinStates),
                dealtContext);
        var takenContext = new DamageContext(
            1.0,
            1.0,
            StaggerLevel.None,
            threat.Skill.OffenseLevel,
            unit.Identity.DefenseLevel,
            threat.ClashCount);
        var taken = threat.Skill.DamageType is DamageType.Guard or DamageType.Evade
            ? 0
            : DamageCalculator.FinalDamage(
                ExpectedAttackPower.OnClashWin(enemySkill, outcome.LoseStates),
                takenContext);
        return new TurnAssignment(
            unit,
            skill,
            threat,
            outcome.EffectiveWinProbability,
            dealt,
            taken,
            dealt - taken);
    }

    TurnAssignment BestUnopposed(TurnUnit unit, IReadOnlyList<EnemyData> enemies, int clashCount) =>
        unit.Identity.Skills.Count == 0 || enemies.Count == 0
            ? new TurnAssignment(unit, NoSkill, null, 1.0, 0, 0, 0)
            : unit.Identity.Skills
                .SelectMany(skill => enemies.Select(enemy => EvaluateUnopposed(unit, skill, enemy, clashCount)))
                .MaxBy(assignment => assignment.ExpectedValue)!;

    public TurnAssignment EvaluateUnopposed(TurnUnit unit, SkillData skill, EnemyData enemy, int clashCount = 0)
    {
        var allySkill = new ClashSkill(skill.BasePower, skill.CoinPower, skill.CoinCount, unit.Sanity);
        var context = new DamageContext(
            enemy.Resistances.SinFor(skill.Sin),
            enemy.Resistances.PhysicalFor(skill.DamageType),
            StaggerLevel.None,
            skill.OffenseLevel,
            enemy.DefenseLevel,
            clashCount);
        var dealt = skill.DamageType is DamageType.Guard or DamageType.Evade
            ? 0
            : DamageCalculator.FinalDamage(ExpectedAttackPower.Unopposed(allySkill), context);
        return new TurnAssignment(unit, skill, null, 1.0, dealt, 0, dealt);
    }

    double UnopposedThreatDamage(EnemyThreat threat, int defenseLevel)
    {
        var enemySkill = new ClashSkill(threat.Skill.BasePower, threat.Skill.CoinPower, threat.Skill.CoinCount, threat.Sanity);
        var context = new DamageContext(
            1.0,
            1.0,
            StaggerLevel.None,
            threat.Skill.OffenseLevel,
            defenseLevel,
            threat.ClashCount);
        return DamageCalculator.FinalDamage(ExpectedAttackPower.Unopposed(enemySkill), context);
    }
}
