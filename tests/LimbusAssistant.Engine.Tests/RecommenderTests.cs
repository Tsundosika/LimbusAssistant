using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class RecommenderTests
{
    static SkillData Skill(string id, int basePower, int coinPower, int coinCount, SinType sin = SinType.Wrath) =>
        new(id, id, basePower, coinPower, coinCount, sin, DamageType.Slash, 30);

    static EnemyData Enemy(ResistanceSet resistances) =>
        new("enemy", "Enemy", 30, 0, resistances, [Skill("enemy-skill", 5, 3, 1)]);

    static ResistanceSet Neutral => new(
        new Dictionary<DamageType, double>(),
        new Dictionary<SinType, double>());

    [Fact]
    public void StrongerSkillRanksFirst()
    {
        var recommender = new Recommender();
        var enemy = Enemy(Neutral);
        var threat = new EnemyThreat(enemy, enemy.Skills[0]);
        var weak = new ClashCandidate("Sinner", Skill("weak", 2, 2, 1), 0, 0);
        var strong = new ClashCandidate("Sinner", Skill("strong", 8, 8, 2), 0, 1);
        var ranked = recommender.Rank([weak, strong], [threat]);
        Assert.Equal("strong", ranked[0].Ally.Skill.Id);
    }

    [Fact]
    public void SinWeaknessBoostsRanking()
    {
        var recommender = new Recommender();
        var resistances = new ResistanceSet(
            new Dictionary<DamageType, double>(),
            new Dictionary<SinType, double> { [SinType.Gloom] = 2.0 });
        var enemy = Enemy(resistances);
        var threat = new EnemyThreat(enemy, enemy.Skills[0]);
        var neutralSin = new ClashCandidate("Sinner", Skill("neutral", 4, 7, 1, SinType.Wrath), 0, 0);
        var fatalSin = new ClashCandidate("Sinner", Skill("fatal", 4, 7, 1, SinType.Gloom), 0, 1);
        var ranked = recommender.Rank([neutralSin, fatalSin], [threat]);
        Assert.Equal("fatal", ranked[0].Ally.Skill.Id);
        Assert.True(ranked[0].ExpectedDamage > ranked[1].ExpectedDamage);
    }

    [Fact]
    public void RespectsTopCount()
    {
        var recommender = new Recommender();
        var enemy = Enemy(Neutral);
        var threats = enemy.Skills.Select(skill => new EnemyThreat(enemy, skill)).ToList();
        var allies = Enumerable.Range(0, 10)
            .Select(i => new ClashCandidate("Sinner", Skill($"skill-{i}", 4 + i, 2, 1), 0, i))
            .ToList();
        var ranked = recommender.Rank(allies, threats, 3);
        Assert.Equal(3, ranked.Count);
    }

    [Fact]
    public void ScoresDescend()
    {
        var recommender = new Recommender();
        var enemy = Enemy(Neutral);
        var threat = new EnemyThreat(enemy, enemy.Skills[0]);
        var allies = Enumerable.Range(0, 5)
            .Select(i => new ClashCandidate("Sinner", Skill($"skill-{i}", 3 + i, 3, 2), 0, i))
            .ToList();
        var ranked = recommender.Rank(allies, [threat], 5);
        for (var i = 1; i < ranked.Count; i++)
        {
            Assert.True(ranked[i - 1].Score >= ranked[i].Score);
        }
    }
}
