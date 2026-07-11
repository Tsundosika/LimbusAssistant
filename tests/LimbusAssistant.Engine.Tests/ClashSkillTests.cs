using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class ClashSkillTests
{
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(45, 0.95)]
    [InlineData(-45, 0.05)]
    [InlineData(100, 0.95)]
    [InlineData(-100, 0.05)]
    public void HeadProbabilityFollowsSanity(int sanity, double expected)
    {
        var skill = new ClashSkill(4, 7, 1, sanity);
        Assert.Equal(expected, skill.HeadProbability, 10);
    }

    [Fact]
    public void SingleCoinDistributionSplitsOnHeads()
    {
        var skill = new ClashSkill(4, 7, 1, 0);
        var distribution = skill.PowerDistribution();
        Assert.Equal(2, distribution.Count);
        Assert.Equal(4, distribution[0].Power);
        Assert.Equal(0.5, distribution[0].Probability, 10);
        Assert.Equal(11, distribution[1].Power);
        Assert.Equal(0.5, distribution[1].Probability, 10);
    }

    [Fact]
    public void NegativeCoinPowerClampsAtZero()
    {
        var skill = new ClashSkill(2, -4, 1, 0);
        var distribution = skill.PowerDistribution();
        Assert.Equal(2, distribution.Count);
        Assert.Equal(0, distribution[0].Power);
        Assert.Equal(0.5, distribution[0].Probability, 10);
        Assert.Equal(2, distribution[1].Power);
    }

    [Fact]
    public void TwoCoinDistributionIsBinomial()
    {
        var skill = new ClashSkill(4, 4, 2, 0);
        var distribution = skill.PowerDistribution();
        Assert.Equal(3, distribution.Count);
        Assert.Equal(4, distribution[0].Power);
        Assert.Equal(0.25, distribution[0].Probability, 10);
        Assert.Equal(8, distribution[1].Power);
        Assert.Equal(0.5, distribution[1].Probability, 10);
        Assert.Equal(12, distribution[2].Power);
        Assert.Equal(0.25, distribution[2].Probability, 10);
    }

    [Fact]
    public void AfterLoseRemovesOneCoin()
    {
        var skill = new ClashSkill(4, 7, 3, 0);
        Assert.Equal(2, skill.AfterLose().CoinCount);
    }
}
