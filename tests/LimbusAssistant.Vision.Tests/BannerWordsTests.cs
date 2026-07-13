using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.Vision.Tests;

public class BannerWordsTests
{
    [Theory]
    [InlineData("Keywords")]
    [InlineData("Staggered")]
    [InlineData("staggerd")]
    [InlineData("Skill Effects")]
    public void FlagsNonSkillBanners(string text)
    {
        Assert.True(BannerWords.IsNonSkillBanner(text));
    }

    [Theory]
    [InlineData("Please answer the survey.")]
    [InlineData("Tripleslash")]
    [InlineData("Mad Steed's Voidrender")]
    public void AllowsRealSkillNames(string text)
    {
        Assert.False(BannerWords.IsNonSkillBanner(text));
    }
}
