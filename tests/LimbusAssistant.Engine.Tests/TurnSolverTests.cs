using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class TurnSolverTests
{
    static SkillData Skill(string id, int basePower, int coinPower, int coinCount, SinType sin = SinType.Wrath) =>
        new(id, id, basePower, coinPower, coinCount, sin, DamageType.Slash, 30);

    static IdentityData Identity(string id, params SkillData[] skills) =>
        new(id, id, id, 30, skills);

    static EnemyData Enemy(params SkillData[] skills) => new(
        "enemy",
        "Enemy",
        30,
        0,
        new ResistanceSet(new Dictionary<DamageType, double>(), new Dictionary<SinType, double>()),
        skills);

    [Fact]
    public void EveryUnitGetsExactlyOneAssignment()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit>
        {
            new(Identity("a", Skill("a1", 4, 7, 1)), 0),
            new(Identity("b", Skill("b1", 5, 4, 2)), 0),
            new(Identity("c", Skill("c1", 6, 2, 3)), 0),
        };
        var enemy = Enemy(Skill("e1", 4, 3, 1), Skill("e2", 5, 3, 2));
        var plan = solver.Solve(units, enemy);
        Assert.Equal(3, plan.Assignments.Count);
        var takenThreats = plan.Assignments
            .Where(assignment => assignment.Threat is not null)
            .Select(assignment => assignment.Threat!.Skill.Id)
            .ToList();
        Assert.Equal(takenThreats.Count, takenThreats.Distinct().Count());
    }

    [Fact]
    public void StrongUnitTakesTheDangerousClash()
    {
        var solver = new TurnSolver();
        var strong = new TurnUnit(Identity("strong", Skill("strong-skill", 10, 10, 2)), 45);
        var weak = new TurnUnit(Identity("weak", Skill("weak-skill", 2, 2, 1)), -20);
        var enemy = Enemy(Skill("dangerous", 8, 8, 2), Skill("mild", 1, 1, 1));
        var plan = solver.Solve([strong, weak], enemy);
        var strongAssignment = plan.Assignments.Single(assignment => assignment.Unit == strong);
        Assert.NotNull(strongAssignment.Threat);
        Assert.Equal("dangerous", strongAssignment.Threat!.Skill.Id);
    }

    [Fact]
    public void ExtraUnitsStrikeUnopposed()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit>
        {
            new(Identity("a", Skill("a1", 4, 7, 1)), 0),
            new(Identity("b", Skill("b1", 5, 4, 2)), 0),
        };
        var enemy = Enemy(Skill("only", 4, 3, 1));
        var plan = solver.Solve(units, enemy);
        Assert.Equal(1, plan.Assignments.Count(assignment => assignment.IsUnopposed));
        Assert.Equal(1, plan.Assignments.Count(assignment => !assignment.IsUnopposed));
    }

    [Fact]
    public void NoUnitsYieldsEmptyPlan()
    {
        var solver = new TurnSolver();
        var plan = solver.Solve([], Enemy(Skill("e1", 4, 3, 1)));
        Assert.Empty(plan.Assignments);
    }

    [Fact]
    public void EnemyWithoutSkillsMeansAllUnopposed()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit> { new(Identity("a", Skill("a1", 4, 7, 1)), 0) };
        var plan = solver.Solve(units, Enemy());
        Assert.All(plan.Assignments, assignment => Assert.True(assignment.IsUnopposed));
        Assert.True(plan.TotalExpectedValue > 0);
    }

    [Fact]
    public void ClashAssignmentPicksBestSkillOfUnit()
    {
        var solver = new TurnSolver();
        var identity = Identity("a", Skill("weak", 1, 1, 1), Skill("strong", 8, 8, 2));
        var unit = new TurnUnit(identity, 0);
        var enemy = Enemy(Skill("e1", 5, 4, 1));
        var plan = solver.Solve([unit], enemy);
        Assert.Equal("strong", plan.Assignments[0].Skill.Id);
    }
}
