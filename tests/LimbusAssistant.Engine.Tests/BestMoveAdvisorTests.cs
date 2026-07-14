using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class BestMoveAdvisorTests
{
    static SkillData Skill(string id, int basePower, int coinPower, int coinCount, DamageType type = DamageType.Slash) =>
        new(id, id, basePower, coinPower, coinCount, SinType.Wrath, type, 30);

    static IdentityData Identity(string id, string sinner, params SkillData[] skills) =>
        new(id, id, sinner, 30, skills);

    static EnemyData Enemy(string id, string name, params SkillData[] skills) =>
        new(id, name, 30, 0, new ResistanceSet(new Dictionary<DamageType, double>(), new Dictionary<SinType, double>()), skills);

    [Fact]
    public void AdvisesEveryUnitAndNumbersTheChosenSkill()
    {
        var solver = new TurnSolver();
        var don = Identity("don", "Don Quixote", Skill("d1", 3, 3, 1), Skill("d2", 8, 8, 2), Skill("d3", 5, 5, 2));
        var units = new List<TurnUnit> { new(don, 30) };
        var enemy = Enemy("e", "Dummy", Skill("e1", 4, 3, 1));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        var move = Assert.Single(report.Moves);
        Assert.Equal("Don Quixote", move.Sinner);
        Assert.Equal(2, move.SkillNumber);
        Assert.Equal("d2", move.SkillName);
    }

    [Fact]
    public void ReportsUnblockedThreatsWithDamage()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit> { new(Identity("a", "A", Skill("a1", 5, 4, 2)), 0) };
        var enemy = Enemy("e", "Enemy", Skill("e1", 6, 5, 2), Skill("e2", 6, 5, 2), Skill("e3", 6, 5, 2));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        Assert.True(report.Unblocked.Count >= 2);
        Assert.All(report.Unblocked, threat => Assert.True(threat.ExpectedDamage > 0));
    }

    [Fact]
    public void NoUnblockedWhenStrongTeamBlocksEveryDangerousThreat()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit>
        {
            new(Identity("a", "A", Skill("a1", 12, 12, 3)), 45),
            new(Identity("b", "B", Skill("b1", 12, 12, 3)), 45),
        };
        var enemy = Enemy("e", "Enemy", Skill("e1", 10, 10, 3), Skill("e2", 10, 10, 3));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        Assert.Empty(report.Unblocked);
    }

    [Fact]
    public void OffersTheBestRemainingSkillAsFallback()
    {
        var solver = new TurnSolver();
        var don = Identity("don", "Don Quixote", Skill("d1", 3, 3, 1), Skill("d2", 9, 9, 2), Skill("d3", 6, 6, 2));
        var units = new List<TurnUnit> { new(don, 30) };
        var enemy = Enemy("e", "Dummy", Skill("e1", 4, 3, 1));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        var move = Assert.Single(report.Moves);
        Assert.Equal(2, move.SkillNumber);
        Assert.NotNull(move.Alternative);
        Assert.Equal(3, move.Alternative!.SkillNumber);
    }

    [Fact]
    public void CarriesTheSkillLookAndResistanceContext()
    {
        var solver = new TurnSolver();
        var resistances = new ResistanceSet(
            new Dictionary<DamageType, double> { [DamageType.Slash] = 2.0 },
            new Dictionary<SinType, double>());
        var enemy = new EnemyData("e", "Dummy", 30, 0, resistances, [Skill("e1", 4, 3, 1)]);
        var units = new List<TurnUnit> { new(Identity("a", "A", Skill("a1", 8, 8, 2)), 0) };
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        var move = Assert.Single(report.Moves);
        Assert.Equal(DamageType.Slash, move.DamageType);
        Assert.Equal(2, move.CoinCount);
        Assert.Equal(2.0, move.PhysicalMultiplier);
    }

    [Fact]
    public void SuggestsAGuarderForUnblockedThreats()
    {
        var solver = new TurnSolver();
        var guardSkill = new SkillData("g1", "Hunker Down", 6, 2, 1, SinType.Sloth, DamageType.Guard, 30);
        var guarder = new IdentityData("b", "B", "Ryoshu", 30, [Skill("b1", 2, 2, 1), guardSkill]);
        var units = new List<TurnUnit>
        {
            new(Identity("a", "A", Skill("a1", 8, 8, 2)), 30),
            new(guarder, 0),
        };
        var enemy = Enemy("e", "Enemy", Skill("e1", 6, 5, 2), Skill("e2", 6, 5, 2), Skill("e3", 6, 5, 2));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        Assert.NotEmpty(report.Unblocked);
        Assert.Equal("Ryoshu", report.Unblocked[0].SuggestedGuarder);
        Assert.Equal(2, report.Unblocked[0].GuardSkillNumber);
    }

    [Fact]
    public void DefensiveSkillsAreNotCountedAsThreats()
    {
        var solver = new TurnSolver();
        var units = new List<TurnUnit> { new(Identity("a", "A", Skill("a1", 6, 5, 2)), 20) };
        var enemy = Enemy("e", "Enemy", Skill("guard", 0, 0, 1, DamageType.Guard));
        var report = BestMoveAdvisor.Advise(solver, units, new[] { enemy });
        Assert.Empty(report.Unblocked);
    }
}
