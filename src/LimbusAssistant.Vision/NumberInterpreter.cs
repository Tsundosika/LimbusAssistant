using System.Globalization;

namespace Tsundosika.LimbusAssistant.Vision;

public static class NumberInterpreter
{
    static readonly char[] MinusVariants =
    {
        (char)0x2212, (char)0x2013, (char)0x2014, (char)0x2010, (char)0x2011, (char)0x2012,
    };

    public static NumberReading Parse(string rawText)
    {
        var cleaned = NormalizeMinus(rawText).Replace("O", "0").Replace("o", "0")
            .Replace("l", "1").Replace("I", "1")
            .Replace("S", "5").Replace("s", "5")
            .Replace("B", "8").Replace("b", "6")
            .Replace(".", "").Replace(",", "").Replace("'", "").Replace("\"", "")
            .Replace("`", "").Replace("~", "").Replace(":", "").Replace(";", "")
            .Replace("_", "").Replace("|", "");
        if (cleaned.Count(char.IsAsciiLetter) > 1)
        {
            return new NumberReading(null, 0, rawText);
        }
        var digits = new string(cleaned.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0
            || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude))
        {
            return new NumberReading(null, 0, rawText);
        }
        var value = HasLeadingMinus(cleaned) ? -magnitude : magnitude;
        var meaningful = cleaned.Count(character => !char.IsWhiteSpace(character));
        var confidence = meaningful == 0 ? 0 : 0.95 * digits.Length / meaningful;
        return new NumberReading(value, confidence, rawText);
    }

    static string NormalizeMinus(string text)
    {
        foreach (var variant in MinusVariants)
        {
            text = text.Replace(variant, '-');
        }
        return text;
    }

    static bool HasLeadingMinus(string cleaned)
    {
        foreach (var character in cleaned)
        {
            if (char.IsAsciiDigit(character))
            {
                return false;
            }
            if (character == '-')
            {
                return true;
            }
        }
        return false;
    }
}
