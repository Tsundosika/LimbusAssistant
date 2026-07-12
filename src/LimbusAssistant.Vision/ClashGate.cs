namespace Tsundosika.LimbusAssistant.Vision;

public static class ClashGate
{
    public const double DefaultThreshold = 0.05;

    public static readonly NormalizedRect SampleBand = new(0.355, 0.155, 0.125, 0.055);

    public static double MeasureSignal(CaptureFrame frame, PixelRect content)
    {
        var band = SampleBand.ToPixelsWithin(content);
        var maxY = Math.Min(frame.Height, band.Y + band.Height);
        var maxX = Math.Min(frame.Width, band.X + band.Width);
        var total = 0;
        var matching = 0;
        for (var y = Math.Max(0, band.Y); y < maxY; y += 2)
        {
            var rowOffset = y * frame.Stride;
            for (var x = Math.Max(0, band.X); x < maxX; x += 2)
            {
                var index = rowOffset + x * 4;
                var blue = frame.PixelsBgra[index];
                var green = frame.PixelsBgra[index + 1];
                var red = frame.PixelsBgra[index + 2];
                total++;
                var ribbonBlue = blue > 140 && blue > red + 60 && green > 40 && green < blue;
                if (ribbonBlue)
                {
                    matching++;
                }
            }
        }
        return total == 0 ? 0 : matching / (double)total;
    }

    public static bool IsClashLikely(CaptureFrame frame, PixelRect content, double threshold = DefaultThreshold) =>
        MeasureSignal(frame, content) >= threshold;
}
