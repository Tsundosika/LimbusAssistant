namespace Tsundosika.LimbusAssistant.Vision;

public sealed record NumberReading(int? Value, double Confidence, string RawText)
{
    public static NumberReading Unknown { get; } = new(null, 0, "");
}
