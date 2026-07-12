using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class RibbonScanner
{
    public static readonly NormalizedRect SearchArea = new(0.05, 0.02, 0.90, 0.70);

    public static PixelRect? FindSkillRibbon(Mat frameBgra, PixelRect content)
    {
        var area = SearchArea.ToPixelsWithin(content);
        if (area.Width < 32 || area.Height < 32)
        {
            return null;
        }
        using var view = frameBgra[new Rect(area.X, area.Y, area.Width, area.Height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(100, 130, 110), new Scalar(130, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Rect? best = null;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var aspect = rect.Height == 0 ? 0 : rect.Width / (double)rect.Height;
            if (rect.Width < 110 || rect.Width > 700 || rect.Height < 22 || rect.Height > 90 || aspect < 2.0)
            {
                continue;
            }
            if (best is not { } current
                || rect.Y > current.Y
                || (rect.Y == current.Y && rect.Width * rect.Height > current.Width * current.Height))
            {
                best = rect;
            }
        }
        if (best is not { } found)
        {
            return null;
        }
        return new PixelRect(
            Math.Max(0, area.X + found.X - 6),
            Math.Max(0, area.Y + found.Y - 6),
            found.Width + 12,
            found.Height + 12);
    }
}
