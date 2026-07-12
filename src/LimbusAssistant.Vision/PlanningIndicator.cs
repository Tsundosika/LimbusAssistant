namespace Tsundosika.LimbusAssistant.Vision;

public static class PlanningIndicator
{
    const double Threshold = 0.02;

    static readonly NormalizedRect[] ToggleBands =
    [
        new(0.72, 0.71, 0.10, 0.12),
        new(0.72, 0.89, 0.10, 0.10),
    ];

    public static bool IsPlanningVisible(CaptureFrame frame, PixelRect content) =>
        MeasureBands(frame, content).Any(signal => signal >= Threshold);

    public static double[] MeasureBands(CaptureFrame frame, PixelRect content) =>
        ToggleBands.Select(band => MeasureBand(frame, band.ToPixelsWithin(content))).ToArray();

    static double MeasureBand(CaptureFrame frame, PixelRect band)
    {
        var maxY = Math.Min(frame.Height, band.Y + band.Height);
        var maxX = Math.Min(frame.Width, band.X + band.Width);
        var total = 0;
        var matching = 0;
        for (var y = Math.Max(0, band.Y); y < maxY; y += 3)
        {
            var rowOffset = y * frame.Stride;
            for (var x = Math.Max(0, band.X); x < maxX; x += 3)
            {
                var index = rowOffset + x * 4;
                var blue = frame.PixelsBgra[index];
                var green = frame.PixelsBgra[index + 1];
                var red = frame.PixelsBgra[index + 2];
                total++;
                if (red > 90 && green < 110 && blue < 110 && red > green + 40 && red > blue + 40)
                {
                    matching++;
                }
            }
        }
        return total == 0 ? 0 : matching / (double)total;
    }
}
