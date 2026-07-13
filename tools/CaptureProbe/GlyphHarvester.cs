using OpenCvSharp;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.CaptureProbe;

public static class GlyphHarvester
{
    public static int Harvest(Mat frameBgra, PixelRect content, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var saved = 0;
        var circles = DockScanner.FindSanityCircles(frameBgra, content);
        for (var i = 0; i < circles.Count; i++)
        {
            saved += ExtractComponents(frameBgra, circles[i], WhiteMask, Path.Combine(outputDirectory, $"sp{i}"));
        }
        var dockBand = new NormalizedRect(0.15, 0.88, 0.75, 0.12).ToPixelsWithin(content);
        saved += ExtractComponents(frameBgra, dockBand, OrangeMask, Path.Combine(outputDirectory, "hp"));
        var fieldBand = new NormalizedRect(0.05, 0.25, 0.90, 0.50).ToPixelsWithin(content);
        saved += ExtractComponents(frameBgra, fieldBand, OrangeMask, Path.Combine(outputDirectory, "fx"));
        return saved;
    }

    static int ExtractComponents(Mat frameBgra, PixelRect region, Func<Mat, Mat> maskBuilder, string prefix)
    {
        var x = Math.Clamp(region.X, 0, Math.Max(0, frameBgra.Width - 2));
        var y = Math.Clamp(region.Y, 0, Math.Max(0, frameBgra.Height - 2));
        var width = Math.Clamp(region.Width, 2, frameBgra.Width - x);
        var height = Math.Clamp(region.Height, 2, frameBgra.Height - y);
        using var view = frameBgra[new Rect(x, y, width, height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        using var mask = maskBuilder(bgr);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var saved = 0;
        var boxes = contours
            .Select(Cv2.BoundingRect)
            .Where(rect => rect.Height is >= 12 and <= 42 && rect.Width is >= 4 and <= 34)
            .OrderBy(rect => rect.X)
            .ToList();
        foreach (var box in boxes)
        {
            using var glyph = mask[box];
            using var normalized = new Mat();
            Cv2.Resize(glyph, normalized, new Size(24, 32), interpolation: InterpolationFlags.Cubic);
            Cv2.Threshold(normalized, normalized, 127, 255, ThresholdTypes.Binary);
            Cv2.ImWrite($"{prefix}_x{x + box.X}_y{y + box.Y}.png", normalized);
            saved++;
        }
        return saved;
    }

    static Mat WhiteMask(Mat bgr)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 175), new Scalar(180, 110, 255), mask);
        return mask;
    }

    static Mat OrangeMask(Mat bgr)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(3, 130, 150), new Scalar(22, 255, 255), mask);
        return mask;
    }
}
