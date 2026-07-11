using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class ClashCalculatorTests
{
    readonly ClashCalculator _calculator = new();

    [Fact]
    public void MirroredSkillsClashAtFiftyPercent()
    {
        var skill = new ClashSkill(4, 7, 2, 0);
        var outcome = _calculator.Calculate(skill, skill);
        Assert.Equal(0.5, outcome.WinProbability, 10);
        Assert.Equal(0.5, outcome.LoseProbability, 10);
        Assert.Equal(0, outcome.UnresolvedProbability, 10);
    }

    [Fact]
    public void EnemyWithoutCoinsLosesOutright()
    {
        var ally = new ClashSkill(4, 7, 2, 0);
        var enemy = new ClashSkill(20, 5, 0, 0);
        var outcome = _calculator.Calculate(ally, enemy);
        Assert.Equal(1.0, outcome.WinProbability, 10);
        Assert.Equal(2, outcome.ExpectedCoinsOnWin, 10);
    }

    [Fact]
    public void StrictlyStrongerConstantSkillAlwaysWins()
    {
        var ally = new ClashSkill(10, 0, 2, 0);
        var enemy = new ClashSkill(5, 0, 2, 0);
        var outcome = _calculator.Calculate(ally, enemy);
        Assert.Equal(1.0, outcome.WinProbability, 10);
        Assert.Equal(2, outcome.ExpectedCoinsOnWin, 10);
    }

    [Fact]
    public void IdenticalConstantSkillsNeverResolve()
    {
        var skill = new ClashSkill(5, 0, 1, 0);
        var outcome = _calculator.Calculate(skill, skill);
        Assert.Equal(0, outcome.WinProbability, 10);
        Assert.Equal(0, outcome.LoseProbability, 10);
        Assert.Equal(1, outcome.UnresolvedProbability, 10);
        Assert.Equal(0.5, outcome.EffectiveWinProbability, 10);
    }

    [Fact]
    public void SingleCoinDuelMatchesHandComputation()
    {
        var ally = new ClashSkill(4, 7, 1, 0);
        var enemy = new ClashSkill(5, 3, 1, 0);
        var outcome = _calculator.Calculate(ally, enemy);
        Assert.Equal(0.5, outcome.WinProbability, 10);
        Assert.Equal(0.5, outcome.LoseProbability, 10);
    }

    [Fact]
    public void HigherSanityImprovesWinRate()
    {
        var enemy = new ClashSkill(4, 7, 2, 0);
        var neutral = _calculator.Calculate(new ClashSkill(4, 7, 2, 0), enemy);
        var confident = _calculator.Calculate(new ClashSkill(4, 7, 2, 45), enemy);
        Assert.True(confident.WinProbability > neutral.WinProbability);
    }

    [Fact]
    public void MoreCoinsGrantAttritionAdvantage()
    {
        var manyCoins = new ClashSkill(4, 4, 3, 0);
        var oneCoin = new ClashSkill(4, 4, 1, 0);
        var outcome = _calculator.Calculate(manyCoins, oneCoin);
        Assert.True(outcome.WinProbability > 0.5);
    }

    [Fact]
    public void SingleExchangeSplitsProbabilitiesCorrectly()
    {
        var left = new ClashSkill(4, 7, 1, 0);
        var right = new ClashSkill(4, 7, 1, 0);
        var exchange = ClashCalculator.SingleExchange(left, right);
        Assert.Equal(0.25, exchange.LeftWin, 10);
        Assert.Equal(0.5, exchange.Draw, 10);
        Assert.Equal(0.25, exchange.RightWin, 10);
    }

    [Fact]
    public void ProbabilitiesSumToOne()
    {
        var ally = new ClashSkill(6, 2, 3, 10);
        var enemy = new ClashSkill(5, 8, 2, -15);
        var outcome = _calculator.Calculate(ally, enemy);
        Assert.Equal(1.0, outcome.WinProbability + outcome.LoseProbability + outcome.UnresolvedProbability, 6);
    }

    [Fact]
    public void LargeClashCompletesQuickly()
    {
        var ally = new ClashSkill(10, 4, 20, 20);
        var enemy = new ClashSkill(12, 3, 20, -10);
        var outcome = _calculator.Calculate(ally, enemy);
        Assert.InRange(outcome.WinProbability, 0, 1);
    }
}
