using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class DamageCalculatorTests
{
    [Theory]
    [InlineData(2.0, 1.0)]
    [InlineData(1.5, 0.5)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.75, -0.125)]
    [InlineData(0.5, -0.25)]
    [InlineData(0.0, -0.5)]
    public void ResistanceTiersMapToStaticModifiers(double multiplier, double expected)
    {
        Assert.Equal(expected, DamageCalculator.SinResistanceModifier(multiplier), 10);
    }

    [Theory]
    [InlineData(StaggerLevel.Staggered, 1.0)]
    [InlineData(StaggerLevel.StaggeredPlus, 1.5)]
    [InlineData(StaggerLevel.StaggeredPlusPlus, 2.0)]
    public void StaggerOverridesPhysicalResistance(StaggerLevel stagger, double expected)
    {
        Assert.Equal(expected, DamageCalculator.PhysicalResistanceModifier(0.5, stagger), 10);
    }

    [Fact]
    public void UnstaggeredPhysicalResistanceUsesTierMapping()
    {
        Assert.Equal(0.5, DamageCalculator.PhysicalResistanceModifier(1.5, StaggerLevel.None), 10);
    }

    [Fact]
    public void EqualLevelsGiveNoAdvantage()
    {
        Assert.Equal(0, DamageCalculator.OffenseAdvantage(30, 30), 10);
    }

    [Fact]
    public void ThreeLevelGapIsAboutTenPercent()
    {
        Assert.Equal(3 / 28.0, DamageCalculator.OffenseAdvantage(28, 25), 10);
    }

    [Fact]
    public void NeutralContextPassesRollThrough()
    {
        var context = new DamageContext(1.0, 1.0, StaggerLevel.None, 30, 30, 0);
        Assert.Equal(10, DamageCalculator.FinalDamage(10, context), 10);
    }

    [Fact]
    public void FatalSinDoublesNeutralRoll()
    {
        var context = new DamageContext(2.0, 1.0, StaggerLevel.None, 30, 30, 0);
        Assert.Equal(20, DamageCalculator.FinalDamage(10, context), 10);
    }

    [Fact]
    public void ClashCountAddsThreePercentEach()
    {
        var context = new DamageContext(1.0, 1.0, StaggerLevel.None, 30, 30, 4);
        Assert.Equal(11.2, DamageCalculator.FinalDamage(10, context), 10);
    }

    [Fact]
    public void DamageNeverGoesNegative()
    {
        var context = new DamageContext(0.0, 0.5, StaggerLevel.None, 10, 60, 0);
        Assert.True(DamageCalculator.FinalDamage(10, context) >= 0);
    }
}
