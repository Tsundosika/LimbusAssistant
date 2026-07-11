using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class ExpectedAttackPowerTests
{
    [Fact]
    public void UnopposedSingleCoinIsBasePlusExpectedHeads()
    {
        var skill = new ClashSkill(4, 7, 1, 0);
        Assert.Equal(7.5, ExpectedAttackPower.Unopposed(skill), 10);
    }

    [Fact]
    public void UnopposedMultiCoinAccumulatesPerHit()
    {
        var skill = new ClashSkill(4, 4, 2, 0);
        Assert.Equal(14, ExpectedAttackPower.Unopposed(skill), 10);
    }

    [Fact]
    public void OnClashWinWeightsStatesByProbability()
    {
        var skill = new ClashSkill(4, 4, 2, 0);
        var states = new List<ClashEndState>
        {
            new(0.5, 2, 0, 0),
            new(0.5, 1, 0, 0),
        };
        var expected = 0.5 * 14 + 0.5 * 6;
        Assert.Equal(expected, ExpectedAttackPower.OnClashWin(skill, states), 10);
    }

    [Fact]
    public void GuaranteedWinKeepsFullPower()
    {
        var calculator = new ClashCalculator();
        var ally = new ClashSkill(4, 7, 2, 0);
        var enemy = new ClashSkill(1, 1, 0, 0);
        var outcome = calculator.Calculate(ally, enemy);
        var hit1 = 4 + 1 * 0.5 * 7;
        var hit2 = 4 + 2 * 0.5 * 7;
        Assert.Equal(hit1 + hit2, ExpectedAttackPower.OnClashWin(ally, outcome.WinStates), 10);
    }
}
