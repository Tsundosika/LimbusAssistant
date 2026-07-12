namespace Tsundosika.LimbusAssistant;

public sealed record TickMetrics(double AverageTickMilliseconds, double LastTickMilliseconds, int OcrCallsLastTick)
{
    public static TickMetrics Empty { get; } = new(0, 0, 0);
}
