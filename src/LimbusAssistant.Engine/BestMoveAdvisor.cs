namespace Tsundosika.LimbusAssistant.Engine;

public static class BestMoveAdvisor
{
    public static BestMoveReport Advise(
        TurnSolver solver,
        IReadOnlyList<TurnUnit> units,
        IReadOnlyList<EnemyData> enemies,
        int clashCount = 0)
    {
        var plan = solver.Solve(units, enemies, clashCount);
        var moves = plan.Assignments
            .Select(assignment => ToAdvice(solver, assignment, enemies, clashCount))
            .ToList();
        var blocked = plan.Assignments
            .Where(assignment => assignment.Threat is not null)
            .Select(assignment => assignment.Threat!.Skill.Id)
            .ToHashSet();
        var guard = FindGuarder(plan.Assignments);
        var unblocked = enemies
            .SelectMany(enemy => enemy.Skills
                .Where(IsAttack)
                .Where(skill => !blocked.Contains(skill.Id))
                .Select(skill => new UnblockedThreat(
                    enemy.Name,
                    skill.Name,
                    solver.UnblockedThreatDamage(skill, enemy, units, clashCount),
                    guard?.Sinner,
                    guard?.SkillName)))
            .OrderByDescending(threat => threat.ExpectedDamage)
            .ToList();
        return new BestMoveReport(moves, unblocked, plan.TotalExpectedValue);
    }

    static BestMoveAdvice ToAdvice(
        TurnSolver solver,
        TurnAssignment assignment,
        IReadOnlyList<EnemyData> enemies,
        int clashCount)
    {
        var skills = assignment.Unit.Identity.Skills;
        var sinMultiplier = 1.0;
        var physicalMultiplier = 1.0;
        if (assignment.Threat is { } threat)
        {
            sinMultiplier = threat.Enemy.Resistances.SinFor(assignment.Skill.Sin);
            physicalMultiplier = threat.Enemy.Resistances.PhysicalFor(assignment.Skill.DamageType);
        }
        return new BestMoveAdvice(
            assignment.Unit.Identity.Sinner,
            assignment.Unit.Identity.Name,
            SkillNumber(skills, assignment.Skill),
            assignment.Skill.Name,
            assignment.IsUnopposed,
            assignment.Threat?.Enemy.Name,
            assignment.Threat?.Skill.Name,
            assignment.WinProbability,
            assignment.ExpectedDamageDealt,
            assignment.ExpectedDamageTaken,
            assignment.Skill.Sin,
            assignment.Skill.DamageType,
            assignment.Skill.CoinCount,
            sinMultiplier,
            physicalMultiplier,
            FindAlternative(solver, assignment, enemies, clashCount));
    }

    static AlternativeMove? FindAlternative(
        TurnSolver solver,
        TurnAssignment assignment,
        IReadOnlyList<EnemyData> enemies,
        int clashCount)
    {
        var unit = assignment.Unit;
        var others = unit.Identity.Skills
            .Where(skill => skill.Id != assignment.Skill.Id)
            .Where(IsAttack)
            .ToList();
        if (others.Count == 0)
        {
            return null;
        }
        TurnAssignment? best = null;
        foreach (var skill in others)
        {
            var candidate = assignment.Threat is { } threat
                ? solver.EvaluateClash(unit, skill, threat)
                : BestUnopposedFor(solver, unit, skill, enemies, clashCount);
            if (candidate is not null && (best is null || candidate.ExpectedValue > best.ExpectedValue))
            {
                best = candidate;
            }
        }
        if (best is null)
        {
            return null;
        }
        return new AlternativeMove(
            SkillNumber(unit.Identity.Skills, best.Skill),
            best.Skill.Name,
            best.WinProbability,
            best.ExpectedDamageDealt);
    }

    static TurnAssignment? BestUnopposedFor(
        TurnSolver solver,
        TurnUnit unit,
        SkillData skill,
        IReadOnlyList<EnemyData> enemies,
        int clashCount)
    {
        TurnAssignment? best = null;
        foreach (var enemy in enemies)
        {
            var candidate = solver.EvaluateUnopposed(unit, skill, enemy, clashCount);
            if (best is null || candidate.ExpectedValue > best.ExpectedValue)
            {
                best = candidate;
            }
        }
        return best;
    }

    static (string Sinner, string SkillName)? FindGuarder(IReadOnlyList<TurnAssignment> assignments)
    {
        (string, string)? guard = null;
        var lowestValue = double.MaxValue;
        foreach (var assignment in assignments)
        {
            var skills = assignment.Unit.Identity.Skills;
            var guardSkill = skills.FirstOrDefault(skill => skill.DamageType is DamageType.Guard or DamageType.Evade);
            if (guardSkill is null || assignment.ExpectedValue >= lowestValue)
            {
                continue;
            }
            lowestValue = assignment.ExpectedValue;
            guard = (assignment.Unit.Identity.Sinner, guardSkill.Name);
        }
        return guard;
    }

    static int SkillNumber(IReadOnlyList<SkillData> skills, SkillData skill)
    {
        var number = 0;
        foreach (var candidate in skills)
        {
            if (IsAttack(candidate))
            {
                number++;
            }
            if (candidate.Id == skill.Id)
            {
                return IsAttack(candidate) ? number : 0;
            }
        }
        return 0;
    }

    static bool IsAttack(SkillData skill) => skill.DamageType is not (DamageType.Guard or DamageType.Evade);
}
