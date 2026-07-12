namespace Tsundosika.LimbusAssistant.Vision;

public sealed record TextReading(string Text, double Confidence)
{
    public static TextReading Empty { get; } = new("", 0);
}
