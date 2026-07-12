using OpenCvSharp;

namespace Tsundosika.LimbusAssistant.Vision;

public static class HighlightScanner
{
    public static readonly NormalizedRect FieldBand = new(0.05, 0.20, 0.90, 0.60);

    public static PixelRect? FindHighlightedUnit(Mat frameBgra, PixelRect content)
    {
        var band = FieldBand.ToPixelsWithin(content);
        if (band.Width < 32 || band.Height < 32)
        {
            return null;
        }
        using var view = frameBgra[new Rect(band.X, band.Y, band.Width, band.Height)];
        using var bgr = new Mat();
        Cv2.CvtColor(view, bgr, ColorConversionCodes.BGRA2BGR);
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(20, 140, 180), new Scalar(36, 255, 255), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Rect? best = null;
        var bestArea = 0;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Height < 60 || rect.Height > 380 || rect.Width < 20 || rect.Width > 380 || rect.Height <= rect.Width)
            {
                continue;
            }
            var area = rect.Width * rect.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = rect;
            }
        }
        if (best is not { } found)
        {
            return null;
        }
        return new PixelRect(band.X + found.X, band.Y + found.Y, found.Width, found.Height);
    }
}
