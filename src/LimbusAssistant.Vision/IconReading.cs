namespace Tsundosika.LimbusAssistant.Vision;

public sealed record IconReading(string? Name, double Confidence)
{
    public static IconReading Unknown { get; } = new(null, 0);
}
