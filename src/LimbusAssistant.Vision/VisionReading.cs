namespace Tsundosika.LimbusAssistant.Vision;

public sealed record VisionReading(
    IReadOnlyDictionary<string, NumberReading> Numbers,
    IReadOnlyDictionary<string, IconReading> Icons,
    int FrameWidth,
    int FrameHeight,
    DateTimeOffset Timestamp)
{
    public static VisionReading Empty { get; } = new(
        new Dictionary<string, NumberReading>(),
        new Dictionary<string, IconReading>(),
        0,
        0,
        DateTimeOffset.MinValue);

    public NumberReading Number(string regionName) => Numbers.GetValueOrDefault(regionName, NumberReading.Unknown);

    public IconReading Icon(string regionName) => Icons.GetValueOrDefault(regionName, IconReading.Unknown);

    public double OverallConfidence
    {
        get
        {
            var confidences = Numbers.Values.Select(reading => reading.Confidence)
                .Concat(Icons.Values.Select(reading => reading.Confidence))
                .ToList();
            return confidences.Count == 0 ? 0 : confidences.Average();
        }
    }
}
