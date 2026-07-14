using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class CoachTextTests
{
    static BestMoveAdvice Move(
        double win = 0.8,
        SinType sin = SinType.Gloom,
        DamageType type = DamageType.Pierce,
        double sinMult = 1.0,
        double physMult = 1.0,
        AlternativeMove? alternative = null) => new(
        "Don Quixote", "W Corp Don", 2, "Lance", false, "Boss", "Big Swing", win, 20, 3,
        sin, type, 2, sinMult, physMult, alternative);

    [Theory]
    [InlineData(0.8, "easy win")]
    [InlineData(0.65, "favored")]
    [InlineData(0.5, "coin flip")]
    [InlineData(0.3, "avoid if you can")]
    public void VerdictMapsProbabilityToPlainWords(double probability, string expected)
    {
        Assert.Equal(expected, CoachText.Verdict(probability));
    }

    [Fact]
    public void InstructionNamesSinnerSkillNumberLookAndTarget()
    {
        var text = CoachText.Instruction(Move(), true);
        Assert.Contains("Don Quixote", text);
        Assert.Contains("Skill 2", text);
        Assert.Contains("light blue Gloom pierce", text);
        Assert.Contains("Big Swing", text);
        Assert.Contains("easy win", text);
        Assert.DoesNotContain("%", text);
    }

    [Fact]
    public void RawModeShowsNumbers()
    {
        var text = CoachText.Instruction(Move(win: 0.8), false);
        Assert.Contains("80", text);
    }

    [Theory]
    [InlineData(2.0, 1.0, "double damage from Gloom")]
    [InlineData(1.5, 1.0, "weak to Gloom")]
    [InlineData(1.0, 1.5, "weak to Pierce")]
    [InlineData(0.5, 1.0, "shrugs off Gloom")]
    [InlineData(1.0, 0.5, "resists Pierce")]
    public void WhyExplainsTheStrongestResistanceSignal(double sinMult, double physMult, string expected)
    {
        var why = CoachText.Why(Move(sinMult: sinMult, physMult: physMult));
        Assert.NotNull(why);
        Assert.Contains(expected, why);
    }

    [Fact]
    public void WhyIsNullForNeutralMatchup()
    {
        Assert.Null(CoachText.Why(Move()));
    }

    [Fact]
    public void FallbackNamesTheAlternativeSkill()
    {
        var move = Move(alternative: new AlternativeMove(1, "Quick Stab", 0.6, 12));
        var text = CoachText.Fallback(move, true);
        Assert.NotNull(text);
        Assert.Contains("Skill 1", text);
        Assert.Contains("Quick Stab", text);
    }

    [Fact]
    public void UnblockedWarningNamesTheGuarder()
    {
        var threat = new UnblockedThreat("Boss", "Wailing Slam", 35, "Ryoshu", 3, "Chop");
        var text = CoachText.UnblockedWarning(threat, true);
        Assert.Contains("Wailing Slam", text);
        Assert.Contains("Ryoshu", text);
        Assert.Contains("Skill 3", text);
    }
}
