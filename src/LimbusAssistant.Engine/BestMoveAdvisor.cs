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
        var moves = plan.Assignments.Select(ToAdvice).ToList();
        var blocked = plan.Assignments
            .Where(assignment => assignment.Threat is not null)
            .Select(assignment => assignment.Threat!.Skill.Id)
            .ToHashSet();
        var unblocked = enemies
            .SelectMany(enemy => enemy.Skills
                .Where(IsAttack)
                .Where(skill => !blocked.Contains(skill.Id))
                .Select(skill => new UnblockedThreat(
                    enemy.Name,
                    skill.Name,
                    solver.UnblockedThreatDamage(skill, enemy, units, clashCount))))
            .OrderByDescending(threat => threat.ExpectedDamage)
            .ToList();
        return new BestMoveReport(moves, unblocked, plan.TotalExpectedValue);
    }

    static BestMoveAdvice ToAdvice(TurnAssignment assignment)
    {
        return new BestMoveAdvice(
            assignment.Unit.Identity.Sinner,
            assignment.Unit.Identity.Name,
            SkillNumber(assignment.Unit.Identity.Skills, assignment.Skill),
            assignment.Skill.Name,
            assignment.IsUnopposed,
            assignment.Threat?.Enemy.Name,
            assignment.Threat?.Skill.Name,
            assignment.WinProbability,
            assignment.ExpectedDamageDealt,
            assignment.ExpectedDamageTaken);
    }

    static int SkillNumber(IReadOnlyList<SkillData> skills, SkillData skill)
    {
        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].Id == skill.Id)
            {
                return i + 1;
            }
        }
        return 0;
    }

    static bool IsAttack(SkillData skill) => skill.DamageType is not (DamageType.Guard or DamageType.Evade);
}
