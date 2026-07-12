namespace Tsundosika.LimbusAssistant.Vision;

public static class ClashGate
{
    public const double DefaultThreshold = 0.0015;

    public static readonly NormalizedRect SampleBand = new(0.05, 0.02, 0.90, 0.58);

    public static double MeasureSignal(CaptureFrame frame, PixelRect content)
    {
        var band = SampleBand.ToPixelsWithin(content);
        var maxY = Math.Min(frame.Height, band.Y + band.Height);
        var maxX = Math.Min(frame.Width, band.X + band.Width);
        var total = 0;
        var matching = 0;
        for (var y = Math.Max(0, band.Y); y < maxY; y += 6)
        {
            var rowOffset = y * frame.Stride;
            for (var x = Math.Max(0, band.X); x < maxX; x += 6)
            {
                var index = rowOffset + x * 4;
                var blue = frame.PixelsBgra[index];
                var green = frame.PixelsBgra[index + 1];
                var red = frame.PixelsBgra[index + 2];
                total++;
                var ribbonBlue = blue > 140 && blue > red + 60 && green > 40 && green < blue;
                var ribbonGold = red > 170 && green > 120 && blue < 100 && red > blue + 90;
                var ribbonRed = red > 160 && green < 90 && blue < 90;
                var ribbonViolet = red > 110 && blue > 150 && green < 100;
                if (ribbonBlue || ribbonGold || ribbonRed || ribbonViolet)
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
