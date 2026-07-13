using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.Vision.Tests;

public class NumberInterpreterTests
{
    [Theory]
    [InlineData("30", 30)]
    [InlineData("-30", -30)]
    [InlineData("+45", 45)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("-45", -45)]
    public void ParsesPlainAndSignedNumbers(string raw, int expected)
    {
        Assert.Equal(expected, NumberInterpreter.Parse(raw).Value);
    }

    [Theory]
    [InlineData(0x2212)]
    [InlineData(0x2013)]
    [InlineData(0x2014)]
    [InlineData(0x2010)]
    public void ParsesUnicodeMinusVariantsAsNegative(int codepoint)
    {
        var reading = NumberInterpreter.Parse((char)codepoint + "30");
        Assert.Equal(-30, reading.Value);
    }

    [Fact]
    public void MinusOnlyCountsWhenItLeadsTheDigits()
    {
        Assert.Equal(30, NumberInterpreter.Parse("3-0").Value);
    }

    [Fact]
    public void ReturnsUnknownForNonNumericText()
    {
        Assert.Null(NumberInterpreter.Parse("hover a skill").Value);
    }

    [Fact]
    public void ParsesRegardlessOfCulture()
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(-12, NumberInterpreter.Parse("-12").Value);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
