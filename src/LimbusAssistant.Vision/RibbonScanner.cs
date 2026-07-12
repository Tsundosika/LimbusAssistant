using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class RibbonScanner
{
    public static readonly NormalizedRect SearchArea = new(0.05, 0.02, 0.90, 0.86);

    public static PixelRect? FindSkillRibbon(Mat frameBgra, PixelRect content) =>
        FindSkillRibbons(frameBgra, content).FirstOrDefault() is { Width: > 0 } first ? first : null;

    public static List<PixelRect> FindSkillRibbons(Mat frameBgra, PixelRect content)
    {
        var area = SearchArea.ToPixelsWithin(content);
        if (area.Width < 32 || area.Height < 32)
        {
            return [];
        }
        using var view = frameBgra[new Rect(area.X, area.Y, area.Width, area.Height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var whiteMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 170), new Scalar(180, 95, 255), whiteMask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 5));
        var candidates = new List<Rect>();
        foreach (var (low, high) in ColorBands)
        {
            using var mask = new Mat();
            Cv2.InRange(hsv, low, high, mask);
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                var aspect = rect.Height == 0 ? 0 : rect.Width / (double)rect.Height;
                if (rect.Width < 110 || rect.Width > 380 || rect.Height < 22 || rect.Height > 110 || aspect < 1.6)
                {
                    continue;
                }
                using var whiteRoi = whiteMask[rect];
                if (Cv2.Mean(whiteRoi).Val0 < 255 * 0.02)
                {
                    continue;
                }
                candidates.Add(rect);
            }
        }
        return candidates
            .OrderByDescending(rect => rect.Y)
            .Take(5)
            .Select(found => new PixelRect(
                Math.Max(0, area.X + found.X - 6),
                Math.Max(0, area.Y + found.Y - 6),
                found.Width + 12,
                found.Height + 12))
            .ToList();
    }

    static readonly (Scalar Low, Scalar High)[] ColorBands =
    [
        (new Scalar(85, 110, 100), new Scalar(130, 255, 255)),
        (new Scalar(10, 110, 130), new Scalar(38, 255, 255)),
        (new Scalar(40, 100, 100), new Scalar(85, 255, 255)),
        (new Scalar(0, 110, 110), new Scalar(10, 255, 255)),
        (new Scalar(168, 110, 110), new Scalar(180, 255, 255)),
        (new Scalar(130, 90, 100), new Scalar(168, 255, 255)),
    ];
}
