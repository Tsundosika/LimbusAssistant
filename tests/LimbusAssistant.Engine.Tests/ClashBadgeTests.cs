using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class ClashBadgeTests
{
    [Theory]
    [InlineData(0.8, "✅", "TAKE IT")]
    [InlineData(0.65, "✅", "GOOD")]
    [InlineData(0.5, "⚠️", "COIN FLIP")]
    [InlineData(0.3, "❌", "FIND BETTER")]
    public void VerdictMapsWinChanceToBigWords(double probability, string icon, string word)
    {
        Assert.Equal((icon, word), ClashBadge.Verdict(probability));
    }

    [Theory]
    [InlineData(0.8, "NIMM ES")]
    [InlineData(0.5, "MÜNZWURF")]
    [InlineData(0.3, "SUCH WAS BESSERES")]
    public void GermanVerdicts(double probability, string word)
    {
        Assert.Equal(word, ClashBadge.Verdict(probability, "de").Word);
    }

    [Fact]
    public void BetterTargetShownOnlyWithAMeaningfulMargin()
    {
        Assert.Null(ClashBadge.BetterTargetHint(0.60, 0.65, "Claw"));
        var hint = ClashBadge.BetterTargetHint(0.50, 0.70, "Claw");
        Assert.NotNull(hint);
        Assert.Contains("Claw", hint);
        Assert.Contains("Better", hint);
    }

    [Fact]
    public void GermanBetterTargetHint()
    {
        var hint = ClashBadge.BetterTargetHint(0.40, 0.70, "Claw", "de");
        Assert.NotNull(hint);
        Assert.Contains("Besser", hint);
    }

    [Fact]
    public void AnswerLineNamesTheBestAnswer()
    {
        Assert.Equal("Answer with Gregor: Lance", ClashBadge.AnswerWith("Gregor: Lance"));
        Assert.Equal("Antwort: Gregor: Lance", ClashBadge.AnswerWith("Gregor: Lance", "de"));
    }
}
