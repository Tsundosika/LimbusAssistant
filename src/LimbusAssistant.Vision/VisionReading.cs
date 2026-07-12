namespace Tsundosika.LimbusAssistant.Vision;

public sealed record VisionReading(
    IReadOnlyDictionary<string, NumberReading> Numbers,
    IReadOnlyDictionary<string, IconReading> Icons,
    IReadOnlyDictionary<string, TextReading> Texts,
    IReadOnlyDictionary<string, PixelRect> Regions,
    int FrameWidth,
    int FrameHeight,
    PixelRect ContentRect,
    DateTimeOffset Timestamp)
{
    public static VisionReading Empty { get; } = new(
        new Dictionary<string, NumberReading>(),
        new Dictionary<string, IconReading>(),
        new Dictionary<string, TextReading>(),
        new Dictionary<string, PixelRect>(),
        0,
        0,
        new PixelRect(0, 0, 0, 0),
        DateTimeOffset.MinValue);

    public TextReading Text(string regionName) => Texts.GetValueOrDefault(regionName, TextReading.Empty);

    public NumberReading Number(string regionName) => Numbers.GetValueOrDefault(regionName, NumberReading.Unknown);

    public IconReading Icon(string regionName) => Icons.GetValueOrDefault(regionName, IconReading.Unknown);

    public double OverallConfidence
    {
        get
        {
            var confidences = Numbers.Values.Select(reading => reading.Confidence)
                .Concat(Icons.Values.Select(reading => reading.Confidence))
                .Concat(Texts.Values.Select(reading => reading.Confidence))
                .ToList();
            return confidences.Count == 0 ? 0 : confidences.Average();
        }
    }
}
